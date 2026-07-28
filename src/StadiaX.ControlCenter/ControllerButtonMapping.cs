using System.Text.Json;

namespace StadiaX.ControlCenter;

internal enum ControllerInputButton
{
    A,
    B,
    X,
    Y,
    Lb,
    Rb,
    Select,
    Start,
    Stadia,
    L3,
    R3,
    Assistant,
    Capture,
    DpadUp,
    DpadDown,
    DpadLeft,
    DpadRight
}

internal enum XboxOutputButton
{
    None,
    A,
    B,
    X,
    Y,
    LeftShoulder,
    RightShoulder,
    Back,
    Start,
    Guide,
    LeftStick,
    RightStick,
    DpadUp,
    DpadDown,
    DpadLeft,
    DpadRight
}

internal sealed record ControllerInputDescriptor(
    ControllerInputButton Id,
    string TelemetryKey,
    string DisplayName,
    uint Bit)
{
    public override string ToString() => UiLocalization.Current.Translate(DisplayName);
}

internal sealed record XboxOutputDescriptor(
    XboxOutputButton Id,
    string DisplayName,
    ushort Mask)
{
    public override string ToString() => UiLocalization.Current.Translate(DisplayName);
}

internal static class ControllerButtonCatalog
{
    public static readonly IReadOnlyList<ControllerInputDescriptor> Inputs =
    [
        new(ControllerInputButton.A, "a", "A", ButtonBits.A),
        new(ControllerInputButton.B, "b", "B", ButtonBits.B),
        new(ControllerInputButton.X, "x", "X", ButtonBits.X),
        new(ControllerInputButton.Y, "y", "Y", ButtonBits.Y),
        new(ControllerInputButton.Lb, "lb", "LB", ButtonBits.Lb),
        new(ControllerInputButton.Rb, "rb", "RB", ButtonBits.Rb),
        new(ControllerInputButton.Select, "select", "Options", ButtonBits.Select),
        new(ControllerInputButton.Start, "start", "Menu", ButtonBits.Start),
        new(ControllerInputButton.Stadia, "stadia", "Stadia", ButtonBits.Stadia),
        new(ControllerInputButton.L3, "l3", "L3", ButtonBits.L3),
        new(ControllerInputButton.R3, "r3", "R3", ButtonBits.R3),
        new(ControllerInputButton.Assistant, "assistant", "Google Assistant", ButtonBits.Assistant),
        new(ControllerInputButton.Capture, "capture", "Capture", ButtonBits.Capture),
        new(ControllerInputButton.DpadUp, "dpad_up", "D-pad Up", ButtonBits.DpadUp),
        new(ControllerInputButton.DpadDown, "dpad_down", "D-pad Down", ButtonBits.DpadDown),
        new(ControllerInputButton.DpadLeft, "dpad_left", "D-pad Left", ButtonBits.DpadLeft),
        new(ControllerInputButton.DpadRight, "dpad_right", "D-pad Right", ButtonBits.DpadRight)
    ];

    public static readonly IReadOnlyList<XboxOutputDescriptor> Outputs =
    [
        new(XboxOutputButton.None, "Disabled", 0),
        new(XboxOutputButton.A, "A", VigemNative.XusbGamepadA),
        new(XboxOutputButton.B, "B", VigemNative.XusbGamepadB),
        new(XboxOutputButton.X, "X", VigemNative.XusbGamepadX),
        new(XboxOutputButton.Y, "Y", VigemNative.XusbGamepadY),
        new(XboxOutputButton.LeftShoulder, "LB", VigemNative.XusbGamepadLeftShoulder),
        new(XboxOutputButton.RightShoulder, "RB", VigemNative.XusbGamepadRightShoulder),
        new(XboxOutputButton.Back, "Back", VigemNative.XusbGamepadBack),
        new(XboxOutputButton.Start, "Start", VigemNative.XusbGamepadStart),
        new(XboxOutputButton.Guide, "Xbox Guide", VigemNative.XusbGamepadGuide),
        new(XboxOutputButton.LeftStick, "L3", VigemNative.XusbGamepadLeftThumb),
        new(XboxOutputButton.RightStick, "R3", VigemNative.XusbGamepadRightThumb),
        new(XboxOutputButton.DpadUp, "D-pad Up", VigemNative.XusbGamepadDpadUp),
        new(XboxOutputButton.DpadDown, "D-pad Down", VigemNative.XusbGamepadDpadDown),
        new(XboxOutputButton.DpadLeft, "D-pad Left", VigemNative.XusbGamepadDpadLeft),
        new(XboxOutputButton.DpadRight, "D-pad Right", VigemNative.XusbGamepadDpadRight)
    ];

    public static ControllerInputDescriptor? FindInput(string telemetryKey)
    {
        return Inputs.FirstOrDefault(input =>
            input.TelemetryKey.Equals(telemetryKey, StringComparison.OrdinalIgnoreCase));
    }

    public static XboxOutputDescriptor Output(XboxOutputButton id)
    {
        return Outputs.First(output => output.Id == id);
    }
}

internal sealed class ControllerButtonMapping
{
    private readonly IReadOnlyDictionary<ControllerInputButton, XboxOutputButton> _buttons;

    private ControllerButtonMapping(IReadOnlyDictionary<ControllerInputButton, XboxOutputButton> buttons)
    {
        _buttons = new Dictionary<ControllerInputButton, XboxOutputButton>(buttons);
    }

    public XboxOutputButton this[ControllerInputButton input] =>
        _buttons.TryGetValue(input, out var output) ? output : XboxOutputButton.None;

    public static ControllerButtonMapping CreateDefault()
    {
        return new ControllerButtonMapping(new Dictionary<ControllerInputButton, XboxOutputButton>
        {
            [ControllerInputButton.A] = XboxOutputButton.A,
            [ControllerInputButton.B] = XboxOutputButton.B,
            [ControllerInputButton.X] = XboxOutputButton.X,
            [ControllerInputButton.Y] = XboxOutputButton.Y,
            [ControllerInputButton.Lb] = XboxOutputButton.LeftShoulder,
            [ControllerInputButton.Rb] = XboxOutputButton.RightShoulder,
            [ControllerInputButton.Select] = XboxOutputButton.Back,
            [ControllerInputButton.Start] = XboxOutputButton.Start,
            [ControllerInputButton.Stadia] = XboxOutputButton.Guide,
            [ControllerInputButton.L3] = XboxOutputButton.LeftStick,
            [ControllerInputButton.R3] = XboxOutputButton.RightStick,
            [ControllerInputButton.Assistant] = XboxOutputButton.None,
            [ControllerInputButton.Capture] = XboxOutputButton.None,
            [ControllerInputButton.DpadUp] = XboxOutputButton.DpadUp,
            [ControllerInputButton.DpadDown] = XboxOutputButton.DpadDown,
            [ControllerInputButton.DpadLeft] = XboxOutputButton.DpadLeft,
            [ControllerInputButton.DpadRight] = XboxOutputButton.DpadRight
        });
    }

    public ControllerButtonMapping With(ControllerInputButton input, XboxOutputButton output)
    {
        var updated = new Dictionary<ControllerInputButton, XboxOutputButton>(_buttons)
        {
            [input] = output
        };
        return new ControllerButtonMapping(updated);
    }

    public ushort MapButtons(ControllerState state)
    {
        ushort result = 0;
        foreach (var input in ControllerButtonCatalog.Inputs)
        {
            if (state.Has(input.Bit))
            {
                result |= ControllerButtonCatalog.Output(this[input.Id]).Mask;
            }
        }
        return result;
    }

    public IReadOnlyDictionary<ControllerInputButton, XboxOutputButton> Snapshot()
    {
        return new Dictionary<ControllerInputButton, XboxOutputButton>(_buttons);
    }
}

internal static class ControllerButtonMappingStore
{
    private const int SchemaVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static ControllerButtonMapping Load(string path, Action<string>? warning = null)
    {
        if (TryLoad(path, out var mapping, out var error))
        {
            return mapping;
        }

        warning?.Invoke(error);
        return ControllerButtonMapping.CreateDefault();
    }

    public static bool TryLoad(string path, out ControllerButtonMapping mapping, out string error)
    {
        mapping = ControllerButtonMapping.CreateDefault();
        error = "";
        if (!File.Exists(path))
        {
            return true;
        }

        try
        {
            mapping = Deserialize(File.ReadAllText(path));
            return true;
        }
        catch (Exception ex)
        {
            error = $"Button mapping file is invalid: {ex.Message}";
            return false;
        }
    }

    public static void Save(string path, ControllerButtonMapping mapping)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = path + ".tmp";
        File.WriteAllText(tempPath, Serialize(mapping));
        File.Move(tempPath, path, overwrite: true);
    }

    internal static string Serialize(ControllerButtonMapping mapping)
    {
        var buttons = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var input in ControllerButtonCatalog.Inputs)
        {
            buttons[input.Id.ToString()] = mapping[input.Id].ToString();
        }

        return JsonSerializer.Serialize(new MappingDocument(SchemaVersion, buttons), JsonOptions);
    }

    internal static ControllerButtonMapping Deserialize(string json)
    {
        var document = JsonSerializer.Deserialize<MappingDocument>(json) ??
                       throw new InvalidDataException("The document is empty.");
        if (document.Schema != SchemaVersion)
        {
            throw new InvalidDataException($"Unsupported schema {document.Schema}.");
        }

        var mapping = ControllerButtonMapping.CreateDefault();
        foreach (var pair in document.Buttons ?? new Dictionary<string, string>())
        {
            if (!Enum.TryParse<ControllerInputButton>(pair.Key, ignoreCase: true, out var input))
            {
                continue;
            }
            if (!Enum.TryParse<XboxOutputButton>(pair.Value, ignoreCase: true, out var output))
            {
                throw new InvalidDataException($"Unknown Xbox output '{pair.Value}' for '{pair.Key}'.");
            }
            mapping = mapping.With(input, output);
        }
        return mapping;
    }

    public static void RunSelfTest()
    {
        var defaults = ControllerButtonMapping.CreateDefault();
        var state = new ControllerState(ButtonBits.A | ButtonBits.Capture, 0, 0, 0, 0, 0, 0);
        if (defaults.MapButtons(state) != VigemNative.XusbGamepadA)
        {
            throw new InvalidOperationException("Default button mapping self-test failed.");
        }

        var customized = defaults
            .With(ControllerInputButton.A, XboxOutputButton.B)
            .With(ControllerInputButton.Capture, XboxOutputButton.A);
        var expected = (ushort)(VigemNative.XusbGamepadA | VigemNative.XusbGamepadB);
        if (customized.MapButtons(state) != expected)
        {
            throw new InvalidOperationException("Custom button mapping self-test failed.");
        }

        var roundTrip = Deserialize(Serialize(customized));
        if (roundTrip.MapButtons(state) != expected)
        {
            throw new InvalidOperationException("Button mapping serialization self-test failed.");
        }
    }

    private sealed record MappingDocument(int Schema, Dictionary<string, string>? Buttons);
}

internal sealed class ControllerButtonMappingProvider
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMilliseconds(500);

    private readonly string _path;
    private readonly Action<string>? _info;
    private readonly Action<string>? _warning;
    private readonly object _sync = new();

    private ControllerButtonMapping _current = ControllerButtonMapping.CreateDefault();
    private DateTime _lastWriteUtc;
    private long _lastLength = -1;
    private bool _lastExists;
    private long _nextRefreshTick;

    public ControllerButtonMappingProvider(string path, Action<string>? info = null, Action<string>? warning = null)
    {
        _path = path;
        _info = info;
        _warning = warning;
        Refresh(force: true);
    }

    public ControllerButtonMapping GetCurrent()
    {
        Refresh(force: false);
        lock (_sync)
        {
            return _current;
        }
    }

    private void Refresh(bool force)
    {
        var now = Environment.TickCount64;
        if (!force && now < Interlocked.Read(ref _nextRefreshTick))
        {
            return;
        }
        Interlocked.Exchange(ref _nextRefreshTick, now + (long)RefreshInterval.TotalMilliseconds);

        lock (_sync)
        {
            try
            {
                var exists = File.Exists(_path);
                var lastWriteUtc = exists ? File.GetLastWriteTimeUtc(_path) : DateTime.MinValue;
                var length = exists ? new FileInfo(_path).Length : -1;
                if (!force &&
                    exists == _lastExists &&
                    lastWriteUtc == _lastWriteUtc &&
                    length == _lastLength)
                {
                    return;
                }

                _lastExists = exists;
                _lastWriteUtc = lastWriteUtc;
                _lastLength = length;
                if (ControllerButtonMappingStore.TryLoad(_path, out var loaded, out var error))
                {
                    _current = loaded;
                    _info?.Invoke(exists
                        ? $"Button mapping loaded from {_path}"
                        : "Default button mapping active");
                }
                else
                {
                    _warning?.Invoke($"{error} Keeping the last valid mapping.");
                }
            }
            catch (Exception ex)
            {
                _warning?.Invoke($"Button mapping refresh failed: {ex.Message}. Keeping the last valid mapping.");
            }
        }
    }
}
