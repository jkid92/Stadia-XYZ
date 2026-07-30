using System.Threading.Channels;
using Viiper.Client;
using Viiper.Client.Devices.Xbox360;
using Viiper.Client.Types;

namespace StadiaX.ControlCenter;

internal sealed class ViiperVirtualGamepadBusFactory : IVirtualGamepadBusFactory
{
    private readonly AppPaths _paths;

    public ViiperVirtualGamepadBusFactory(AppPaths paths)
    {
        _paths = paths;
    }

    public IVirtualGamepadBus Create(int controllerCount)
    {
        return new ViiperVirtualGamepadBus(_paths, controllerCount);
    }
}

internal sealed class ViiperVirtualGamepadBus : IVirtualGamepadBus
{
    private const int MaxControllers = 4;

    private readonly List<string> _initializationWarnings = new();
    private readonly ViiperPad[] _pads;

    private ViiperServerLease? _serverLease;
    private ViiperClient? _client;
    private uint _busId;
    private bool _busCreated;
    private bool _disposed;

    public ViiperVirtualGamepadBus(AppPaths paths, int controllerCount)
    {
        ControllerCount = Math.Clamp(controllerCount, 1, MaxControllers);
        _pads = new ViiperPad[ControllerCount];

        try
        {
            InitializeAsync(paths).GetAwaiter().GetResult();
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public int ControllerCount { get; }

    public IReadOnlyList<string> InitializationWarnings => _initializationWarnings;

    public event Action<int, byte, byte>? RumbleRequested;

    public bool TryUpdate(int controllerIndex, VirtualGamepadReport report, out string error)
    {
        if (!TryGetPad(controllerIndex, out var pad, out error))
        {
            return false;
        }

        return pad.TryQueue(report, out error);
    }

    public bool TryNeutralize(int controllerIndex, out string error)
    {
        return TryUpdate(controllerIndex, default, out error);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        RumbleRequested = null;
        foreach (var pad in _pads)
        {
            if (pad is null)
            {
                continue;
            }

            try
            {
                pad.Dispose();
            }
            catch (Exception ex)
            {
                AppDiagnosticsLogger.Record(
                    "VIIPER_PAD_DISPOSE_WARN",
                    ("error", ex.Message));
            }
        }

        if (_busCreated && _client is not null)
        {
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                _client.BusRemoveAsync(_busId, timeout.Token).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                AppDiagnosticsLogger.Record(
                    "VIIPER_BUS_REMOVE_WARN",
                    ("bus", _busId.ToString()),
                    ("error", ex.Message));
            }
        }

        _client?.Dispose();
        _client = null;
        _busCreated = false;
        _serverLease?.Dispose();
        _serverLease = null;
    }

    internal static void RunSelfTest()
    {
        var report = new VirtualGamepadReport
        {
            Buttons = XboxButtonBits.A | XboxButtonBits.DpadLeft,
            LeftTrigger = 0x12,
            RightTrigger = 0xE4,
            ThumbLX = -12345,
            ThumbLY = 23456,
            ThumbRX = short.MinValue,
            ThumbRY = short.MaxValue
        };
        var input = CreateInput(report);
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            input.Write(writer);
        }

        var bytes = stream.ToArray();
        if (bytes.Length != 20 ||
            BitConverter.ToUInt32(bytes, 0) != report.Buttons ||
            bytes[4] != report.LeftTrigger ||
            bytes[5] != report.RightTrigger ||
            BitConverter.ToInt16(bytes, 6) != report.ThumbLX ||
            BitConverter.ToInt16(bytes, 8) != report.ThumbLY ||
            BitConverter.ToInt16(bytes, 10) != report.ThumbRX ||
            BitConverter.ToInt16(bytes, 12) != report.ThumbRY ||
            bytes.AsSpan(14, 6).IndexOfAnyExcept((byte)0) >= 0)
        {
            throw new InvalidOperationException("VIIPER Xbox 360 packet self-test failed.");
        }
    }

    private async Task InitializeAsync(AppPaths paths)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        _serverLease = await ViiperRuntime.AcquireAsync(paths, timeout.Token).ConfigureAwait(false);
        _client = new ViiperClient(ViiperRuntime.Host, ViiperRuntime.ApiPort);
        var ping = await _client.PingAsync(timeout.Token).ConfigureAwait(false);
        if (!ping.Server.Equals("VIIPER", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The local virtual-controller API is not a VIIPER server.");
        }

        var bus = await _client.BusCreateAsync(null, timeout.Token).ConfigureAwait(false);
        _busId = bus.BusID;
        _busCreated = true;

        for (var index = 0; index < ControllerCount; index++)
        {
            var device = await _client.BusDeviceAddAsync(
                _busId,
                new DeviceCreateRequest
                {
                    Type = "xbox360",
                    DeviceSpecific = new Dictionary<string, object?>
                    {
                        ["subType"] = 1
                    }
                },
                timeout.Token).ConfigureAwait(false);
            var stream = await _client.ConnectDeviceAsync(
                device.BusID,
                device.DevID,
                timeout.Token).ConfigureAwait(false);
            _pads[index] = new ViiperPad(
                index,
                stream,
                (controllerIndex, largeMotor, smallMotor) =>
                    RumbleRequested?.Invoke(controllerIndex, largeMotor, smallMotor));
        }

        AppDiagnosticsLogger.Record(
            "VIIPER_VIRTUAL_BUS_READY",
            ("version", _serverLease.Version),
            ("bus", _busId.ToString()),
            ("controllers", ControllerCount.ToString()),
            ("api", $"{ViiperRuntime.Host}:{ViiperRuntime.ApiPort}"));
    }

    private bool TryGetPad(int controllerIndex, out ViiperPad pad, out string error)
    {
        pad = null!;
        if (_disposed)
        {
            error = "VIIPER virtual controller bus is disposed.";
            return false;
        }

        if (controllerIndex < 0 || controllerIndex >= _pads.Length)
        {
            error = $"Virtual controller index {controllerIndex} is outside the active range.";
            return false;
        }

        pad = _pads[controllerIndex];
        if (pad is null)
        {
            error = $"VIIPER virtual controller P{controllerIndex + 1} is not initialized.";
            return false;
        }

        error = "";
        return true;
    }

    private static Xbox360Input CreateInput(VirtualGamepadReport report)
    {
        return new Xbox360Input
        {
            Buttons = report.Buttons,
            Lt = report.LeftTrigger,
            Rt = report.RightTrigger,
            Lx = report.ThumbLX,
            Ly = report.ThumbLY,
            Rx = report.ThumbRX,
            Ry = report.ThumbRY
        };
    }

    private sealed class ViiperPad : IDisposable
    {
        private readonly int _controllerIndex;
        private readonly Action<int, byte, byte> _onRumble;
        private readonly Channel<VirtualGamepadReport> _reports;
        private readonly CancellationTokenSource _cancellation = new();
        private readonly Task _sender;

        private ViiperDevice? _device;
        private string? _lastError;
        private bool _disposed;

        public ViiperPad(
            int controllerIndex,
            ViiperDevice device,
            Action<int, byte, byte> onRumble)
        {
            _controllerIndex = controllerIndex;
            _device = device;
            _onRumble = onRumble;
            _reports = Channel.CreateBounded<VirtualGamepadReport>(
                new BoundedChannelOptions(1)
                {
                    SingleReader = true,
                    SingleWriter = false,
                    FullMode = BoundedChannelFullMode.DropOldest,
                    AllowSynchronousContinuations = false
                });
            device.OnOutput = ReadRumbleAsync;
            device.OnDisconnect = OnDisconnected;
            _sender = Task.Run(SendLoopAsync);
            _reports.Writer.TryWrite(default);
        }

        public bool TryQueue(VirtualGamepadReport report, out string error)
        {
            if (_disposed)
            {
                error = $"VIIPER P{_controllerIndex + 1} is disposed.";
                return false;
            }

            if (_sender.IsCompleted)
            {
                error = Volatile.Read(ref _lastError) ??
                        $"VIIPER P{_controllerIndex + 1} sender stopped.";
                return false;
            }

            if (!_reports.Writer.TryWrite(report))
            {
                error = $"VIIPER P{_controllerIndex + 1} input queue is closed.";
                return false;
            }

            error = Volatile.Read(ref _lastError) ?? "";
            return string.IsNullOrEmpty(error);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _reports.Writer.TryWrite(default);
            _reports.Writer.TryComplete();
            try
            {
                _sender.Wait(TimeSpan.FromMilliseconds(250));
            }
            catch
            {
            }

            _cancellation.Cancel();
            var device = Interlocked.Exchange(ref _device, null);
            if (device is not null)
            {
                device.OnDisconnect = null;
                device.Dispose();
            }
            _cancellation.Dispose();
        }

        private async Task SendLoopAsync()
        {
            try
            {
                await foreach (var report in _reports.Reader.ReadAllAsync(_cancellation.Token).ConfigureAwait(false))
                {
                    var device = Volatile.Read(ref _device);
                    if (device is null)
                    {
                        Volatile.Write(
                            ref _lastError,
                            $"VIIPER P{_controllerIndex + 1} device stream is unavailable.");
                        return;
                    }

                    try
                    {
                        await device.SendAsync(
                            CreateInput(report),
                            _cancellation.Token).ConfigureAwait(false);
                        Volatile.Write(ref _lastError, null);
                    }
                    catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
                    {
                        return;
                    }
                    catch (Exception ex)
                    {
                        Volatile.Write(ref _lastError, ex.Message);
                        AppDiagnosticsLogger.Record(
                            "VIIPER_INPUT_STREAM_FAILED",
                            ("controller", $"P{_controllerIndex + 1}"),
                            ("error", ex.Message));
                        return;
                    }
                }
            }
            catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                Volatile.Write(ref _lastError, ex.Message);
            }
        }

        private async Task ReadRumbleAsync(Stream stream)
        {
            var feedback = new byte[2];
            await stream.ReadExactlyAsync(feedback, _cancellation.Token).ConfigureAwait(false);
            _onRumble(_controllerIndex, feedback[0], feedback[1]);
        }

        private void OnDisconnected()
        {
            if (!_disposed)
            {
                Volatile.Write(
                    ref _lastError,
                    $"VIIPER P{_controllerIndex + 1} feedback stream disconnected.");
            }
        }
    }
}
