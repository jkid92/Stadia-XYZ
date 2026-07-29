using System.Diagnostics;
using HidSharp;
using HidSharp.Reports;
using HidSharp.Reports.Input;

namespace StadiaX.ControlCenter;

internal sealed class WindowsNativeReceiver
{
    private const int MaxControllers = 4;
    private const int StartPhaseCount = 5;
    private const int HidPresenceProbeIntervalMs = 2000;

    private readonly AppPaths _paths;
    private readonly StatusWriter _status;
    private readonly IWindowsNativeControllerScanner _scanner;
    private readonly IVirtualGamepadBusFactory _virtualGamepadFactory;
    private readonly IReadOnlyList<WindowsNativeHidDevice>? _initialDevices;
    private readonly ControllerTelemetryWriter _telemetryWriter;
    private readonly ControllerButtonMappingProvider _mappingProvider;
    private readonly ControllerRumbleSettingsProvider _rumbleSettings;
    private readonly WindowsNativeMacroEngine _macroEngine;
    private readonly object _logLock = new();
    private readonly object _telemetryErrorLock = new();
    private readonly object _controllerInputStateLock = new();
    private readonly object _virtualUpdateStateLock = new();
    private readonly object _rumbleStateLock = new();
    private readonly bool[] _controllerInputsOpen = new bool[MaxControllers];
    private readonly bool[] _virtualUpdateFailed = new bool[MaxControllers];
    private readonly DateTimeOffset[] _nextVirtualUpdateErrorLog = new DateTimeOffset[MaxControllers];
    private readonly long[] _nextRumbleWaitLogTick = new long[MaxControllers];
    private readonly WindowsNativeRumbleWriter?[] _rumbleWriters = new WindowsNativeRumbleWriter?[MaxControllers];

    private IVirtualGamepadBus? _virtualGamepads;
    private int _expectedControllerCount;
    private DateTimeOffset _nextTelemetryErrorLog = DateTimeOffset.MinValue;

    public WindowsNativeReceiver(
        AppPaths paths,
        StatusWriter status,
        IWindowsNativeControllerScanner scanner,
        IReadOnlyList<WindowsNativeHidDevice>? initialDevices = null,
        IVirtualGamepadBusFactory? virtualGamepadFactory = null)
    {
        _paths = paths;
        _status = status;
        _scanner = scanner;
        _initialDevices = initialDevices;
        _virtualGamepadFactory = virtualGamepadFactory ?? new VigemVirtualGamepadBusFactory();
        _telemetryWriter = new ControllerTelemetryWriter(paths);
        _mappingProvider = new ControllerButtonMappingProvider(
            paths.ControllerMapping,
            message => LogInfo("{0}", message),
            message => LogError("{0}", message));
        _rumbleSettings = new ControllerRumbleSettingsProvider(paths.RumbleSettings);
        _macroEngine = new WindowsNativeMacroEngine(paths.MacroConfig, LogInfo, LogError);
    }

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_paths.LogDirectory);
        LogInfo("Windows Native receiver starting");

        try
        {
            var discoveredDevices = (_initialDevices is { Count: > 0 }
                    ? _initialDevices
                    : await _scanner.FindStadiaControllersAsync().ConfigureAwait(false));
            var profiles = new NativeControlServices(_paths, new ProcessRunner()).GetProfiles();
            var devices = NativeControlServices
                .OrderWindowsNativeDevices(discoveredDevices, profiles)
                .Take(MaxControllers)
                .ToArray();
            if (devices.Length == 0)
            {
                _status.Write("WINDOWS_NATIVE_NOT_READY", "No Stadia HID controller is visible to Windows");
                LogInfo("No Stadia HID controller is visible to Windows");
                return 2;
            }

            _expectedControllerCount = devices.Length;
            for (var i = 0; i < devices.Length; i++)
            {
                var rumble = devices[i].MaxOutputReportLength >= WindowsNativeRumbleReport.MinimumLength
                    ? $"ready outputReportLength={devices[i].MaxOutputReportLength}"
                    : $"unavailable outputReportLength={devices[i].MaxOutputReportLength}";
                _status.Write(
                    "WINDOWS_NATIVE_CONTROLLER_CAPABILITIES",
                    $"P{i + 1} battery=windows rumble={rumble} device={devices[i].FriendlyName}");
            }
            InitializeVirtualGamepads(devices.Length);
            _status.WritePhase(
                "Windows Native",
                4,
                StartPhaseCount,
                "Virtual pads",
                "OK",
                $"{devices.Length} virtual Xbox 360 controller slot(s) running");
            _status.WritePhase(
                "Windows Native",
                5,
                StartPhaseCount,
                "Input streaming",
                "START",
                $"Opening input from {devices.Length} Stadia controller(s)");
            _status.Write("WINDOWS_NATIVE_READY", $"Starting Windows Native input for {devices.Length} controller(s)");
            WriteReadyMarker(devices.Length);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var controllerTasks = devices
                .Select((device, index) => Task.Run(() => RunControllerAsync(index, device, linked.Token), linked.Token))
                .ToArray();
            var rumbleServer = new WindowsNativeRumbleUdpServer(
                controllerIndex => controllerIndex >= 0 && controllerIndex < _rumbleWriters.Length ? _rumbleWriters[controllerIndex] : null,
                LogInfo,
                LogError);
            var tasks = controllerTasks
                .Append(Task.Run(() => rumbleServer.RunAsync(linked.Token), linked.Token))
                .ToArray();

            try
            {
                var shutdownTask = Task.Delay(Timeout.InfiniteTimeSpan, linked.Token);
                var completed = await Task.WhenAny(tasks.Append(shutdownTask)).ConfigureAwait(false);
                if (completed == shutdownTask || cancellationToken.IsCancellationRequested)
                {
                    linked.Cancel();
                    try
                    {
                        await Task.WhenAll(tasks).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (linked.IsCancellationRequested)
                    {
                    }
                    catch (Exception ex) when (linked.IsCancellationRequested)
                    {
                        LogError("Windows Native worker shutdown reported after stop: {0}", ex.Message);
                    }

                    return 0;
                }

                var workerIndex = Array.IndexOf(tasks, completed);
                var workerName = workerIndex >= 0 && workerIndex < controllerTasks.Length
                    ? $"P{workerIndex + 1} controller loop"
                    : "rumble server";
                var workerFailure = completed.Exception?.GetBaseException();
                linked.Cancel();
                try
                {
                    await Task.WhenAll(tasks).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (linked.IsCancellationRequested)
                {
                }
                catch (Exception ex) when (workerFailure is not null)
                {
                    LogError("Worker shutdown after {0} failure also reported: {1}", workerName, ex.Message);
                }

                if (workerFailure is not null)
                {
                    throw new InvalidOperationException($"Windows Native {workerName} failed: {workerFailure.Message}", workerFailure);
                }

                throw new InvalidOperationException($"Windows Native {workerName} stopped unexpectedly.");
            }
            catch (OperationCanceledException) when (linked.IsCancellationRequested)
            {
                return 0;
            }
            catch (Exception ex)
            {
                linked.Cancel();
                _status.Write("WINDOWS_NATIVE_FAILED", ex.Message);
                LogError("Windows Native receiver failed: {0}", ex.Message);
                return 1;
            }
        }
        catch (Exception ex)
        {
            _status.Write("WINDOWS_NATIVE_START_FAILED", ex.Message);
            LogError("Windows Native startup failed: {0}", ex.Message);
            return 1;
        }
        finally
        {
            DeleteReadyMarker();
            ClearControllerTelemetry();
            CleanupVirtualGamepads();
            _macroEngine.Dispose();
            LogInfo("Windows Native receiver stopped");
        }
    }

    private void ClearControllerTelemetry()
    {
        var cleanup = WindowsNativeRuntime.ClearControllerStateFiles(_paths);
        foreach (var file in cleanup.Removed)
        {
            LogInfo("Controller telemetry cleanup removed {0}", file);
        }

        foreach (var warning in cleanup.Warnings)
        {
            LogError("Controller telemetry cleanup failed for {0}", warning);
        }
    }

    private async Task RunControllerAsync(int controllerIndex, WindowsNativeHidDevice device, CancellationToken cancellationToken)
    {
        var currentDevice = device;
        var connectedOnce = false;
        var reconnectAttempt = 0;
        var neutralized = false;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var resolved = await ResolveCurrentHidDeviceAsync(currentDevice).ConfigureAwait(false);
                if (resolved is null)
                {
                    throw new IOException($"{currentDevice.FriendlyName} is not visible to Windows HID");
                }

                currentDevice = resolved.Value.Descriptor;
                var hidDevice = resolved.Value.Device;
                using var stream = hidDevice.Open();
                stream.ReadTimeout = 500;
                stream.WriteTimeout = 80;
                var mapper = new WindowsNativeHidMapper(hidDevice);
                var rumbleWriter = new WindowsNativeRumbleWriter(
                    controllerIndex + 1,
                    hidDevice,
                    stream,
                    _status,
                    () => _rumbleSettings.IsEnabled(controllerIndex),
                    LogInfo,
                    LogError);
                var buffer = new byte[Math.Max(1, hidDevice.GetMaxInputReportLength())];
                var nextPresenceProbe = Environment.TickCount64 + HidPresenceProbeIntervalMs;
                Interlocked.Exchange(ref _rumbleWriters[controllerIndex], rumbleWriter)?.Dispose();
                lock (_rumbleStateLock)
                {
                    _nextRumbleWaitLogTick[controllerIndex] = 0;
                }
                reconnectAttempt = 0;
                neutralized = false;
                _status.Write(
                    connectedOnce ? "WINDOWS_NATIVE_CONTROLLER_RECONNECTED" : "WINDOWS_NATIVE_CONTROLLER_OPEN",
                    $"P{controllerIndex + 1}: {currentDevice.FriendlyName}");
                _status.Write(
                    rumbleWriter.IsSupported ? "WINDOWS_NATIVE_RUMBLE_READY" : "WINDOWS_NATIVE_RUMBLE_UNAVAILABLE",
                    $"P{controllerIndex + 1} rumble={(rumbleWriter.IsSupported ? "ready" : "unavailable")} " +
                    $"route=ViGEm-to-Stadia-HID outputReportLength={rumbleWriter.OutputReportLength}");
                LogInfo(
                    connectedOnce ? "P{0} Windows Native HID reconnected: {1}" : "P{0} Windows Native HID open: {1}",
                    controllerIndex + 1,
                    currentDevice.FriendlyName);
                ReportControllerInputState(controllerIndex, open: true);
                connectedOnce = true;

                try
                {
                    while (!cancellationToken.IsCancellationRequested)
                    {
                        int read;
                        try
                        {
                            Array.Clear(buffer);
                            read = stream.Read(buffer);
                        }
                        catch (TimeoutException)
                        {
                            var now = Environment.TickCount64;
                            if (now >= nextPresenceProbe)
                            {
                                nextPresenceProbe = now + HidPresenceProbeIntervalMs;
                                if (FindHidDevice(currentDevice.FileSystemName) is null)
                                {
                                    throw new IOException($"{currentDevice.FriendlyName} disappeared from Windows HID");
                                }
                            }

                            continue;
                        }

                        if (read <= 0)
                        {
                            await Task.Delay(10, cancellationToken).ConfigureAwait(false);
                            continue;
                        }

                        if (!mapper.TryParse(buffer.AsSpan(0, read), out var state))
                        {
                            continue;
                        }

                        var outputState = _macroEngine.ProcessState(controllerIndex, state);
                        var virtualGamepads = _virtualGamepads;
                        var updateError = "bus unavailable";
                        if (virtualGamepads is null ||
                            !virtualGamepads.TryUpdate(
                                controllerIndex,
                                ControllerStateMapper.ToXusb(outputState, _mappingProvider.GetCurrent()),
                                out updateError))
                        {
                            ReportVirtualUpdateFailure(controllerIndex, updateError);
                        }
                        else
                        {
                            ReportVirtualUpdateRecovered(controllerIndex);
                        }

                        WriteTelemetrySafely(controllerIndex, state);
                    }
                }
                finally
                {
                    _macroEngine.ResetController(controllerIndex);
                    Interlocked.Exchange(ref _rumbleWriters[controllerIndex], null)?.Dispose();
                    if (!cancellationToken.IsCancellationRequested)
                    {
                        ReportControllerInputState(controllerIndex, open: false);
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                reconnectAttempt++;
                if (!neutralized)
                {
                    NeutralizeVirtualPad(controllerIndex);
                    neutralized = true;
                }

                if (reconnectAttempt == 1 || reconnectAttempt % 5 == 0)
                {
                    _status.Write(
                        "WINDOWS_NATIVE_CONTROLLER_RECONNECT_WAIT",
                        $"P{controllerIndex + 1}: attempt={reconnectAttempt} error={ex.Message}");
                    LogError(
                        "P{0} Windows Native HID unavailable; reconnect attempt {1}: {2}",
                        controllerIndex + 1,
                        reconnectAttempt,
                        ex.Message);
                }

                var retryDelay = TimeSpan.FromMilliseconds(Math.Min(5000, 750 * reconnectAttempt));
                await Task.Delay(retryDelay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private void ReportControllerInputState(int controllerIndex, bool open)
    {
        int opened;
        int expected;
        lock (_controllerInputStateLock)
        {
            if (_controllerInputsOpen[controllerIndex] == open)
            {
                return;
            }

            _controllerInputsOpen[controllerIndex] = open;
            opened = _controllerInputsOpen.Count(value => value);
            expected = Math.Max(1, _expectedControllerCount);
        }

        if (!open)
        {
            _status.Write(
                "WINDOWS_NATIVE_CONTROLLER_INPUT_LOST",
                $"P{controllerIndex + 1} input=disconnected active={opened}/{expected}");
        }

        if (opened < expected)
        {
            _status.WritePhase(
                "Windows Native",
                5,
                StartPhaseCount,
                "Input streaming",
                open ? "START" : "WAIT",
                $"{opened}/{expected} Stadia controller input stream(s) active");
            return;
        }

        _status.WritePhase(
            "Windows Native",
            5,
            StartPhaseCount,
            "Input streaming",
            "OK",
            $"{expected} Stadia controller input stream(s) active");
        _status.Write("WINDOWS_NATIVE_INPUT_READY", $"Input streaming active for {expected} controller(s)");
    }

    private async Task<(WindowsNativeHidDevice Descriptor, HidDevice Device)?> ResolveCurrentHidDeviceAsync(WindowsNativeHidDevice expected)
    {
        var direct = FindHidDevice(expected.FileSystemName);
        if (direct is not null)
        {
            return (expected, direct);
        }

        var currentDevices = await _scanner.FindStadiaControllersAsync().ConfigureAwait(false);
        var descriptor = currentDevices.FirstOrDefault(candidate => SameControllerIdentity(expected, candidate));
        if (descriptor is null)
        {
            return null;
        }

        var hidDevice = FindHidDevice(descriptor.FileSystemName);
        return hidDevice is null ? null : (descriptor, hidDevice);
    }

    private static HidDevice? FindHidDevice(string fileSystemName)
    {
        return DeviceList.Local.GetHidDevices()
            .FirstOrDefault(item => item.GetFileSystemName().Equals(fileSystemName, StringComparison.OrdinalIgnoreCase));
    }

    private static bool SameControllerIdentity(WindowsNativeHidDevice expected, WindowsNativeHidDevice candidate)
    {
        if (!string.IsNullOrWhiteSpace(expected.DeviceInstancePath) &&
            !string.IsNullOrWhiteSpace(candidate.DeviceInstancePath))
        {
            return expected.DeviceInstancePath.Equals(candidate.DeviceInstancePath, StringComparison.OrdinalIgnoreCase);
        }

        if (!string.IsNullOrWhiteSpace(expected.HidHideSymbolicLink) &&
            !string.IsNullOrWhiteSpace(candidate.HidHideSymbolicLink))
        {
            return expected.HidHideSymbolicLink.Equals(candidate.HidHideSymbolicLink, StringComparison.OrdinalIgnoreCase);
        }

        return expected.FileSystemName.Equals(candidate.FileSystemName, StringComparison.OrdinalIgnoreCase);
    }

    private void NeutralizeVirtualPad(int controllerIndex)
    {
        var virtualGamepads = _virtualGamepads;
        if (virtualGamepads is null)
        {
            return;
        }

        if (!virtualGamepads.TryNeutralize(controllerIndex, out var error))
        {
            LogError("P{0} virtual controller neutral reset failed: {1}", controllerIndex + 1, error);
        }

        DeactivateTelemetrySafely(controllerIndex);
    }

    private void WriteTelemetrySafely(int controllerIndex, ControllerState state)
    {
        try
        {
            _telemetryWriter.Write(controllerIndex, state);
        }
        catch (Exception ex)
        {
            ReportTelemetryError(controllerIndex, ex);
        }
    }

    private void DeactivateTelemetrySafely(int controllerIndex)
    {
        try
        {
            _telemetryWriter.Deactivate(controllerIndex);
        }
        catch (Exception ex)
        {
            ReportTelemetryError(controllerIndex, ex);
        }
    }

    private void ReportTelemetryError(int controllerIndex, Exception ex)
    {
        var now = DateTimeOffset.UtcNow;
        var shouldLog = false;
        lock (_telemetryErrorLock)
        {
            if (now >= _nextTelemetryErrorLog)
            {
                _nextTelemetryErrorLog = now.AddSeconds(5);
                shouldLog = true;
            }
        }

        if (shouldLog)
        {
            LogError("P{0} controller telemetry write failed: {1}", controllerIndex + 1, ex.Message);
        }
    }

    private void InitializeVirtualGamepads(int targetCount)
    {
        _virtualGamepads = _virtualGamepadFactory.Create(targetCount);
        _virtualGamepads.RumbleRequested += OnRumble;
        foreach (var warning in _virtualGamepads.InitializationWarnings)
        {
            LogError("Virtual controller initialization warning: {0}", warning);
        }

        for (var i = 0; i < _virtualGamepads.ControllerCount; i++)
        {
            LogInfo("P{0} Windows Native virtual Xbox 360 pad ready", i + 1);
        }
    }

    private void ReportVirtualUpdateFailure(int controllerIndex, string error)
    {
        var now = DateTimeOffset.UtcNow;
        var shouldLog = false;
        lock (_virtualUpdateStateLock)
        {
            _virtualUpdateFailed[controllerIndex] = true;
            if (now >= _nextVirtualUpdateErrorLog[controllerIndex])
            {
                _nextVirtualUpdateErrorLog[controllerIndex] = now.AddSeconds(5);
                shouldLog = true;
            }
        }

        if (!shouldLog)
        {
            return;
        }

        var detail = string.IsNullOrWhiteSpace(error) ? "bus unavailable" : error;
        _status.Write(
            "WINDOWS_NATIVE_VIRTUAL_UPDATE_FAILED",
            $"P{controllerIndex + 1}: {detail}; repeated failures are suppressed for 5 seconds");
        LogError("P{0} virtual controller update failed: {1}", controllerIndex + 1, detail);
    }

    private void ReportVirtualUpdateRecovered(int controllerIndex)
    {
        lock (_virtualUpdateStateLock)
        {
            if (!_virtualUpdateFailed[controllerIndex])
            {
                return;
            }

            _virtualUpdateFailed[controllerIndex] = false;
            _nextVirtualUpdateErrorLog[controllerIndex] = DateTimeOffset.MinValue;
        }

        _status.Write(
            "WINDOWS_NATIVE_VIRTUAL_UPDATE_RECOVERED",
            $"P{controllerIndex + 1}: virtual controller updates resumed");
        LogInfo("P{0} virtual controller updates resumed", controllerIndex + 1);
    }

    private void OnRumble(int controllerIndex, byte largeMotor, byte smallMotor)
    {
        if (controllerIndex < 0 || controllerIndex >= _rumbleWriters.Length)
        {
            return;
        }

        var writer = Volatile.Read(ref _rumbleWriters[controllerIndex]);
        if (writer is null)
        {
            ReportRumbleWaitingForHid(controllerIndex, largeMotor, smallMotor);
            return;
        }

        writer.Send(largeMotor, smallMotor);
    }

    private void ReportRumbleWaitingForHid(int controllerIndex, byte largeMotor, byte smallMotor)
    {
        if (largeMotor == 0 && smallMotor == 0)
        {
            return;
        }

        var now = Environment.TickCount64;
        lock (_rumbleStateLock)
        {
            if (now < _nextRumbleWaitLogTick[controllerIndex])
            {
                return;
            }

            _nextRumbleWaitLogTick[controllerIndex] = now + 5000;
        }

        _status.Write(
            "WINDOWS_NATIVE_RUMBLE_WAIT",
            $"P{controllerIndex + 1} rumble=waiting reason=physical HID stream not ready");
        LogInfo(
            "P{0} rumble requested while the physical Stadia HID stream is reconnecting; waiting for HID.",
            controllerIndex + 1);
    }

    private void CleanupVirtualGamepads()
    {
        var virtualGamepads = _virtualGamepads;
        _virtualGamepads = null;
        if (virtualGamepads is null)
        {
            return;
        }

        virtualGamepads.RumbleRequested -= OnRumble;
        virtualGamepads.Dispose();
    }

    private void WriteReadyMarker(int controllerCount)
    {
        Directory.CreateDirectory(_paths.LogDirectory);
        using var process = Process.GetCurrentProcess();
        var path = WindowsNativeRuntime.ReadyPath(_paths);
        var tempPath = WindowsNativeRuntime.ReadyTempPath(_paths);
        File.WriteAllText(
            tempPath,
            $"{DateTimeOffset.Now:O}|pid={Environment.ProcessId}|controllers={controllerCount}|processStartUtc={process.StartTime.ToUniversalTime():O}{Environment.NewLine}");
        File.Move(tempPath, path, overwrite: true);
    }

    private void DeleteReadyMarker()
    {
        WindowsNativeRuntime.ClearReadyMarker(_paths);
    }

    private void LogInfo(string format, params object[] args) => Log("INFO", format, args);

    private void LogError(string format, params object[] args) => Log("ERROR", format, args);

    private void Log(string level, string format, params object[] args)
    {
        try
        {
            var message = args.Length == 0 ? format : string.Format(System.Globalization.CultureInfo.InvariantCulture, format, args);
            var line = $"[{DateTime.Now}] {level}: pid={Environment.ProcessId} {message}{Environment.NewLine}";
            lock (_logLock)
            {
                Directory.CreateDirectory(_paths.LogDirectory);
                File.AppendAllText(_paths.ReceiverLog, line);
                File.AppendAllText(Path.Combine(_paths.LogDirectory, "windows-native.log"), line);
            }
        }
        catch (Exception ex)
        {
            AppDiagnosticsLogger.Record(
                "WINDOWS_NATIVE_RECEIVER_LOG_WRITE_WARN",
                ("level", level),
                ("error", ex.Message));
        }
    }
}

internal sealed class WindowsNativeHidMapper
{
    private const uint UsagePageGenericDesktop = 0x0001;
    private const uint UsagePageSimulation = 0x0002;
    private const uint UsagePageButton = 0x0009;

    private readonly ReportDescriptor _descriptor;
    private readonly DeviceItemInputParser _parser;

    public WindowsNativeHidMapper(HidDevice device)
    {
        _descriptor = device.GetReportDescriptor();
        var item = _descriptor.DeviceItems.FirstOrDefault(deviceItem => deviceItem.InputReports.Any()) ??
                   throw new InvalidOperationException("HID descriptor does not expose input reports.");
        _parser = item.CreateDeviceItemInputParser();
    }

    public bool TryParse(ReadOnlySpan<byte> reportBytes, out ControllerState state)
    {
        state = default;
        if (TryParseKnownStadiaReport(reportBytes, out state))
        {
            return true;
        }

        var report = ResolveReport(reportBytes);
        if (report is null)
        {
            return false;
        }

        var data = reportBytes.ToArray();
        if (!_parser.TryParseReport(data, 0, report))
        {
            return false;
        }

        uint buttons = 0;
        byte leftTrigger = 0;
        byte rightTrigger = 0;
        short lx = 0;
        short ly = 0;
        short rx = 0;
        short ry = 0;

        for (var i = 0; i < _parser.ValueCount; i++)
        {
            var value = _parser.GetValue(i);
            if (!value.IsValid || value.IsNull)
            {
                continue;
            }

            var logical = value.GetLogicalValue();
            foreach (var usage in value.Usages)
            {
                var page = UsagePage(usage);
                var id = UsageId(usage);
                if (page == UsagePageButton)
                {
                    if (logical != 0)
                    {
                        buttons |= ButtonFromHidButton(id);
                    }
                    continue;
                }

                if (page == UsagePageGenericDesktop)
                {
                    switch (id)
                    {
                        case 0x30: lx = ScaleAxis(logical, value.DataItem); break;
                        case 0x31: ly = ScaleAxis(logical, value.DataItem); break;
                        case 0x32: rx = ScaleAxis(logical, value.DataItem); break;
                        case 0x35: ry = ScaleAxis(logical, value.DataItem); break;
                        case 0x36: leftTrigger = ScaleTrigger(logical, value.DataItem); break;
                        case 0x37: rightTrigger = ScaleTrigger(logical, value.DataItem); break;
                        case 0x39: buttons |= DpadFromHat(logical); break;
                    }
                    continue;
                }

                if (page == UsagePageSimulation)
                {
                    switch (id)
                    {
                        case 0xC4: rightTrigger = ScaleTrigger(logical, value.DataItem); break;
                        case 0xC5: leftTrigger = ScaleTrigger(logical, value.DataItem); break;
                    }
                }
            }
        }

        state = new ControllerState(buttons, leftTrigger, rightTrigger, lx, ly, rx, ry);
        return true;
    }

    private Report? ResolveReport(ReadOnlySpan<byte> reportBytes)
    {
        if (_descriptor.ReportsUseID)
        {
            return reportBytes.Length > 0 &&
                   _descriptor.TryGetReport(ReportType.Input, reportBytes[0], out var report)
                ? report
                : null;
        }

        return _descriptor.InputReports.FirstOrDefault();
    }

    internal static bool TryParseKnownStadiaReport(ReadOnlySpan<byte> data, out ControllerState state)
    {
        state = default;
        if (data.Length < 10 || data[0] != 0x03)
        {
            return false;
        }

        uint buttons = DpadFromHat(data[1]);
        if ((data[2] & 0x80) != 0) buttons |= ButtonBits.R3;
        if ((data[2] & 0x40) != 0) buttons |= ButtonBits.Select;
        if ((data[2] & 0x20) != 0) buttons |= ButtonBits.Start;
        if ((data[2] & 0x10) != 0) buttons |= ButtonBits.Stadia;
        if ((data[2] & 0x02) != 0) buttons |= ButtonBits.Assistant;
        if ((data[2] & 0x01) != 0) buttons |= ButtonBits.Capture;

        if ((data[3] & 0x40) != 0) buttons |= ButtonBits.A;
        if ((data[3] & 0x20) != 0) buttons |= ButtonBits.B;
        if ((data[3] & 0x10) != 0) buttons |= ButtonBits.X;
        if ((data[3] & 0x08) != 0) buttons |= ButtonBits.Y;
        if ((data[3] & 0x04) != 0) buttons |= ButtonBits.Lb;
        if ((data[3] & 0x02) != 0) buttons |= ButtonBits.Rb;
        if ((data[3] & 0x01) != 0) buttons |= ButtonBits.L3;

        state = new ControllerState(
            buttons,
            data[8],
            data[9],
            ScaleRawStick(data[4]),
            ScaleRawStick(data[5]),
            ScaleRawStick(data[6]),
            ScaleRawStick(data[7]));
        return true;
    }

    internal static void RunSelfTest()
    {
        byte[] report = [0x03, 0x01, 0xD3, 0x77, 0x80, 0x01, 0xFF, 0x80, 0x20, 0xE0];
        if (!TryParseKnownStadiaReport(report, out var state) ||
            !state.Has(ButtonBits.A) ||
            !state.Has(ButtonBits.B) ||
            !state.Has(ButtonBits.X) ||
            !state.Has(ButtonBits.Lb) ||
            !state.Has(ButtonBits.Rb) ||
            !state.Has(ButtonBits.L3) ||
            !state.Has(ButtonBits.R3) ||
            !state.Has(ButtonBits.Select) ||
            !state.Has(ButtonBits.Capture) ||
            !state.Has(ButtonBits.Assistant) ||
            !state.Has(ButtonBits.DpadUp) ||
            !state.Has(ButtonBits.DpadRight) ||
            state.TriggerLeft != 0x20 ||
            state.TriggerRight != 0xE0 ||
            state.StickLeftX != 0 ||
            state.StickLeftY >= 0 ||
            state.StickRightX <= 0)
        {
            throw new InvalidOperationException("Known Stadia HID report parser self-test failed.");
        }
    }

    private static uint ButtonFromHidButton(uint id)
    {
        return id switch
        {
            1 => ButtonBits.A,
            2 => ButtonBits.B,
            3 => ButtonBits.X,
            4 => ButtonBits.Y,
            5 => ButtonBits.Lb,
            6 => ButtonBits.Rb,
            7 => ButtonBits.Select,
            8 => ButtonBits.Start,
            9 => ButtonBits.L3,
            10 => ButtonBits.R3,
            11 => ButtonBits.Stadia,
            12 => ButtonBits.Assistant,
            _ => 0
        };
    }

    private static uint DpadFromHat(int value)
    {
        return value switch
        {
            0 => ButtonBits.DpadUp,
            1 => ButtonBits.DpadUp | ButtonBits.DpadRight,
            2 => ButtonBits.DpadRight,
            3 => ButtonBits.DpadRight | ButtonBits.DpadDown,
            4 => ButtonBits.DpadDown,
            5 => ButtonBits.DpadDown | ButtonBits.DpadLeft,
            6 => ButtonBits.DpadLeft,
            7 => ButtonBits.DpadLeft | ButtonBits.DpadUp,
            _ => 0
        };
    }

    private static short ScaleAxis(int value, DataItem item)
    {
        var min = item.LogicalMinimum;
        var max = item.LogicalMaximum;
        if (max <= min)
        {
            return 0;
        }

        var center = min + ((max - min) / 2.0);
        var normalized = (value - center) / Math.Max(center - min, max - center);
        return (short)Math.Clamp((int)Math.Round(normalized * 32767), -32767, 32767);
    }

    private static short ScaleRawStick(byte value)
    {
        if (value == 0x80)
        {
            return 0;
        }

        var normalized = (value - 128) / 127d;
        return (short)Math.Clamp((int)Math.Round(normalized * 32767), -32767, 32767);
    }

    private static byte ScaleTrigger(int value, DataItem item)
    {
        var min = item.LogicalMinimum;
        var max = item.LogicalMaximum;
        if (max <= min)
        {
            return 0;
        }

        var normalized = Math.Clamp((value - min) / (double)(max - min), 0, 1);
        return (byte)Math.Round(normalized * 255);
    }

    private static uint UsagePage(uint usage) => usage >> 16;

    private static uint UsageId(uint usage) => usage & 0xFFFF;
}
