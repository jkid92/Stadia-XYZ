using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using HidSharp;

namespace StadiaX.ControlCenter;

internal static class WindowsNativeRuntime
{
    public const int RumblePort = 45504;

    public static string ReadyPath(AppPaths paths) => Path.Combine(paths.LogDirectory, "windows-native.ready");
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
