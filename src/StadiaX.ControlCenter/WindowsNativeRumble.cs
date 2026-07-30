using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using HidSharp;
using Microsoft.Win32.SafeHandles;

namespace StadiaX.ControlCenter;

internal static class WindowsNativeRuntime
{
    public const int RumblePort = 45504;
    public const int MaxControllers = 4;

    public static string ReadyPath(AppPaths paths) => Path.Combine(paths.LogDirectory, "windows-native.ready");

    public static string ReadyTempPath(AppPaths paths) => ReadyPath(paths) + ".tmp";

    public static void ClearReadyMarker(AppPaths paths)
    {
        TryDeleteFile(ReadyPath(paths));
        TryDeleteFile(ReadyTempPath(paths));
    }

    public static (IReadOnlyList<string> Removed, IReadOnlyList<string> Warnings) ClearControllerStateFiles(AppPaths paths)
    {
        var removed = new List<string>();
        var warnings = new List<string>();
        foreach (var path in new[] { paths.ControllerState, paths.ControllerState + ".tmp" })
        {
            try
            {
                if (!File.Exists(path))
                {
                    continue;
                }

                File.Delete(path);
                removed.Add(Path.GetFileName(path));
            }
            catch (Exception ex)
            {
                warnings.Add($"{Path.GetFileName(path)}: {ex.Message}");
            }
        }

        return (removed, warnings);
    }

    public static bool TryGetActiveReceiver(AppPaths paths, out int pid, out int controllers)
    {
        pid = 0;
        controllers = 0;
        var readyPath = ReadyPath(paths);
        if (!File.Exists(readyPath))
        {
            return false;
        }

        DateTimeOffset processStart;
        bool hasExactProcessStart;
        try
        {
            var marker = File.ReadAllText(readyPath).Trim();
            var timestamp = ReadMarkerTimestamp(marker, readyPath);
            processStart = ReadProcessStartTimestamp(marker, timestamp, out hasExactProcessStart);
            foreach (var part in marker.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var pair = part.Split('=', 2, StringSplitOptions.TrimEntries);
                if (pair.Length != 2)
                {
                    continue;
                }

                if (pair[0].Equals("pid", StringComparison.OrdinalIgnoreCase))
                {
                    _ = int.TryParse(pair[1], out pid);
                }
                else if (pair[0].Equals("controllers", StringComparison.OrdinalIgnoreCase))
                {
                    _ = int.TryParse(pair[1], out controllers);
                }
            }

        }
        catch (Exception ex)
        {
            controllers = 1;
            AppDiagnosticsLogger.Record("WINDOWS_NATIVE_READY_MARKER_READ_WARN", ("error", ex.Message));
            return true;
        }

        if (pid <= 0)
        {
            TryDeleteFile(readyPath);
            controllers = 0;
            return false;
        }

        try
        {
            using var process = Process.GetProcessById(pid);
            if (process.HasExited || !LooksLikeReceiverProcess(process) || !ProcessMatchesMarker(process, processStart, hasExactProcessStart))
            {
                TryDeleteFile(readyPath);
                pid = 0;
                controllers = 0;
                return false;
            }

            controllers = Math.Clamp(controllers, 1, MaxControllers);
            return true;
        }
        catch (ArgumentException)
        {
            TryDeleteFile(readyPath);
        }
        catch (InvalidOperationException)
        {
            TryDeleteFile(readyPath);
        }
        catch (Exception ex)
        {
            controllers = Math.Clamp(controllers, 1, MaxControllers);
            AppDiagnosticsLogger.Record(
                "WINDOWS_NATIVE_READY_PROCESS_PROBE_WARN",
                ("pid", pid.ToString()),
                ("error", ex.Message));
            return true;
        }

        pid = 0;
        controllers = 0;
        return false;
    }

    public static bool TryOpenActiveReceiverProcess(AppPaths paths, out Process? process, out int pid, out int controllers)
    {
        process = null;
        pid = 0;
        controllers = 0;

        if (!TryGetActiveReceiver(paths, out pid, out controllers))
        {
            return false;
        }

        if (pid <= 0)
        {
            AppDiagnosticsLogger.Record("WINDOWS_NATIVE_READY_PROCESS_OPEN_DEFERRED", ("reason", "marker temporarily unreadable"));
            return false;
        }

        var readyPath = ReadyPath(paths);
        Process? candidate = null;
        try
        {
            var marker = File.ReadAllText(readyPath).Trim();
            var timestamp = ReadMarkerTimestamp(marker, readyPath);
            var processStart = ReadProcessStartTimestamp(marker, timestamp, out var hasExactProcessStart);
            candidate = Process.GetProcessById(pid);
            if (!candidate.HasExited && LooksLikeReceiverProcess(candidate) && ProcessMatchesMarker(candidate, processStart, hasExactProcessStart))
            {
                process = candidate;
                candidate = null;
                return true;
            }

            TryDeleteFile(readyPath);
        }
        catch (ArgumentException)
        {
            TryDeleteFile(readyPath);
        }
        catch (InvalidOperationException)
        {
            TryDeleteFile(readyPath);
        }
        catch (Exception ex)
        {
            AppDiagnosticsLogger.Record(
                "WINDOWS_NATIVE_READY_PROCESS_OPEN_WARN",
                ("pid", pid.ToString()),
                ("error", ex.Message));
        }
        finally
        {
            candidate?.Dispose();
        }

        pid = 0;
        controllers = 0;
        return false;
    }

    private static DateTimeOffset ReadMarkerTimestamp(string marker, string readyPath)
    {
        var first = marker.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(first) && DateTimeOffset.TryParse(first, out var timestamp))
        {
            return timestamp;
        }

        return new DateTimeOffset(File.GetLastWriteTimeUtc(readyPath), TimeSpan.Zero);
    }

    private static DateTimeOffset ReadProcessStartTimestamp(string marker, DateTimeOffset fallback, out bool hasExactProcessStart)
    {
        foreach (var part in marker.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var pair = part.Split('=', 2, StringSplitOptions.TrimEntries);
            if (pair.Length == 2 &&
                pair[0].Equals("processStartUtc", StringComparison.OrdinalIgnoreCase) &&
                DateTimeOffset.TryParse(pair[1], out var processStart))
            {
                hasExactProcessStart = true;
                return processStart;
            }
        }

        hasExactProcessStart = false;
        return fallback;
    }

    private static bool ProcessMatchesMarker(Process process, DateTimeOffset expectedStart, bool hasExactProcessStart)
    {
        var startedAt = new DateTimeOffset(process.StartTime.ToUniversalTime(), TimeSpan.Zero);
        var expectedStartUtc = expectedStart.ToUniversalTime();
        return hasExactProcessStart
            ? (startedAt - expectedStartUtc).Duration() <= TimeSpan.FromSeconds(2)
            : startedAt <= expectedStartUtc.AddSeconds(15);
    }

    private static bool LooksLikeReceiverProcess(Process process)
    {
        return process.ProcessName.Equals("StadiaX", StringComparison.OrdinalIgnoreCase) &&
               process.MainWindowHandle == IntPtr.Zero;
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }
}

internal static class WindowsNativeRumbleProtocol
{
    private const byte PacketMagic = 0x53;
    private const byte PacketVersion = 1;
    private const int PacketSize = 6;

    public static byte[] BuildPacket(int controllerIndex, byte largeMotor, byte smallMotor)
    {
        if (controllerIndex < 0 || controllerIndex >= WindowsNativeRuntime.MaxControllers)
        {
            throw new ArgumentOutOfRangeException(nameof(controllerIndex));
        }

        return new[]
        {
            PacketMagic,
            PacketVersion,
            (byte)controllerIndex,
            (byte)0,
            largeMotor,
            smallMotor
        };
    }

    public static bool TryParse(byte[] data, out int controllerIndex, out byte largeMotor, out byte smallMotor)
    {
        controllerIndex = 0;
        largeMotor = 0;
        smallMotor = 0;

        if (data.Length != PacketSize ||
            data[0] != PacketMagic ||
            data[1] != PacketVersion ||
            data[2] >= WindowsNativeRuntime.MaxControllers ||
            data[3] != 0)
        {
            return false;
        }

        controllerIndex = data[2];
        largeMotor = data[4];
        smallMotor = data[5];
        return true;
    }

    internal static void RunSelfTest()
    {
        for (var expectedControllerIndex = 0; expectedControllerIndex < WindowsNativeRuntime.MaxControllers; expectedControllerIndex++)
        {
            var packet = BuildPacket(expectedControllerIndex, 220, 180);
            if (!TryParse(packet, out var controllerIndex, out var largeMotor, out var smallMotor) ||
                controllerIndex != expectedControllerIndex ||
                largeMotor != 220 ||
                smallMotor != 180)
            {
                throw new InvalidOperationException($"Windows Native P{expectedControllerIndex + 1} rumble packet self-test failed.");
            }
        }

        var invalidPacket = BuildPacket(0, 220, 180);
        invalidPacket[3] = 1;
        if (TryParse(invalidPacket, out _, out _, out _))
        {
            throw new InvalidOperationException("Windows Native rumble packet validation self-test failed.");
        }

        var disabled = ApplyEnabled(enabled: false, largeMotor: 220, smallMotor: 180);
        var enabled = ApplyEnabled(enabled: true, largeMotor: 220, smallMotor: 180);
        if (disabled != (0, 0) || enabled != (220, 180))
        {
            throw new InvalidOperationException("Windows Native rumble enable/disable self-test failed.");
        }

        var outputReport = WindowsNativeRumbleReport.Build(8, 220, 180);
        byte[] expected = [0x05, 0x00, 220, 0x00, 180, 0, 0, 0];
        if (!outputReport.SequenceEqual(expected))
        {
            throw new InvalidOperationException("Windows Native Stadia HID rumble report self-test failed.");
        }

        var fullReport = WindowsNativeRumbleReport.Build(5, byte.MaxValue, 0);
        byte[] expectedFullReport = [0x05, 0x00, 0xFF, 0x00, 0x00];
        if (!fullReport.SequenceEqual(expectedFullReport))
        {
            throw new InvalidOperationException("Windows Native Stadia HID motor range self-test failed.");
        }

        var gattPayload = WindowsNativeRumbleReport.BuildGattPayload(byte.MaxValue, 128);
        byte[] expectedGattPayload = [0x00, 0xFF, 0x00, 0x80];
        if (!gattPayload.SequenceEqual(expectedGattPayload))
        {
            throw new InvalidOperationException("Windows Native Stadia BLE rumble payload self-test failed.");
        }

        try
        {
            _ = WindowsNativeRumbleReport.Build(4, 1, 1);
            throw new InvalidOperationException("Windows Native rumble report length self-test failed.");
        }
        catch (ArgumentOutOfRangeException)
        {
        }
    }

    internal static (byte LargeMotor, byte SmallMotor) ApplyEnabled(
        bool enabled,
        byte largeMotor,
        byte smallMotor)
    {
        return enabled ? (largeMotor, smallMotor) : ((byte)0, (byte)0);
    }
}

internal static class WindowsNativeRumbleReport
{
    public const int MinimumLength = 5;
    public const byte ReportId = 0x05;

    public static byte[] Build(int reportLength, byte largeMotor, byte smallMotor)
    {
        if (reportLength < MinimumLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(reportLength),
                $"Stadia rumble requires an output report of at least {MinimumLength} bytes.");
        }

        var buffer = new byte[reportLength];
        buffer[0] = ReportId;
        BuildGattPayload(largeMotor, smallMotor).CopyTo(buffer, 1);
        return buffer;
    }

    public static byte[] BuildGattPayload(byte largeMotor, byte smallMotor)
    {
        return [0x00, largeMotor, 0x00, smallMotor];
    }
}

internal sealed class WindowsNativeHidOutputTransport : IDisposable
{
    private static readonly WindowsNativeHidOutputMode[] AutomaticModes =
    [
        WindowsNativeHidOutputMode.Win32WriteFile,
        WindowsNativeHidOutputMode.HidSharpStream,
        WindowsNativeHidOutputMode.HidDSetOutputReport
    ];

    private readonly HidStream _legacyStream;
    private readonly string _devicePath;
    private readonly int _outputReportLength;
    private readonly int _featureReportLength;

    private SafeFileHandle? _nativeHandle;
    private WindowsNativeHidOutputMode? _automaticRoute;
    private WindowsNativeHidOutputMode? _lastRequestedMode;
    private int _disposed;

    public WindowsNativeHidOutputTransport(HidDevice device, HidStream legacyStream)
    {
        _legacyStream = legacyStream;
        _devicePath = device.GetFileSystemName();
        _outputReportLength = ReadReportLength(device.GetMaxOutputReportLength);
        _featureReportLength = ReadReportLength(device.GetMaxFeatureReportLength);
    }

    public int OutputReportLength => _outputReportLength;

    public int FeatureReportLength => _featureReportLength;

    public bool IsSupported(WindowsNativeHidOutputMode mode)
    {
        return mode == WindowsNativeHidOutputMode.HidDSetFeature
            ? _featureReportLength >= WindowsNativeRumbleReport.MinimumLength
            : _outputReportLength >= WindowsNativeRumbleReport.MinimumLength;
    }

    public bool TrySend(
        WindowsNativeHidOutputMode requestedMode,
        byte largeMotor,
        byte smallMotor,
        out WindowsNativeHidOutputMode usedMode,
        out string error)
    {
        usedMode = requestedMode;
        error = "";
        if (Volatile.Read(ref _disposed) != 0)
        {
            error = "HID output transport is disposed.";
            return false;
        }

        if (_lastRequestedMode != requestedMode)
        {
            _lastRequestedMode = requestedMode;
            _automaticRoute = null;
            ResetNativeHandle();
        }

        if (requestedMode != WindowsNativeHidOutputMode.Auto)
        {
            return TrySendWithMode(requestedMode, largeMotor, smallMotor, out error);
        }

        var failures = new List<string>();
        WindowsNativeHidOutputMode? attemptedPreferred = null;
        if (_automaticRoute is { } preferred)
        {
            attemptedPreferred = preferred;
            if (TrySendWithMode(preferred, largeMotor, smallMotor, out error))
            {
                usedMode = preferred;
                return true;
            }

            failures.Add($"{WindowsNativeHidOutputModeStore.TechnicalName(preferred)}: {error}");
            _automaticRoute = null;
            ResetNativeHandle();
        }

        foreach (var mode in AutomaticModes)
        {
            if (mode == attemptedPreferred)
            {
                continue;
            }

            if (TrySendWithMode(mode, largeMotor, smallMotor, out error))
            {
                _automaticRoute = mode;
                usedMode = mode;
                return true;
            }

            failures.Add($"{WindowsNativeHidOutputModeStore.TechnicalName(mode)}: {error}");
            ResetNativeHandle();
        }

        usedMode = WindowsNativeHidOutputMode.Auto;
        error = string.Join(" | ", failures);
        return false;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        ResetNativeHandle();
    }

    private bool TrySendWithMode(
        WindowsNativeHidOutputMode mode,
        byte largeMotor,
        byte smallMotor,
        out string error)
    {
        error = "";
        var reportLength = mode == WindowsNativeHidOutputMode.HidDSetFeature
            ? _featureReportLength
            : _outputReportLength;
        if (reportLength < WindowsNativeRumbleReport.MinimumLength)
        {
            var reportType = mode == WindowsNativeHidOutputMode.HidDSetFeature ? "feature" : "output";
            error = $"HID device does not expose a usable {reportType} report (length={reportLength}).";
            return false;
        }

        var buffer = WindowsNativeRumbleReport.Build(reportLength, largeMotor, smallMotor);
        try
        {
            switch (mode)
            {
                case WindowsNativeHidOutputMode.HidSharpStream:
                    _legacyStream.Write(buffer);
                    return true;

                case WindowsNativeHidOutputMode.Win32WriteFile:
                    if (!TryGetNativeHandle(out var writeHandle, out error))
                    {
                        return false;
                    }
                    if (!WindowsNativeHidInterop.WriteFile(
                            writeHandle,
                            buffer,
                            (uint)buffer.Length,
                            out var bytesWritten,
                            IntPtr.Zero))
                    {
                        error = LastWin32Error("WriteFile");
                        return false;
                    }
                    if (bytesWritten != buffer.Length)
                    {
                        error = $"WriteFile completed a partial HID report ({bytesWritten}/{buffer.Length} bytes).";
                        return false;
                    }
                    return true;

                case WindowsNativeHidOutputMode.HidDSetOutputReport:
                    if (!TryGetNativeHandle(out var outputHandle, out error))
                    {
                        return false;
                    }
                    if (!WindowsNativeHidInterop.HidD_SetOutputReport(outputHandle, buffer, buffer.Length))
                    {
                        error = LastWin32Error("HidD_SetOutputReport");
                        return false;
                    }
                    return true;

                case WindowsNativeHidOutputMode.HidDSetFeature:
                    if (!TryGetNativeHandle(out var featureHandle, out error))
                    {
                        return false;
                    }
                    if (!WindowsNativeHidInterop.HidD_SetFeature(featureHandle, buffer, buffer.Length))
                    {
                        error = LastWin32Error("HidD_SetFeature");
                        return false;
                    }
                    return true;

                default:
                    error = $"Unsupported HID output mode: {mode}.";
                    return false;
            }
        }
        catch (Exception ex)
        {
            error = $"{WindowsNativeHidOutputModeStore.TechnicalName(mode)}: {ex.Message}";
            return false;
        }
    }

    private bool TryGetNativeHandle(out SafeFileHandle handle, out string error)
    {
        if (_nativeHandle is { IsInvalid: false, IsClosed: false })
        {
            handle = _nativeHandle;
            error = "";
            return true;
        }

        ResetNativeHandle();
        var errors = new List<string>();
        foreach (var access in new[]
                 {
                     WindowsNativeHidInterop.GenericRead | WindowsNativeHidInterop.GenericWrite,
                     WindowsNativeHidInterop.GenericWrite
                 })
        {
            var candidate = WindowsNativeHidInterop.CreateFile(
                _devicePath,
                access,
                WindowsNativeHidInterop.FileShareRead | WindowsNativeHidInterop.FileShareWrite,
                IntPtr.Zero,
                WindowsNativeHidInterop.OpenExisting,
                0,
                IntPtr.Zero);
            if (!candidate.IsInvalid)
            {
                _nativeHandle = candidate;
                handle = candidate;
                error = "";
                return true;
            }

            errors.Add($"access=0x{access:X8} {LastWin32Error("CreateFile")}");
            candidate.Dispose();
        }

        handle = new SafeFileHandle(IntPtr.Zero, ownsHandle: false);
        error = "Could not open a separate shared HID output handle: " + string.Join("; ", errors);
        return false;
    }

    private void ResetNativeHandle()
    {
        _nativeHandle?.Dispose();
        _nativeHandle = null;
    }

    private static int ReadReportLength(Func<int> read)
    {
        try
        {
            return read();
        }
        catch
        {
            return 0;
        }
    }

    private static string LastWin32Error(string operation)
    {
        var error = Marshal.GetLastWin32Error();
        return error == 0
            ? $"{operation} returned false without a Win32 error code."
            : $"{operation} failed ({error}: {new Win32Exception(error).Message}).";
    }
}

internal static class WindowsNativeHidInterop
{
    public const uint GenericRead = 0x80000000;
    public const uint GenericWrite = 0x40000000;
    public const uint FileShareRead = 0x00000001;
    public const uint FileShareWrite = 0x00000002;
    public const uint OpenExisting = 3;

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern SafeFileHandle CreateFile(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool WriteFile(
        SafeFileHandle file,
        byte[] buffer,
        uint bytesToWrite,
        out uint bytesWritten,
        IntPtr overlapped);

    [DllImport("hid.dll", EntryPoint = "HidD_SetOutputReport", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool HidD_SetOutputReport(
        SafeFileHandle hidDeviceObject,
        byte[] reportBuffer,
        int reportBufferLength);

    [DllImport("hid.dll", EntryPoint = "HidD_SetFeature", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool HidD_SetFeature(
        SafeFileHandle hidDeviceObject,
        byte[] reportBuffer,
        int reportBufferLength);
}

internal sealed class WindowsNativeRumbleWriter : IDisposable
{
    private const int DuplicateWindowMs = 4;
    private const int SuccessLogIntervalMs = 1000;
    private const int ErrorLogIntervalMs = 5000;
    private const int QueueCapacity = 16;
    private const int WorkerStopTimeoutMs = 250;

    private readonly int _controllerNumber;
    private readonly WindowsNativeHidOutputTransport _transport;
    private readonly StatusWriter _status;
    private readonly Func<bool> _isEnabled;
    private readonly Func<WindowsNativeHidOutputMode> _outputMode;
    private readonly Action<string, object[]> _logInfo;
    private readonly Action<string, object[]> _logError;
    private readonly Channel<RumbleCommand> _commands;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task _worker;
    private readonly object _queueLock = new();
    private readonly object _sendLock = new();

    private byte _lastLarge;
    private byte _lastSmall;
    private long _lastTick;
    private byte _lastQueuedLarge;
    private byte _lastQueuedSmall;
    private long _lastQueuedTick;
    private long _nextSuccessLogTick;
    private long _nextErrorLogTick;
    private WindowsNativeHidOutputMode? _lastRequestedMode;
    private int _disposed;

    public WindowsNativeRumbleWriter(
        int controllerNumber,
        HidDevice device,
        HidStream stream,
        StatusWriter status,
        Func<bool> isEnabled,
        Func<WindowsNativeHidOutputMode> outputMode,
        Action<string, object[]> logInfo,
        Action<string, object[]> logError)
    {
        _controllerNumber = controllerNumber;
        _transport = new WindowsNativeHidOutputTransport(device, stream);
        _status = status;
        _isEnabled = isEnabled;
        _outputMode = outputMode;
        _logInfo = logInfo;
        _logError = logError;
        _commands = Channel.CreateBounded<RumbleCommand>(new BoundedChannelOptions(QueueCapacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropOldest,
            AllowSynchronousContinuations = false
        });
        _worker = Task.Run(ProcessCommandsAsync);
    }

    public bool IsSupported => _transport.IsSupported(_outputMode());

    public int OutputReportLength => _transport.OutputReportLength;

    public int FeatureReportLength => _transport.FeatureReportLength;

    public void Send(byte largeMotor, byte smallMotor)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        (largeMotor, smallMotor) = WindowsNativeRumbleProtocol.ApplyEnabled(
            _isEnabled(),
            largeMotor,
            smallMotor);

        lock (_queueLock)
        {
            var now = Environment.TickCount64;
            if (_lastQueuedTick != 0 &&
                now - _lastQueuedTick >= 0 &&
                now - _lastQueuedTick < DuplicateWindowMs &&
                _lastQueuedLarge == largeMotor &&
                _lastQueuedSmall == smallMotor)
            {
                return;
            }

            if (_commands.Writer.TryWrite(new RumbleCommand(largeMotor, smallMotor, Stopwatch.GetTimestamp())))
            {
                _lastQueuedLarge = largeMotor;
                _lastQueuedSmall = smallMotor;
                _lastQueuedTick = now;
            }
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _commands.Writer.TryComplete();
        _shutdown.Cancel();
        try
        {
            _worker.Wait(WorkerStopTimeoutMs);
        }
        catch (AggregateException ex) when (ex.InnerExceptions.All(error => error is OperationCanceledException))
        {
        }
        catch (OperationCanceledException)
        {
        }

        lock (_sendLock)
        {
            _ = TrySendOutputReport(0, 0, out _, out _, out _);
            _lastLarge = 0;
            _lastSmall = 0;
            _lastTick = Environment.TickCount64;
        }

        _transport.Dispose();
        _shutdown.Dispose();
    }

    private async Task ProcessCommandsAsync()
    {
        try
        {
            await foreach (var command in _commands.Reader.ReadAllAsync(_shutdown.Token).ConfigureAwait(false))
            {
                SendNow(command.LargeMotor, command.SmallMotor, command.EnqueuedTimestamp);
            }
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logError(
                "P{0} Windows Native rumble worker failed: {1}",
                new object[] { _controllerNumber, ex.Message });
            _status.Write(
                "WINDOWS_NATIVE_RUMBLE_WORKER_FAILED",
                $"P{_controllerNumber} rumble=unavailable error={ex.Message}");
        }
    }

    private void SendNow(byte largeMotor, byte smallMotor, long enqueuedTimestamp)
    {
        string? errorToLog = null;
        var logSuccess = false;
        var requestedMode = WindowsNativeHidOutputMode.Auto;
        var usedMode = WindowsNativeHidOutputMode.Auto;
        var queueDelay = Stopwatch.GetElapsedTime(enqueuedTimestamp);
        var writeDuration = TimeSpan.Zero;
        lock (_sendLock)
        {
            var now = Environment.TickCount64;
            if (_lastTick != 0 &&
                now - _lastTick >= 0 &&
                now - _lastTick < DuplicateWindowMs &&
                _lastLarge == largeMotor &&
                _lastSmall == smallMotor)
            {
                return;
            }

            var writeStarted = Stopwatch.GetTimestamp();
            var sent = TrySendOutputReport(largeMotor, smallMotor, out requestedMode, out usedMode, out var error);
            writeDuration = Stopwatch.GetElapsedTime(writeStarted);
            if (!sent)
            {
                if (now >= _nextErrorLogTick)
                {
                    _nextErrorLogTick = now + ErrorLogIntervalMs;
                    errorToLog = error;
                }
            }
            else
            {
                _lastLarge = largeMotor;
                _lastSmall = smallMotor;
                _lastTick = now;
                _nextErrorLogTick = 0;
                if (largeMotor == 0 && smallMotor == 0)
                {
                    _nextSuccessLogTick = 0;
                }
                else if (now >= _nextSuccessLogTick)
                {
                    _nextSuccessLogTick = now + SuccessLogIntervalMs;
                    logSuccess = true;
                }
            }
        }

        if (errorToLog is not null)
        {
            _logError(
                "P{0} Windows Native rumble write failed requested={1} queueMs={2:0.00} writeMs={3:0.00}: {4}",
                new object[]
                {
                    _controllerNumber,
                    WindowsNativeHidOutputModeStore.TechnicalName(requestedMode),
                    queueDelay.TotalMilliseconds,
                    writeDuration.TotalMilliseconds,
                    errorToLog
                });
            _status.Write(
                "WINDOWS_NATIVE_RUMBLE_UNAVAILABLE",
                $"P{_controllerNumber} rumble=unavailable requested={WindowsNativeHidOutputModeStore.TechnicalName(requestedMode)} " +
                $"queueMs={queueDelay.TotalMilliseconds:0.00} writeMs={writeDuration.TotalMilliseconds:0.00} error={errorToLog}");
        }
        else if (logSuccess)
        {
            _logInfo(
                "P{0} Windows Native rumble sent large={1} small={2} requested={3} used={4} queueMs={5:0.00} writeMs={6:0.00}",
                new object[]
                {
                    _controllerNumber,
                    largeMotor,
                    smallMotor,
                    WindowsNativeHidOutputModeStore.TechnicalName(requestedMode),
                    WindowsNativeHidOutputModeStore.TechnicalName(usedMode),
                    queueDelay.TotalMilliseconds,
                    writeDuration.TotalMilliseconds
                });
            _status.Write(
                "WINDOWS_NATIVE_RUMBLE_OK",
                $"P{_controllerNumber} rumble=working large={largeMotor} small={smallMotor} " +
                $"requested={WindowsNativeHidOutputModeStore.TechnicalName(requestedMode)} " +
                $"used={WindowsNativeHidOutputModeStore.TechnicalName(usedMode)} " +
                $"queueMs={queueDelay.TotalMilliseconds:0.00} writeMs={writeDuration.TotalMilliseconds:0.00}");
        }
    }

    private bool TrySendOutputReport(
        byte largeMotor,
        byte smallMotor,
        out WindowsNativeHidOutputMode requestedMode,
        out WindowsNativeHidOutputMode usedMode,
        out string error)
    {
        requestedMode = _outputMode();
        if (_lastRequestedMode != requestedMode)
        {
            _lastRequestedMode = requestedMode;
            _nextSuccessLogTick = 0;
            _nextErrorLogTick = 0;
            _status.Write(
                "WINDOWS_NATIVE_HID_OUTPUT_MODE_ACTIVE",
                $"P{_controllerNumber} requested={WindowsNativeHidOutputModeStore.TechnicalName(requestedMode)} " +
                $"outputReportLength={OutputReportLength} featureReportLength={FeatureReportLength}");
            _logInfo(
                "P{0} HID output mode changed to {1}; outputReportLength={2} featureReportLength={3}",
                new object[]
                {
                    _controllerNumber,
                    WindowsNativeHidOutputModeStore.TechnicalName(requestedMode),
                    OutputReportLength,
                    FeatureReportLength
                });
        }

        var sent = _transport.TrySend(requestedMode, largeMotor, smallMotor, out usedMode, out error);
        return sent;
    }

    private readonly record struct RumbleCommand(byte LargeMotor, byte SmallMotor, long EnqueuedTimestamp);
}

internal sealed class WindowsNativeRumbleUdpServer
{
    private readonly Func<int, WindowsNativeRumbleWriter?> _writerForController;
    private readonly Action<string, object[]> _logInfo;
    private readonly Action<string, object[]> _logError;

    public WindowsNativeRumbleUdpServer(
        Func<int, WindowsNativeRumbleWriter?> writerForController,
        Action<string, object[]> logInfo,
        Action<string, object[]> logError)
    {
        _writerForController = writerForController;
        _logInfo = logInfo;
        _logError = logError;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var udp = new UdpClient(AddressFamily.InterNetwork);
            udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            udp.Client.Bind(new IPEndPoint(IPAddress.Loopback, WindowsNativeRuntime.RumblePort));
            _logInfo("Windows Native rumble test listener ready on 127.0.0.1:{0}", new object[] { WindowsNativeRuntime.RumblePort });

            while (!cancellationToken.IsCancellationRequested)
            {
                var result = await udp.ReceiveAsync(cancellationToken).ConfigureAwait(false);
                if (!IPAddress.IsLoopback(result.RemoteEndPoint.Address))
                {
                    continue;
                }

                if (!WindowsNativeRumbleProtocol.TryParse(result.Buffer, out var controllerIndex, out var largeMotor, out var smallMotor))
                {
                    continue;
                }

                var writer = _writerForController(controllerIndex);
                if (writer is null)
                {
                    _logError("Windows Native rumble requested for P{0}, but no HID writer is ready.", new object[] { controllerIndex + 1 });
                    continue;
                }

                writer.Send(largeMotor, smallMotor);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logError("Windows Native rumble listener failed: {0}", new object[] { ex.Message });
        }
    }
}
