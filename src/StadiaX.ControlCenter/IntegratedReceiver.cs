using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace StadiaX.ControlCenter;

internal sealed class IntegratedReceiver
{
    private const int MaxControllers = 4;
    private const byte PacketMagic = 0x53;
    private const byte PacketVersion = 1;
    private const int PortInput = 45493;
    private const int PortRumble = 45494;
    private const int PortMacro = 45499;
    private const int ControllerStateSize = 12;
    private const int InputPacketSize = 16;
    private const int RumblePacketSize = 6;
    private const int OneShotDebounceMs = 200;
    private const int RumbleDuplicateWindowMs = 4;

    private readonly AppPaths _paths;
    private readonly string _bridgeIp;
    private readonly StatusWriter _status;
    private readonly object _logLock = new();
    private readonly object _telemetryLock = new();
    private readonly object _rumbleLock = new();
    private readonly ControllerTelemetryState[] _telemetry = Enumerable.Range(0, MaxControllers).Select(_ => new ControllerTelemetryState()).ToArray();
    private readonly byte[] _lastRumbleLarge = new byte[MaxControllers];
    private readonly byte[] _lastRumbleSmall = new byte[MaxControllers];
    private readonly long[] _lastRumbleTick = new long[MaxControllers];
    private readonly VigemNative.X360Notification _rumbleCallback;

    private IntPtr _client;
    private readonly IntPtr[] _targets = new IntPtr[MaxControllers];
    private UdpClient? _rumbleClient;
    private IPEndPoint? _rumbleEndpoint;
    private DateTimeOffset _lastTelemetryWrite = DateTimeOffset.MinValue;

    public IntegratedReceiver(AppPaths paths, string bridgeIp, StatusWriter status)
    {
        _paths = paths;
        _bridgeIp = bridgeIp;
        _status = status;
        _rumbleCallback = OnRumble;
    }

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_paths.LogDirectory);
        LogInfo("Integrated receiver starting - bridge at {0}", _bridgeIp);

        if (!IPAddress.TryParse(_bridgeIp, out var bridgeAddress))
        {
            LogError("Invalid Linux bridge IP address: {0}", _bridgeIp);
            return 1;
        }

        try
        {
            InitializeVigem();
            InitializeRumble(bridgeAddress);

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var inputTask = Task.Run(() => RunInputLoopAsync(linked.Token), linked.Token);
            var macroTask = Task.Run(() => RunMacroLoopAsync(linked.Token), linked.Token);

            LogInfo("Integrated receiver running.");
            _status.Write("RECEIVER_READY", "Integrated Windows receiver is running inside StadiaX.exe");

            try
            {
                var shutdownTask = Task.Delay(Timeout.InfiniteTimeSpan, linked.Token);
                var completed = await Task.WhenAny(inputTask, macroTask, shutdownTask).ConfigureAwait(false);
                if (completed == shutdownTask)
                {
                    return 0;
                }

                linked.Cancel();
                try
                {
                    await Task.WhenAll(inputTask, macroTask).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return 0;
                }

                if (completed.IsFaulted)
                {
                    throw completed.Exception?.GetBaseException() ?? new InvalidOperationException("Integrated receiver loop failed.");
                }

                throw new InvalidOperationException("Integrated receiver loop stopped unexpectedly.");
            }
            catch (OperationCanceledException) when (linked.IsCancellationRequested)
            {
                return 0;
            }
            catch (Exception ex)
            {
                linked.Cancel();
                LogError("Integrated receiver failed: {0}", ex.Message);
                _status.Write("RECEIVER_FAILED", ex.Message);
                return 1;
            }
        }
        catch (Exception ex)
        {
            LogError("Integrated receiver startup failed: {0}", ex.Message);
            _status.Write("RECEIVER_START_FAILED", ex.Message);
            return 1;
        }
        finally
        {
            CleanupVigem();
            lock (_rumbleLock)
            {
                _rumbleClient?.Dispose();
                _rumbleClient = null;
                _rumbleEndpoint = null;
                Array.Clear(_lastRumbleLarge);
                Array.Clear(_lastRumbleSmall);
                Array.Clear(_lastRumbleTick);
            }

            LogInfo("Integrated receiver stopped.");
        }
    }

    private void InitializeVigem()
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

        for (var i = 0; i < MaxControllers; i++)
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
                LogError("ViGEm rumble notification registration failed for pad {0}: 0x{1:X8}", i + 1, notify);
            }

            LogInfo("Virtual Xbox 360 pad {0} ready", i + 1);
        }
    }

    private void InitializeRumble(IPAddress bridgeAddress)
    {
        _rumbleClient = new UdpClient(AddressFamily.InterNetwork);
        _rumbleEndpoint = new IPEndPoint(bridgeAddress, PortRumble);
        _rumbleClient.Connect(_rumbleEndpoint);
    }

    private async Task RunInputLoopAsync(CancellationToken cancellationToken)
    {
        using var udp = CreateBoundUdpClient(PortInput);
        LogInfo("Listening for controller packets on UDP {0}", PortInput);

        while (!cancellationToken.IsCancellationRequested)
        {
            var result = await udp.ReceiveAsync(cancellationToken).ConfigureAwait(false);
            if (!TryParseInputPacket(result.Buffer, out var controllerIndex, out var state))
            {
                continue;
            }

            var target = _targets[controllerIndex];
            if (target == IntPtr.Zero)
            {
                continue;
            }

            var update = VigemNative.vigem_target_x360_update(_client, target, ControllerStateMapper.ToXusb(state));
            if (!VigemNative.Success(update))
            {
                LogError("ViGEm update failed for pad {0}: 0x{1:X8}", controllerIndex + 1, update);
            }

            WriteControllerTelemetry(controllerIndex, state);
        }
    }

    private async Task RunMacroLoopAsync(CancellationToken cancellationToken)
    {
        var mappings = MacroMappingLoader.Load(_paths.MacroConfig, LogInfo, LogError);
        if (mappings.Count == 0)
        {
            LogInfo("No macro shortcuts loaded.");
        }

        using var udp = CreateBoundUdpClient(PortMacro);
        var slotState = mappings.ToDictionary(mapping => mapping.Code, _ => DateTimeOffset.MinValue, StringComparer.OrdinalIgnoreCase);
        LogInfo("Listening for macro packets on UDP {0}", PortMacro);

        while (!cancellationToken.IsCancellationRequested)
        {
            var result = await udp.ReceiveAsync(cancellationToken).ConfigureAwait(false);
            var code = Encoding.ASCII.GetString(result.Buffer).TrimEnd('\0', '\r', '\n', ' ');
            var mapping = mappings.FirstOrDefault(item => item.Code.Equals(code, StringComparison.OrdinalIgnoreCase));
            if (mapping is null)
            {
                continue;
            }

            var now = DateTimeOffset.UtcNow;
            if (mapping.Repeat || !slotState.TryGetValue(mapping.Code, out var last) || (now - last).TotalMilliseconds > OneShotDebounceMs)
            {
                KeyboardSender.PressCombo(mapping);
            }

            slotState[mapping.Code] = now;
        }
    }

    private static UdpClient CreateBoundUdpClient(int port)
    {
        var udp = new UdpClient(AddressFamily.InterNetwork);
        try
        {
            udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            udp.Client.Bind(new IPEndPoint(IPAddress.Any, port));
            return udp;
        }
        catch
        {
            udp.Dispose();
            throw;
        }
    }

    private bool TryParseInputPacket(byte[] data, out int controllerIndex, out ControllerState state)
    {
        controllerIndex = 0;
        state = default;

        if (data.Length == ControllerStateSize)
        {
            state = ParseControllerState(data.AsSpan());
            return true;
        }

        if (data.Length != InputPacketSize || data[0] != PacketMagic || data[1] != PacketVersion || data[2] >= MaxControllers)
        {
            return false;
        }

        controllerIndex = data[2];
        state = ParseControllerState(data.AsSpan(4));
        return true;
    }

    private static ControllerState ParseControllerState(ReadOnlySpan<byte> data)
    {
        return new ControllerState(
            BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(0, 2)),
            data[2],
            data[3],
            BinaryPrimitives.ReadInt16LittleEndian(data.Slice(4, 2)),
            BinaryPrimitives.ReadInt16LittleEndian(data.Slice(6, 2)),
            BinaryPrimitives.ReadInt16LittleEndian(data.Slice(8, 2)),
            BinaryPrimitives.ReadInt16LittleEndian(data.Slice(10, 2)));
    }

    private void OnRumble(IntPtr client, IntPtr target, byte largeMotor, byte smallMotor, byte ledNumber, IntPtr userData)
    {
        var controllerIndex = userData.ToInt32();
        if (controllerIndex < 0 || controllerIndex >= MaxControllers)
        {
            return;
        }

        var packet = new byte[RumblePacketSize];
        packet[0] = PacketMagic;
        packet[1] = PacketVersion;
        packet[2] = (byte)controllerIndex;
        packet[4] = largeMotor;
        packet[5] = smallMotor;

        lock (_rumbleLock)
        {
            if (_rumbleClient is null || _rumbleEndpoint is null)
            {
                return;
            }

            var now = Environment.TickCount64;
            var elapsed = now - _lastRumbleTick[controllerIndex];
            if (_lastRumbleTick[controllerIndex] != 0 &&
                elapsed >= 0 &&
                elapsed < RumbleDuplicateWindowMs &&
                _lastRumbleLarge[controllerIndex] == largeMotor &&
                _lastRumbleSmall[controllerIndex] == smallMotor)
            {
                return;
            }

            try
            {
                _rumbleClient.Send(packet, packet.Length);
                _lastRumbleLarge[controllerIndex] = largeMotor;
                _lastRumbleSmall[controllerIndex] = smallMotor;
                _lastRumbleTick[controllerIndex] = now;
            }
            catch (Exception ex)
            {
                LogError("Rumble send failed: {0}", ex.Message);
            }
        }
    }

    private void WriteControllerTelemetry(int controllerIndex, ControllerState state)
    {
        var now = DateTimeOffset.UtcNow;
        var tickMs = Environment.TickCount64;
        ControllerTelemetryState[] snapshot;

        lock (_telemetryLock)
        {
            var telemetry = _telemetry[controllerIndex];
            telemetry.State = state;
            telemetry.Packets++;
            if (telemetry.FirstSeenMs == 0)
            {
                telemetry.FirstSeenMs = tickMs;
            }

            telemetry.LastSeenMs = tickMs;

            if (now - _lastTelemetryWrite < TimeSpan.FromMilliseconds(33))
            {
                return;
            }

            _lastTelemetryWrite = now;
            snapshot = _telemetry.Select(item => item.Clone()).ToArray();
        }

        Directory.CreateDirectory(_paths.LogDirectory);
        var tempPath = _paths.ControllerState + ".tmp";
        using (var stream = File.Create(tempPath))
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("timestamp", tickMs);
            writer.WriteNumber("active_controller", controllerIndex);
            WriteButtonsJson(writer, state);
            WriteAxesJson(writer, state);
            writer.WriteStartArray("controllers");
            for (var i = 0; i < snapshot.Length; i++)
            {
                var telemetry = snapshot[i];
                var active = telemetry.LastSeenMs > 0 && tickMs - telemetry.LastSeenMs < 5000;
                var elapsed = telemetry.FirstSeenMs > 0 && telemetry.LastSeenMs >= telemetry.FirstSeenMs
                    ? (telemetry.LastSeenMs - telemetry.FirstSeenMs) / 1000d
                    : 0d;
                var pps = elapsed > 0 ? telemetry.Packets / elapsed : 0d;

                writer.WriteStartObject();
                writer.WriteNumber("index", i);
                writer.WriteBoolean("active", active);
                writer.WriteNumber("last_seen_ms", telemetry.LastSeenMs);
                writer.WriteNumber("last_seen_age_ms", telemetry.LastSeenMs > 0 ? tickMs - telemetry.LastSeenMs : 0);
                writer.WriteNumber("packets", telemetry.Packets);
                writer.WriteNumber("pps", Math.Round(pps, 2));
                WriteButtonsJson(writer, telemetry.State);
                WriteAxesJson(writer, telemetry.State);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        File.Move(tempPath, _paths.ControllerState, overwrite: true);
    }

    private static void WriteButtonsJson(Utf8JsonWriter writer, ControllerState state)
    {
        writer.WriteStartObject("buttons");
        writer.WriteBoolean("a", state.Has(ButtonBits.A));
        writer.WriteBoolean("b", state.Has(ButtonBits.B));
        writer.WriteBoolean("x", state.Has(ButtonBits.X));
        writer.WriteBoolean("y", state.Has(ButtonBits.Y));
        writer.WriteBoolean("lb", state.Has(ButtonBits.Lb));
        writer.WriteBoolean("rb", state.Has(ButtonBits.Rb));
        writer.WriteBoolean("select", state.Has(ButtonBits.Select));
        writer.WriteBoolean("start", state.Has(ButtonBits.Start));
        writer.WriteBoolean("stadia", state.Has(ButtonBits.Stadia));
        writer.WriteBoolean("l3", state.Has(ButtonBits.L3));
        writer.WriteBoolean("r3", state.Has(ButtonBits.R3));
        writer.WriteBoolean("assistant", state.Has(ButtonBits.Assistant));
        writer.WriteBoolean("dpad_up", state.Has(ButtonBits.DpadUp));
        writer.WriteBoolean("dpad_down", state.Has(ButtonBits.DpadDown));
        writer.WriteBoolean("dpad_left", state.Has(ButtonBits.DpadLeft));
        writer.WriteBoolean("dpad_right", state.Has(ButtonBits.DpadRight));
        writer.WriteEndObject();
    }

    private static void WriteAxesJson(Utf8JsonWriter writer, ControllerState state)
    {
        writer.WriteStartObject("axes");
        writer.WriteNumber("trigger_left", state.TriggerLeft);
        writer.WriteNumber("trigger_right", state.TriggerRight);
        writer.WriteNumber("stick_lx", state.StickLeftX);
        writer.WriteNumber("stick_ly", state.StickLeftY);
        writer.WriteNumber("stick_rx", state.StickRightX);
        writer.WriteNumber("stick_ry", state.StickRightY);
        writer.WriteEndObject();
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
            try { if (_client != IntPtr.Zero) VigemNative.vigem_target_remove(_client, target); } catch { }
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

    private void LogInfo(string format, params object[] args)
    {
        Log("INFO", format, args);
    }

    private void LogError(string format, params object[] args)
    {
        Log("ERROR", format, args);
    }

    private void Log(string level, string format, params object[] args)
    {
        var message = args.Length == 0 ? format : string.Format(System.Globalization.CultureInfo.InvariantCulture, format, args);
        var line = $"[{DateTime.Now}] {level}: {message}{Environment.NewLine}";
        lock (_logLock)
        {
            File.AppendAllText(_paths.ReceiverLog, line);
        }
    }

    private sealed class ControllerTelemetryState
    {
        public ControllerState State { get; set; }
        public ulong Packets { get; set; }
        public long FirstSeenMs { get; set; }
        public long LastSeenMs { get; set; }

        public ControllerTelemetryState Clone()
        {
            return new ControllerTelemetryState
            {
                State = State,
                Packets = Packets,
                FirstSeenMs = FirstSeenMs,
                LastSeenMs = LastSeenMs
            };
        }
    }
}

internal readonly record struct ControllerState(
    ushort Buttons,
    byte TriggerLeft,
    byte TriggerRight,
    short StickLeftX,
    short StickLeftY,
    short StickRightX,
    short StickRightY)
{
    public bool Has(ushort bit) => (Buttons & bit) != 0;
}

internal static class ButtonBits
{
    public const ushort A = 1 << 0;
    public const ushort B = 1 << 1;
    public const ushort X = 1 << 2;
    public const ushort Y = 1 << 3;
    public const ushort Lb = 1 << 4;
    public const ushort Rb = 1 << 5;
    public const ushort Select = 1 << 6;
    public const ushort Start = 1 << 7;
    public const ushort Stadia = 1 << 8;
    public const ushort L3 = 1 << 9;
    public const ushort R3 = 1 << 10;
    public const ushort Assistant = 1 << 11;
    public const ushort DpadUp = 1 << 12;
    public const ushort DpadDown = 1 << 13;
    public const ushort DpadLeft = 1 << 14;
    public const ushort DpadRight = 1 << 15;
}

internal sealed record MacroHotkey(string Code, ushort Modifiers, ushort MainKey, bool Repeat);

internal static class MacroMappingLoader
{
    private const ushort ModAlt = 1;
    private const ushort ModControl = 2;
    private const ushort ModShift = 4;
    private const ushort ModWin = 8;

    private static readonly Dictionary<string, ushort> KeyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["F1"] = 0x70, ["F2"] = 0x71, ["F3"] = 0x72, ["F4"] = 0x73,
        ["F5"] = 0x74, ["F6"] = 0x75, ["F7"] = 0x76, ["F8"] = 0x77,
        ["F9"] = 0x78, ["F10"] = 0x79, ["F11"] = 0x7A, ["F12"] = 0x7B,
        ["0"] = 0x30, ["1"] = 0x31, ["2"] = 0x32, ["3"] = 0x33, ["4"] = 0x34,
        ["5"] = 0x35, ["6"] = 0x36, ["7"] = 0x37, ["8"] = 0x38, ["9"] = 0x39,
        ["A"] = 0x41, ["B"] = 0x42, ["C"] = 0x43, ["D"] = 0x44, ["E"] = 0x45,
        ["F"] = 0x46, ["G"] = 0x47, ["H"] = 0x48, ["I"] = 0x49, ["J"] = 0x4A,
        ["K"] = 0x4B, ["L"] = 0x4C, ["M"] = 0x4D, ["N"] = 0x4E, ["O"] = 0x4F,
        ["P"] = 0x50, ["Q"] = 0x51, ["R"] = 0x52, ["S"] = 0x53, ["T"] = 0x54,
        ["U"] = 0x55, ["V"] = 0x56, ["W"] = 0x57, ["X"] = 0x58, ["Y"] = 0x59,
        ["Z"] = 0x5A,
        ["TAB"] = 0x09, ["ESC"] = 0x1B, ["ESCAPE"] = 0x1B,
        ["SPACE"] = 0x20, ["ENTER"] = 0x0D, ["RETURN"] = 0x0D,
        ["BACKSPACE"] = 0x08, ["DELETE"] = 0x2E, ["INSERT"] = 0x2D,
        ["HOME"] = 0x24, ["END"] = 0x23, ["PAGEUP"] = 0x21, ["PAGEDOWN"] = 0x22,
        ["UP"] = 0x26, ["DOWN"] = 0x28, ["LEFT"] = 0x25, ["RIGHT"] = 0x27,
        ["PRINTSCREEN"] = 0x2C, ["SCROLLLOCK"] = 0x91, ["PAUSE"] = 0x13,
        ["CAPSLOCK"] = 0x14, ["NUMLOCK"] = 0x90, ["APPS"] = 0x5D,
        ["VOLUME_UP"] = 0xAF, ["VOLUME_DOWN"] = 0xAE, ["VOLUME_MUTE"] = 0xAD,
        ["MEDIA_NEXT"] = 0xB0, ["MEDIA_PREV"] = 0xB1, ["MEDIA_PLAY_PAUSE"] = 0xB3, ["MEDIA_STOP"] = 0xB2,
        ["NEXT_TRACK"] = 0xB0, ["PREV_TRACK"] = 0xB1,
        ["LWIN"] = 0x5B, ["RWIN"] = 0x5C,
        ["LCONTROL"] = 0xA2, ["RCONTROL"] = 0xA3,
        ["LMENU"] = 0xA4, ["RMENU"] = 0xA5,
        ["SHIFT"] = 0x10, ["CONTROL"] = 0x11, ["CTRL"] = 0x11, ["ALT"] = 0x12
    };

    public static IReadOnlyList<MacroHotkey> Load(string path, Action<string, object[]> logInfo, Action<string, object[]> logError)
    {
        if (!File.Exists(path))
        {
            logError("Config {0} not found, shortcuts disabled.", new object[] { Path.GetFileName(path) });
            return Array.Empty<MacroHotkey>();
        }

        var mappings = new List<MacroHotkey>();
        var inButtons = false;
        foreach (var rawLine in File.ReadLines(path))
        {
            var line = StripComment(rawLine).Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (line.StartsWith("[", StringComparison.Ordinal))
            {
                var end = line.IndexOf(']');
                inButtons = end > 0 && line.Substring(1, end - 1).Equals("Buttons", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (!inButtons)
            {
                continue;
            }

            var equals = line.IndexOf('=');
            if (equals <= 0)
            {
                continue;
            }

            var code = line[..equals].Trim();
            var shortcut = line[(equals + 1)..].Trim();
            if (code.Length == 0 || code.Length >= 16)
            {
                continue;
            }

            var (modifiers, mainKey) = ParseShortcut(shortcut);
            mappings.Add(new MacroHotkey(code, modifiers, mainKey, IsRepeatable(mainKey)));
            if (mappings.Count >= 128)
            {
                break;
            }
        }

        logInfo("Loaded {0} shortcut mappings from {1}", new object[] { mappings.Count, Path.GetFileName(path) });
        return mappings;
    }

    private static string StripComment(string line)
    {
        var semicolon = line.IndexOf(';');
        var hash = line.IndexOf('#');
        var cut = semicolon < 0 ? hash : hash < 0 ? semicolon : Math.Min(semicolon, hash);
        return cut < 0 ? line : line[..cut];
    }

    private static (ushort Modifiers, ushort MainKey) ParseShortcut(string shortcut)
    {
        ushort modifiers = 0;
        ushort mainKey = 0;
        foreach (var part in shortcut.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (part.Equals("CTRL", StringComparison.OrdinalIgnoreCase) || part.Equals("CONTROL", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= ModControl;
            }
            else if (part.Equals("ALT", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= ModAlt;
            }
            else if (part.Equals("SHIFT", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= ModShift;
            }
            else if (part.Equals("LWIN", StringComparison.OrdinalIgnoreCase) || part.Equals("RWIN", StringComparison.OrdinalIgnoreCase) || part.Equals("WIN", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= ModWin;
            }
            else if (KeyNames.TryGetValue(part, out var vk))
            {
                mainKey = vk;
            }
        }

        return (modifiers, mainKey);
    }

    private static bool IsRepeatable(ushort vk)
    {
        return vk is 0xAF or 0xAE or 0xAD or 0xB0 or 0xB1 or 0xB2 or 0xB3 or 0x26 or 0x28 or 0x25 or 0x27;
    }
}

internal static class KeyboardSender
{
    private const ushort ModAlt = 1;
    private const ushort ModControl = 2;
    private const ushort ModShift = 4;
    private const ushort ModWin = 8;
    private const uint InputKeyboard = 1;
    private const uint KeyUp = 0x0002;
    private const ushort VkMenu = 0x12;
    private const ushort VkControl = 0x11;
    private const ushort VkShift = 0x10;
    private const ushort VkLWin = 0x5B;

    public static void PressCombo(MacroHotkey mapping)
    {
        if (mapping.Modifiers == 0 && mapping.MainKey == 0)
        {
            return;
        }

        var keys = new List<ushort>(5);
        if ((mapping.Modifiers & ModAlt) != 0) keys.Add(VkMenu);
        if ((mapping.Modifiers & ModControl) != 0) keys.Add(VkControl);
        if ((mapping.Modifiers & ModShift) != 0) keys.Add(VkShift);
        if ((mapping.Modifiers & ModWin) != 0) keys.Add(VkLWin);
        if (mapping.MainKey != 0) keys.Add(mapping.MainKey);

        Send(keys, keyUp: false);
        Thread.Sleep(20);
        keys.Reverse();
        Send(keys, keyUp: true);
    }

    private static void Send(IReadOnlyList<ushort> keys, bool keyUp)
    {
        var inputs = keys.Select(key => new NativeInput
        {
            Type = InputKeyboard,
            Data = new InputUnion
            {
                Keyboard = new KeyboardInput
                {
                    VirtualKey = key,
                    Flags = keyUp ? KeyUp : 0
                }
            }
        }).ToArray();

        if (inputs.Length > 0)
        {
            _ = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeInput>());
        }
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint numberOfInputs, NativeInput[] inputs, int sizeOfInputStructure);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeInput
    {
        public uint Type;
        public InputUnion Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public KeyboardInput Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }
}

internal static class VigemNative
{
    public const ushort XusbGamepadDpadUp = 0x0001;
    public const ushort XusbGamepadDpadDown = 0x0002;
    public const ushort XusbGamepadDpadLeft = 0x0004;
    public const ushort XusbGamepadDpadRight = 0x0008;
    public const ushort XusbGamepadStart = 0x0010;
    public const ushort XusbGamepadBack = 0x0020;
    public const ushort XusbGamepadLeftThumb = 0x0040;
    public const ushort XusbGamepadRightThumb = 0x0080;
    public const ushort XusbGamepadLeftShoulder = 0x0100;
    public const ushort XusbGamepadRightShoulder = 0x0200;
    public const ushort XusbGamepadGuide = 0x0400;
    public const ushort XusbGamepadA = 0x1000;
    public const ushort XusbGamepadB = 0x2000;
    public const ushort XusbGamepadX = 0x4000;
    public const ushort XusbGamepadY = 0x8000;

    private const uint VigEmErrorNone = 0x20000000;

    public static bool Success(uint error) => error == VigEmErrorNone;

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate void X360Notification(IntPtr client, IntPtr target, byte largeMotor, byte smallMotor, byte ledNumber, IntPtr userData);

    [StructLayout(LayoutKind.Sequential)]
    public struct XusbReport
    {
        public ushort Buttons;
        public byte LeftTrigger;
        public byte RightTrigger;
        public short ThumbLX;
        public short ThumbLY;
        public short ThumbRX;
        public short ThumbRY;
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.ApplicationDirectory | DllImportSearchPath.AssemblyDirectory)]
    [DllImport("ViGEmClient.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr vigem_alloc();

    [DefaultDllImportSearchPaths(DllImportSearchPath.ApplicationDirectory | DllImportSearchPath.AssemblyDirectory)]
    [DllImport("ViGEmClient.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern void vigem_free(IntPtr vigem);

    [DefaultDllImportSearchPaths(DllImportSearchPath.ApplicationDirectory | DllImportSearchPath.AssemblyDirectory)]
    [DllImport("ViGEmClient.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern uint vigem_connect(IntPtr vigem);

    [DefaultDllImportSearchPaths(DllImportSearchPath.ApplicationDirectory | DllImportSearchPath.AssemblyDirectory)]
    [DllImport("ViGEmClient.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern void vigem_disconnect(IntPtr vigem);

    [DefaultDllImportSearchPaths(DllImportSearchPath.ApplicationDirectory | DllImportSearchPath.AssemblyDirectory)]
    [DllImport("ViGEmClient.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr vigem_target_x360_alloc();

    [DefaultDllImportSearchPaths(DllImportSearchPath.ApplicationDirectory | DllImportSearchPath.AssemblyDirectory)]
    [DllImport("ViGEmClient.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern void vigem_target_free(IntPtr target);

    [DefaultDllImportSearchPaths(DllImportSearchPath.ApplicationDirectory | DllImportSearchPath.AssemblyDirectory)]
    [DllImport("ViGEmClient.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern uint vigem_target_add(IntPtr vigem, IntPtr target);

    [DefaultDllImportSearchPaths(DllImportSearchPath.ApplicationDirectory | DllImportSearchPath.AssemblyDirectory)]
    [DllImport("ViGEmClient.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern uint vigem_target_remove(IntPtr vigem, IntPtr target);

    [DefaultDllImportSearchPaths(DllImportSearchPath.ApplicationDirectory | DllImportSearchPath.AssemblyDirectory)]
    [DllImport("ViGEmClient.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern uint vigem_target_x360_register_notification(IntPtr vigem, IntPtr target, X360Notification notification, IntPtr userData);

    [DefaultDllImportSearchPaths(DllImportSearchPath.ApplicationDirectory | DllImportSearchPath.AssemblyDirectory)]
    [DllImport("ViGEmClient.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern void vigem_target_x360_unregister_notification(IntPtr target);

    [DefaultDllImportSearchPaths(DllImportSearchPath.ApplicationDirectory | DllImportSearchPath.AssemblyDirectory)]
    [DllImport("ViGEmClient.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern uint vigem_target_x360_update(IntPtr vigem, IntPtr target, XusbReport report);
}
