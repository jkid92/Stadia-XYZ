using HidSharp;
using HidSharp.Reports;
using HidSharp.Reports.Input;

namespace StadiaX.ControlCenter;

internal sealed class WindowsNativeReceiver
{
    private const int MaxControllers = 4;

    private readonly AppPaths _paths;
    private readonly StatusWriter _status;
    private readonly WindowsNativeHidScanner _scanner;
    private readonly IReadOnlyList<WindowsNativeHidDevice>? _initialDevices;
    private readonly ControllerTelemetryWriter _telemetryWriter;
    private readonly object _logLock = new();
    private readonly WindowsNativeRumbleWriter?[] _rumbleWriters = new WindowsNativeRumbleWriter?[MaxControllers];
    private readonly VigemNative.X360Notification _rumbleCallback;
    private readonly IntPtr[] _targets = new IntPtr[MaxControllers];

    private IntPtr _client;

    public WindowsNativeReceiver(
        AppPaths paths,
        StatusWriter status,
        WindowsNativeHidScanner scanner,
        IReadOnlyList<WindowsNativeHidDevice>? initialDevices = null)
    {
        _paths = paths;
        _status = status;
        _scanner = scanner;
        _initialDevices = initialDevices;
        _telemetryWriter = new ControllerTelemetryWriter(paths);
        _rumbleCallback = OnRumble;
    }

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_paths.LogDirectory);
        LogInfo("Windows Native receiver starting");

        try
        {
            var devices = ((_initialDevices is { Count: > 0 }
                    ? _initialDevices
                    : await _scanner.FindStadiaControllersAsync().ConfigureAwait(false)))
                .Take(MaxControllers)
                .ToArray();
            if (devices.Length == 0)
            {
                _status.Write("WINDOWS_NATIVE_NOT_READY", "No Stadia HID controller is visible to Windows");
                LogInfo("No Stadia HID controller is visible to Windows");
                return 2;
            }

            InitializeVigem(devices.Length);
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
                var controllerGroup = Task.WhenAll(controllerTasks);
                var shutdownTask = Task.Delay(Timeout.InfiniteTimeSpan, linked.Token);
                var completed = await Task.WhenAny(controllerGroup, shutdownTask).ConfigureAwait(false);
                if (completed == shutdownTask)
                {
                    return 0;
                }

                var controllerLoopsStopped = completed == controllerGroup;
                linked.Cancel();
                try
                {
                    await Task.WhenAll(tasks).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!controllerLoopsStopped)
                {
                    return 0;
                }
                catch (OperationCanceledException)
                {
                }

                if (controllerGroup.IsFaulted)
                {
                    throw controllerGroup.Exception?.GetBaseException() ??
                          new InvalidOperationException("Windows Native controller loop failed.");
                }

                throw new InvalidOperationException("Windows Native controller loop stopped unexpectedly.");
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
            CleanupVigem();
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
                var rumbleWriter = new WindowsNativeRumbleWriter(hidDevice, stream, LogInfo, LogError);
                var buffer = new byte[Math.Max(1, hidDevice.GetMaxInputReportLength())];
                _rumbleWriters[controllerIndex] = rumbleWriter;
                reconnectAttempt = 0;
                neutralized = false;
                _status.Write(
                    connectedOnce ? "WINDOWS_NATIVE_CONTROLLER_RECONNECTED" : "WINDOWS_NATIVE_CONTROLLER_OPEN",
                    $"P{controllerIndex + 1}: {currentDevice.FriendlyName}");
                LogInfo(
                    connectedOnce ? "P{0} Windows Native HID reconnected: {1}" : "P{0} Windows Native HID open: {1}",
                    controllerIndex + 1,
                    currentDevice.FriendlyName);
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

                        var update = VigemNative.vigem_target_x360_update(_client, _targets[controllerIndex], ControllerStateMapper.ToXusb(state));
                        if (!VigemNative.Success(update))
                        {
                            LogError("P{0} ViGEm update failed: 0x{1:X8}", controllerIndex + 1, update);
                        }

                        _telemetryWriter.Write(controllerIndex, state);
                    }
                }
                finally
                {
                    try { rumbleWriter.Send(0, 0); } catch { }
                    _rumbleWriters[controllerIndex] = null;
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
        var target = _targets[controllerIndex];
        if (_client == IntPtr.Zero || target == IntPtr.Zero)
        {
            return;
        }

        var update = VigemNative.vigem_target_x360_update(_client, target, default);
        if (!VigemNative.Success(update))
        {
            LogError("P{0} ViGEm neutral reset failed: 0x{1:X8}", controllerIndex + 1, update);
        }

        _telemetryWriter.Write(controllerIndex, default);
    }

    private void InitializeVigem(int targetCount)
    {
        _client = VigemNative.vigem_alloc();
        if (_client == IntPtr.Zero)
        {
            throw new InvalidOperationException("ViGEm client allocation failed.");
        }

        var connect = VigemNative.vigem_connect(_client);
        if (!VigemNative.Success(connect))
        {
            throw new InvalidOperationException($"ViGEmBus init failed: 0x{connect:X8}. Is the driver installed?");
        }

        for (var i = 0; i < Math.Clamp(targetCount, 1, MaxControllers); i++)
        {
            var target = VigemNative.vigem_target_x360_alloc();
            if (target == IntPtr.Zero)
            {
                throw new InvalidOperationException($"ViGEm virtual pad {i + 1} allocation failed.");
            }

            _targets[i] = target;
            var add = VigemNative.vigem_target_add(_client, target);
            if (!VigemNative.Success(add))
            {
                throw new InvalidOperationException($"ViGEm virtual pad {i + 1} init failed: 0x{add:X8}.");
            }

            var notify = VigemNative.vigem_target_x360_register_notification(_client, target, _rumbleCallback, new IntPtr(i));
            if (!VigemNative.Success(notify))
            {
                LogError("P{0} ViGEm rumble notification registration failed: 0x{1:X8}", i + 1, notify);
            }

            LogInfo("P{0} Windows Native virtual Xbox 360 pad ready", i + 1);
        }
    }

    private void OnRumble(IntPtr client, IntPtr target, byte largeMotor, byte smallMotor, byte ledNumber, IntPtr userData)
    {
        var controllerIndex = userData.ToInt32();
        if (controllerIndex < 0 || controllerIndex >= _rumbleWriters.Length)
        {
            return;
        }

        var writer = _rumbleWriters[controllerIndex];
        if (writer is null)
        {
            return;
        }

        writer.Send(largeMotor, smallMotor);
    }

    private void CleanupVigem()
    {
        for (var i = 0; i < _targets.Length; i++)
        {
            var target = _targets[i];
            if (target == IntPtr.Zero)
            {
                continue;
            }

            try { VigemNative.vigem_target_x360_unregister_notification(target); } catch { }
            try { VigemNative.vigem_target_remove(_client, target); } catch { }
            try { VigemNative.vigem_target_free(target); } catch { }
            _targets[i] = IntPtr.Zero;
        }

        if (_client != IntPtr.Zero)
        {
            try { VigemNative.vigem_disconnect(_client); } catch { }
            try { VigemNative.vigem_free(_client); } catch { }
            _client = IntPtr.Zero;
        }
    }

    private void WriteReadyMarker(int controllerCount)
    {
        Directory.CreateDirectory(_paths.LogDirectory);
        File.WriteAllText(
            WindowsNativeRuntime.ReadyPath(_paths),
            $"{DateTimeOffset.Now:O}|pid={Environment.ProcessId}|controllers={controllerCount}{Environment.NewLine}");
    }

    private void DeleteReadyMarker()
    {
        var path = WindowsNativeRuntime.ReadyPath(_paths);
        if (File.Exists(path))
        {
            try { File.Delete(path); } catch { }
        }
    }

    private void LogInfo(string format, params object[] args) => Log("INFO", format, args);

    private void LogError(string format, params object[] args) => Log("ERROR", format, args);

    private void Log(string level, string format, params object[] args)
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

        ushort buttons = 0;
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

    private static ushort ButtonFromHidButton(uint id)
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

    private static ushort DpadFromHat(int value)
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
