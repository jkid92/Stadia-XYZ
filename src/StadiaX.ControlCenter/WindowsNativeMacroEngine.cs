using System.Collections.Concurrent;

namespace StadiaX.ControlCenter;

internal sealed class WindowsNativeMacroEngine : IDisposable
{
    private const int ControllerCount = 4;
    private const long RepeatDelayMs = 350;
    private const long RepeatIntervalMs = 100;
    private const uint SuppressedButtons =
        ButtonBits.A | ButtonBits.B | ButtonBits.X | ButtonBits.Y |
        ButtonBits.Lb | ButtonBits.Rb | ButtonBits.Select | ButtonBits.Start |
        ButtonBits.Stadia | ButtonBits.L3 | ButtonBits.R3 |
        ButtonBits.Assistant | ButtonBits.Capture |
        ButtonBits.DpadUp | ButtonBits.DpadDown | ButtonBits.DpadLeft | ButtonBits.DpadRight;

    private static readonly MacroInput[] Inputs =
    [
        new("A", ButtonBits.A),
        new("B", ButtonBits.B),
        new("X", ButtonBits.X),
        new("Y", ButtonBits.Y),
        new("UP", ButtonBits.DpadUp),
        new("DOWN", ButtonBits.DpadDown),
        new("LEFT", ButtonBits.DpadLeft),
        new("RIGHT", ButtonBits.DpadRight),
        new("LB", ButtonBits.Lb),
        new("RB", ButtonBits.Rb),
        new("L2", 0, TriggerSide.Left),
        new("R2", 0, TriggerSide.Right),
        new("L3", ButtonBits.L3),
        new("R3", ButtonBits.R3),
        new("SELECT", ButtonBits.Select),
        new("START", ButtonBits.Start),
        new("STADIA", ButtonBits.Stadia)
    ];

    private readonly string _configPath;
    private readonly Action<string, object[]> _logInfo;
    private readonly Action<string, object[]> _logError;
    private readonly Action<MacroHotkey> _dispatch;
    private readonly BlockingCollection<MacroHotkey>? _queue;
    private readonly Task? _worker;
    private readonly ControllerMacroState[] _controllers =
        Enumerable.Range(0, ControllerCount).Select(_ => new ControllerMacroState()).ToArray();
    private IReadOnlyDictionary<string, MacroHotkey> _mappings =
        new Dictionary<string, MacroHotkey>(StringComparer.OrdinalIgnoreCase);
    private DateTime _configWriteUtc = DateTime.MinValue;
    private long _nextReloadTick;
    private int _disposed;

    public WindowsNativeMacroEngine(
        string configPath,
        Action<string, object[]> logInfo,
        Action<string, object[]> logError,
        Action<MacroHotkey>? dispatcher = null)
    {
        _configPath = configPath;
        _logInfo = logInfo;
        _logError = logError;

        if (dispatcher is not null)
        {
            _dispatch = dispatcher;
        }
        else
        {
            _queue = new BlockingCollection<MacroHotkey>(64);
            _dispatch = mapping =>
            {
                if (!_queue.IsAddingCompleted)
                {
                    _ = _queue.TryAdd(mapping);
                }
            };
            _worker = Task.Run(DispatchLoop);
        }

        ReloadMappings(force: true);
    }

    public ControllerState ProcessState(int controllerIndex, ControllerState state)
    {
        if (controllerIndex < 0 || controllerIndex >= _controllers.Length ||
            Volatile.Read(ref _disposed) != 0)
        {
            return state;
        }

        var now = Environment.TickCount64;
        if (now >= Volatile.Read(ref _nextReloadTick))
        {
            Volatile.Write(ref _nextReloadTick, now + 1000);
            ReloadMappings(force: false);
        }

        var assistant = state.Has(ButtonBits.Assistant);
        var capture = state.Has(ButtonBits.Capture);
        var controller = _controllers[controllerIndex];
        var assistantChord = false;
        var captureChord = false;

        foreach (var input in Inputs)
        {
            var active = input.IsActive(state);
            ProcessSlot(controller, "A_" + input.Code, assistant && active, now);
            ProcessSlot(controller, "C_" + input.Code, capture && active, now);
            assistantChord |= assistant && active;
            captureChord |= capture && active;
        }

        ProcessSlot(controller, "A", assistant && !assistantChord, now);
        ProcessSlot(controller, "C", capture && !captureChord, now);

        if (!assistant && !capture)
        {
            return state;
        }

        return state with
        {
            Buttons = state.Buttons & ~SuppressedButtons,
            TriggerLeft = 0,
            TriggerRight = 0
        };
    }

    public void ResetController(int controllerIndex)
    {
        if (controllerIndex < 0 || controllerIndex >= _controllers.Length)
        {
            return;
        }

        _controllers[controllerIndex].Slots.Clear();
    }

    private void ProcessSlot(ControllerMacroState controller, string code, bool active, long now)
    {
        if (!controller.Slots.TryGetValue(code, out var slot))
        {
            slot = new MacroSlotState();
            controller.Slots[code] = slot;
        }

        if (!active)
        {
            slot.Held = false;
            slot.FirstFireTick = 0;
            slot.LastFireTick = 0;
            return;
        }

        var mappings = Volatile.Read(ref _mappings);
        if (!mappings.TryGetValue(code, out var mapping))
        {
            slot.Held = true;
            return;
        }

        if (!slot.Held)
        {
            slot.Held = true;
            slot.FirstFireTick = now;
            slot.LastFireTick = now;
            _dispatch(mapping);
            return;
        }

        if (mapping.Repeat &&
            now - slot.FirstFireTick >= RepeatDelayMs &&
            now - slot.LastFireTick >= RepeatIntervalMs)
        {
            slot.LastFireTick = now;
            _dispatch(mapping);
        }
    }

    private void ReloadMappings(bool force)
    {
        try
        {
            var writeUtc = File.Exists(_configPath)
                ? File.GetLastWriteTimeUtc(_configPath)
                : DateTime.MinValue;
            if (!force && writeUtc == _configWriteUtc)
            {
                return;
            }

            var loaded = MacroMappingLoader.Load(_configPath, _logInfo, _logError)
                .GroupBy(mapping => mapping.Code, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);
            Volatile.Write(ref _mappings, loaded);
            _configWriteUtc = writeUtc;
            _logInfo(
                "Windows Native macro configuration active: {0} shortcut(s)",
                new object[] { loaded.Count });
        }
        catch (Exception ex)
        {
            _logError(
                "Windows Native macro configuration reload failed: {0}",
                new object[] { ex.Message });
        }
    }

    private void DispatchLoop()
    {
        if (_queue is null)
        {
            return;
        }

        foreach (var mapping in _queue.GetConsumingEnumerable())
        {
            try
            {
                KeyboardSender.PressCombo(mapping);
            }
            catch (Exception ex)
            {
                _logError(
                    "Windows Native macro {0} failed: {1}",
                    new object[] { mapping.Code, ex.Message });
            }
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _queue?.CompleteAdding();
        try
        {
            _worker?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException ex)
        {
            _logError(
                "Windows Native macro worker shutdown failed: {0}",
                new object[] { ex.GetBaseException().Message });
        }
        _queue?.Dispose();
    }

    internal static void RunSelfTest()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "StadiaX-NativeMacros-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "stadia_buttons.ini");
        try
        {
            File.WriteAllText(path, "[Buttons]\r\nA_A=ALT+Z\r\nC_UP=VOLUME_UP\r\n");
            var fired = new List<MacroHotkey>();
            using var engine = new WindowsNativeMacroEngine(
                path,
                (_, _) => { },
                (_, _) => { },
                fired.Add);

            var assistantChord = new ControllerState(
                ButtonBits.Assistant | ButtonBits.A,
                180,
                160,
                0,
                0,
                0,
                0);
            var filtered = engine.ProcessState(0, assistantChord);
            if (filtered.Buttons != 0 ||
                filtered.TriggerLeft != 0 ||
                filtered.TriggerRight != 0 ||
                fired.Count != 1 ||
                !fired[0].Code.Equals("A_A", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Windows Native macro chord self-test failed.");
            }

            _ = engine.ProcessState(0, default);
            _ = engine.ProcessState(
                0,
                new ControllerState(ButtonBits.Capture | ButtonBits.DpadUp, 0, 0, 0, 0, 0, 0));
            if (fired.Count != 2 ||
                !fired[1].Code.Equals("C_UP", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Windows Native capture macro self-test failed.");
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class ControllerMacroState
    {
        public Dictionary<string, MacroSlotState> Slots { get; } =
            new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class MacroSlotState
    {
        public bool Held { get; set; }
        public long FirstFireTick { get; set; }
        public long LastFireTick { get; set; }
    }

    private readonly record struct MacroInput(
        string Code,
        uint Button,
        TriggerSide Trigger = TriggerSide.None)
    {
        public bool IsActive(ControllerState state)
        {
            return Trigger switch
            {
                TriggerSide.Left => state.TriggerLeft > 128,
                TriggerSide.Right => state.TriggerRight > 128,
                _ => state.Has(Button)
            };
        }
    }

    private enum TriggerSide
    {
        None,
        Left,
        Right
    }
}
