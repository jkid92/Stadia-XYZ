using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using HidSharp;

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

        if (data.Length != PacketSize || data[0] != PacketMagic || data[1] != PacketVersion || data[2] >= 4)
        {
            return false;
        }

        controllerIndex = data[2];
        largeMotor = data[4];
        smallMotor = data[5];
        return true;
    }
}

internal sealed class WindowsNativeRumbleWriter
{
    private const byte StadiaRumbleReportId = 5;

    private readonly HidDevice _device;
    private readonly HidStream _stream;
    private readonly Action<string, object[]> _logInfo;
    private readonly Action<string, object[]> _logError;
    private readonly object _lock = new();

    private byte _lastLarge;
    private byte _lastSmall;
    private long _lastTick;
    private bool _hasLoggedFailure;

    public WindowsNativeRumbleWriter(
        HidDevice device,
        HidStream stream,
        Action<string, object[]> logInfo,
        Action<string, object[]> logError)
    {
        _device = device;
        _stream = stream;
        _logInfo = logInfo;
        _logError = logError;
    }

    public void Send(byte largeMotor, byte smallMotor)
    {
        lock (_lock)
        {
            var now = Environment.TickCount64;
            if (_lastTick != 0 &&
                now - _lastTick >= 0 &&
                now - _lastTick < 4 &&
                _lastLarge == largeMotor &&
                _lastSmall == smallMotor)
            {
                return;
            }

            _lastLarge = largeMotor;
            _lastSmall = smallMotor;
            _lastTick = now;

            if (!TrySendOutputReport(largeMotor, smallMotor, out var error))
            {
                if (!_hasLoggedFailure || largeMotor != 0 || smallMotor != 0)
                {
                    _logError(
                        "Windows Native rumble write failed: {0}",
                        new object[] { error });
                    _hasLoggedFailure = true;
                }
                return;
            }

            if (largeMotor != 0 || smallMotor != 0)
            {
                _logInfo(
                    "Windows Native rumble sent large={0} small={1}",
                    new object[] { largeMotor, smallMotor });
            }
        }
    }

    private bool TrySendOutputReport(byte largeMotor, byte smallMotor, out string error)
    {
        error = "";
        var maxOutputLength = _device.GetMaxOutputReportLength();
        if (maxOutputLength <= 0)
        {
            error = "HID device does not expose an output report.";
            return false;
        }

        if (maxOutputLength < 5)
        {
            error = $"HID output report is too short for Stadia rumble: {maxOutputLength} byte(s).";
            return false;
        }

        var buffer = new byte[maxOutputLength];
        buffer[0] = StadiaRumbleReportId;
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(1, 2), ScaleRumble(largeMotor));
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(3, 2), ScaleRumble(smallMotor));

        try
        {
            _stream.Write(buffer);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static ushort ScaleRumble(byte value)
    {
        return (ushort)(value * 257);
    }
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
