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

    public static ControllerInputDescriptor Input(ControllerInputButton id)
    {
        return Inputs.First(input => input.Id == id);
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

    public ControllerButtonMapping AssignOutput(XboxOutputButton output, ControllerInputButton? input)
    {
        if (output == XboxOutputButton.None)
        {
            throw new ArgumentOutOfRangeException(nameof(output), "Disabled is not a mappable Xbox output.");
        }

        var updated = new Dictionary<ControllerInputButton, XboxOutputButton>(_buttons);
        foreach (var physicalInput in ControllerButtonCatalog.Inputs)
        {
            if (updated.TryGetValue(physicalInput.Id, out var current) && current == output)
            {
                updated[physicalInput.Id] = XboxOutputButton.None;
            }
        }

        if (input is not null)
        {
            updated[input.Value] = output;
        }

        return new ControllerButtonMapping(updated);
    }

    public IReadOnlyList<ControllerInputButton> InputsFor(XboxOutputButton output)
    {
        return ControllerButtonCatalog.Inputs
            .Where(input => this[input.Id] == output)
            .Select(input => input.Id)
            .ToArray();
    }

    public ControllerMappingValidation Validate()
    {
        var missing = new List<XboxOutputButton>();
        var duplicate = new List<XboxOutputButton>();
        foreach (var output in ControllerButtonCatalog.Outputs.Where(output => output.Id != XboxOutputButton.None))
        {
            var count = InputsFor(output.Id).Count;
            if (count == 0)
            {
                missing.Add(output.Id);
            }
            else if (count > 1)
            {
                duplicate.Add(output.Id);
            }
        }

        return new ControllerMappingValidation(
            ControllerButtonCatalog.Outputs.Count - 1 - missing.Count,
            ControllerButtonCatalog.Outputs.Count - 1,
            missing,
            duplicate);
    }

    public string Fingerprint()
    {
        return string.Join(
            ";",
            ControllerButtonCatalog.Inputs.Select(input => $"{input.Id}={this[input.Id]}"));
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

internal sealed record ControllerMappingValidation(
    int AssignedOutputCount,
    int TotalOutputCount,
    IReadOnlyList<XboxOutputButton> MissingOutputs,
    IReadOnlyList<XboxOutputButton> DuplicateOutputs)
{
    public bool IsComplete => MissingOutputs.Count == 0 && DuplicateOutputs.Count == 0;
}

internal sealed record ControllerMappingProfile(string Id, string Name, ControllerButtonMapping Mapping)
{
    public override string ToString() => Name;
}

internal sealed class ControllerMappingConfiguration
{
    private readonly IReadOnlyList<ControllerMappingProfile> _profiles;

    public ControllerMappingConfiguration(
        IEnumerable<ControllerMappingProfile> profiles,
        string activeProfileId)
    {
        _profiles = profiles.ToArray();
        if (_profiles.Count == 0)
        {
            throw new ArgumentException("At least one mapping profile is required.", nameof(profiles));
        }
        if (_profiles.Select(profile => profile.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != _profiles.Count)
        {
            throw new ArgumentException("Mapping profile IDs must be unique.", nameof(profiles));
        }
        if (_profiles.All(profile => !profile.Id.Equals(activeProfileId, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException($"Active mapping profile '{activeProfileId}' was not found.", nameof(activeProfileId));
        }

        ActiveProfileId = activeProfileId;
    }

    public IReadOnlyList<ControllerMappingProfile> Profiles => _profiles;

    public string ActiveProfileId { get; }

    public ControllerMappingProfile ActiveProfile =>
        _profiles.First(profile => profile.Id.Equals(ActiveProfileId, StringComparison.OrdinalIgnoreCase));

    public ControllerButtonMapping ActiveMapping => ActiveProfile.Mapping;

    public static ControllerMappingConfiguration CreateDefault()
    {
        var profile = new ControllerMappingProfile(
            "stadia-standard",
            "Stadia standard",
            ControllerButtonMapping.CreateDefault());
        return new ControllerMappingConfiguration([profile], profile.Id);
    }

    public ControllerMappingConfiguration WithActiveProfile(string profileId)
    {
        return new ControllerMappingConfiguration(_profiles, profileId);
    }

    public ControllerMappingConfiguration ReplaceProfile(
        string profileId,
        string name,
        ControllerButtonMapping mapping)
    {
        var normalizedName = NormalizeProfileName(name);
        var replaced = false;
        var profiles = _profiles.Select(profile =>
        {
            if (!profile.Id.Equals(profileId, StringComparison.OrdinalIgnoreCase))
            {
                return profile;
            }

            replaced = true;
            return profile with { Name = normalizedName, Mapping = mapping };
        }).ToArray();
        if (!replaced)
        {
            throw new ArgumentException($"Mapping profile '{profileId}' was not found.", nameof(profileId));
        }

        return new ControllerMappingConfiguration(profiles, ActiveProfileId);
    }

    public ControllerMappingConfiguration AddProfile(string name, ControllerButtonMapping mapping)
    {
        var profile = new ControllerMappingProfile(
            Guid.NewGuid().ToString("N"),
            NormalizeProfileName(name),
            mapping);
        return new ControllerMappingConfiguration(_profiles.Append(profile), profile.Id);
    }

    public ControllerMappingConfiguration RemoveProfile(string profileId)
    {
        if (_profiles.Count == 1)
        {
            throw new InvalidOperationException("The last mapping profile cannot be removed.");
        }

        var profiles = _profiles
            .Where(profile => !profile.Id.Equals(profileId, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (profiles.Length == _profiles.Count)
        {
            throw new ArgumentException($"Mapping profile '{profileId}' was not found.", nameof(profileId));
        }

        var activeProfileId = ActiveProfileId.Equals(profileId, StringComparison.OrdinalIgnoreCase)
            ? profiles[0].Id
            : ActiveProfileId;
        return new ControllerMappingConfiguration(profiles, activeProfileId);
    }

    private static string NormalizeProfileName(string name)
    {
        var normalized = (name ?? "").Trim();
        if (normalized.Length == 0)
        {
            throw new ArgumentException("Mapping profile name cannot be empty.", nameof(name));
        }
        return normalized.Length <= 64 ? normalized : normalized[..64];
    }
}

internal static class ControllerButtonMappingStore
{
    private const int SchemaVersion = 2;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static ControllerButtonMapping Load(string path, Action<string>? warning = null)
    {
        return LoadConfiguration(path, warning).ActiveMapping;
    }

    public static ControllerMappingConfiguration LoadConfiguration(string path, Action<string>? warning = null)
    {
        if (TryLoadConfiguration(path, out var configuration, out var error))
        {
            return configuration;
        }

        warning?.Invoke(error);
        return ControllerMappingConfiguration.CreateDefault();
    }

    public static bool TryLoad(string path, out ControllerButtonMapping mapping, out string error)
    {
        var loaded = TryLoadConfiguration(path, out var configuration, out error);
        mapping = configuration.ActiveMapping;
        return loaded;
    }

    public static bool TryLoadConfiguration(
        string path,
        out ControllerMappingConfiguration configuration,
        out string error)
    {
        configuration = ControllerMappingConfiguration.CreateDefault();
        error = "";
        if (!File.Exists(path))
        {
            return true;
        }

        try
        {
            configuration = DeserializeConfiguration(File.ReadAllText(path));
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
        var defaults = ControllerMappingConfiguration.CreateDefault();
        Save(path, defaults.ReplaceProfile(defaults.ActiveProfileId, defaults.ActiveProfile.Name, mapping));
    }

    public static void Save(string path, ControllerMappingConfiguration configuration)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = path + ".tmp";
        File.WriteAllText(tempPath, Serialize(configuration));
        File.Move(tempPath, path, overwrite: true);
    }

    internal static string Serialize(ControllerButtonMapping mapping)
    {
        var defaults = ControllerMappingConfiguration.CreateDefault();
        return Serialize(defaults.ReplaceProfile(
            defaults.ActiveProfileId,
            defaults.ActiveProfile.Name,
            mapping));
    }

    internal static string Serialize(ControllerMappingConfiguration configuration)
    {
        var profiles = configuration.Profiles
            .Select(profile => new MappingProfileDocument(
                profile.Id,
                profile.Name,
                SerializeButtons(profile.Mapping)))
            .ToArray();
        return JsonSerializer.Serialize(
            new MappingDocument(SchemaVersion, configuration.ActiveProfileId, profiles, null),
            JsonOptions);
    }

    internal static ControllerButtonMapping Deserialize(string json)
    {
        return DeserializeConfiguration(json).ActiveMapping;
    }

    internal static ControllerMappingConfiguration DeserializeConfiguration(string json)
    {
        var document = JsonSerializer.Deserialize<MappingDocument>(json) ??
                       throw new InvalidDataException("The document is empty.");

        if (document.Schema == 1)
        {
            var migrated = DeserializeButtons(document.Buttons);
            var profile = new ControllerMappingProfile("migrated", "Personalizzato", migrated);
            return new ControllerMappingConfiguration([profile], profile.Id);
        }
        if (document.Schema != SchemaVersion)
        {
            throw new InvalidDataException($"Unsupported schema {document.Schema}.");
        }

        var profiles = (document.Profiles ?? Array.Empty<MappingProfileDocument>())
            .Select(profile =>
            {
                if (string.IsNullOrWhiteSpace(profile.Id))
                {
                    throw new InvalidDataException("A mapping profile has no ID.");
                }
                if (string.IsNullOrWhiteSpace(profile.Name))
                {
                    throw new InvalidDataException($"Mapping profile '{profile.Id}' has no name.");
                }
                return new ControllerMappingProfile(
                    profile.Id.Trim(),
                    profile.Name.Trim(),
                    DeserializeButtons(profile.Buttons));
            })
            .ToArray();
        if (profiles.Length == 0)
        {
            throw new InvalidDataException("The mapping document contains no profiles.");
        }
        if (string.IsNullOrWhiteSpace(document.ActiveProfile))
        {
            throw new InvalidDataException("The mapping document has no active profile.");
        }
        return new ControllerMappingConfiguration(profiles, document.ActiveProfile);
    }

    private static Dictionary<string, string> SerializeButtons(ControllerButtonMapping mapping)
    {
        var buttons = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var input in ControllerButtonCatalog.Inputs)
        {
            buttons[input.Id.ToString()] = mapping[input.Id].ToString();
        }
        return buttons;
    }

    private static ControllerButtonMapping DeserializeButtons(Dictionary<string, string>? buttons)
    {
        var mapping = ControllerButtonMapping.CreateDefault();
        foreach (var pair in buttons ?? new Dictionary<string, string>())
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

        var exclusive = defaults.AssignOutput(XboxOutputButton.A, ControllerInputButton.Capture);
        if (exclusive[ControllerInputButton.A] != XboxOutputButton.None ||
            exclusive[ControllerInputButton.Capture] != XboxOutputButton.A ||
            exclusive.Validate().DuplicateOutputs.Count != 0)
        {
            throw new InvalidOperationException("Exclusive output assignment self-test failed.");
        }

        const string legacyJson =
            """
            {
              "Schema": 1,
              "Buttons": {
                "A": "B",
                "B": "A"
              }
            }
            """;
        var migrated = DeserializeConfiguration(legacyJson);
        if (migrated.ActiveMapping[ControllerInputButton.A] != XboxOutputButton.B ||
            migrated.ActiveMapping[ControllerInputButton.B] != XboxOutputButton.A)
        {
            throw new InvalidOperationException("Legacy mapping migration self-test failed.");
        }

        var profiles = ControllerMappingConfiguration.CreateDefault()
            .AddProfile("Arcade", customized);
        var profileRoundTrip = DeserializeConfiguration(Serialize(profiles));
        if (profileRoundTrip.Profiles.Count != 2 ||
            profileRoundTrip.ActiveProfile.Name != "Arcade" ||
            profileRoundTrip.ActiveMapping.Fingerprint() != customized.Fingerprint())
        {
            throw new InvalidOperationException("Mapping profile serialization self-test failed.");
        }

        var tempDirectory = Path.Combine(Path.GetTempPath(), "StadiaX-MappingSelfTest-" + Guid.NewGuid().ToString("N"));
        var tempPath = Path.Combine(tempDirectory, "controller_mapping.json");
        try
        {
            Save(tempPath, profiles);
            var provider = new ControllerButtonMappingProvider(tempPath);
            if (provider.GetCurrent().Fingerprint() != customized.Fingerprint())
            {
                throw new InvalidOperationException("Active mapping profile provider self-test failed.");
            }

            var defaultProfile = profiles.Profiles[0];
            Thread.Sleep(20);
            Save(tempPath, profiles.WithActiveProfile(defaultProfile.Id));
            Thread.Sleep(ControllerButtonMappingProvider.RefreshDelay + TimeSpan.FromMilliseconds(50));
            if (provider.GetCurrent().Fingerprint() != defaultProfile.Mapping.Fingerprint())
            {
                throw new InvalidOperationException("Mapping profile hot-reload self-test failed.");
            }
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }

    private sealed record MappingDocument(
        int Schema,
        string? ActiveProfile,
        MappingProfileDocument[]? Profiles,
        Dictionary<string, string>? Buttons);

    private sealed record MappingProfileDocument(
        string Id,
        string Name,
        Dictionary<string, string>? Buttons);
}

internal sealed class ControllerButtonMappingProvider
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMilliseconds(500);
    internal static TimeSpan RefreshDelay => RefreshInterval;

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
