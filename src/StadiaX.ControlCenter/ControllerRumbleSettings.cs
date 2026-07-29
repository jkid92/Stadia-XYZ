using System.Text.Json;

namespace StadiaX.ControlCenter;

internal static class ControllerRumbleSettingsStore
{
    public const int ControllerCount = 4;
    private static readonly object Sync = new();
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static bool IsEnabled(string path, int controllerNumber)
    {
        ValidateControllerNumber(controllerNumber);
        return Load(path)[controllerNumber - 1];
    }

    public static bool[] Load(string path)
    {
        lock (Sync)
        {
            return LoadCore(path);
        }
    }

    public static bool[] SetEnabled(string path, int controllerNumber, bool enabled)
    {
        ValidateControllerNumber(controllerNumber);
        lock (Sync)
        {
            var values = LoadCore(path);
            values[controllerNumber - 1] = enabled;
            SaveCore(path, values);
            return values;
        }
    }

    public static void RunSelfTest()
    {
        var directory = Path.Combine(Path.GetTempPath(), "StadiaX-RumbleSettings-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "rumble-settings.json");
        try
        {
            var defaults = Load(path);
            if (defaults.Length != ControllerCount || defaults.Any(enabled => !enabled))
            {
                throw new InvalidOperationException("Controller rumble default settings self-test failed.");
            }

            var changed = SetEnabled(path, 2, false);
            var reloaded = Load(path);
            if (changed[1] || reloaded[1] || reloaded.Where((_, index) => index != 1).Any(enabled => !enabled))
            {
                throw new InvalidOperationException("Controller rumble persistence self-test failed.");
            }

            File.WriteAllText(path, "{ invalid json");
            if (Load(path).Any(enabled => !enabled))
            {
                throw new InvalidOperationException("Controller rumble recovery self-test failed.");
            }
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static bool[] LoadCore(string path)
    {
        var defaults = CreateDefaults();
        if (!File.Exists(path))
        {
            return defaults;
        }

        try
        {
            var document = JsonSerializer.Deserialize<RumbleSettingsDocument>(File.ReadAllText(path));
            if (document?.Enabled is null)
            {
                return defaults;
            }

            for (var index = 0; index < Math.Min(ControllerCount, document.Enabled.Length); index++)
            {
                defaults[index] = document.Enabled[index];
            }
        }
        catch (Exception ex)
        {
            AppDiagnosticsLogger.Record(
                "RUMBLE_SETTINGS_READ_WARN",
                ("file", path),
                ("error", ex.Message));
        }

        return defaults;
    }

    private static void SaveCore(string path, IReadOnlyList<bool> values)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var normalized = CreateDefaults();
        for (var index = 0; index < Math.Min(ControllerCount, values.Count); index++)
        {
            normalized[index] = values[index];
        }

        var temporaryPath = path + $".tmp.{Environment.ProcessId}";
        try
        {
            var document = new RumbleSettingsDocument(1, normalized);
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(document, JsonOptions));
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static bool[] CreateDefaults()
    {
        return Enumerable.Repeat(true, ControllerCount).ToArray();
    }

    private static void ValidateControllerNumber(int controllerNumber)
    {
        if (controllerNumber < 1 || controllerNumber > ControllerCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(controllerNumber),
                $"Controller number must be between 1 and {ControllerCount}.");
        }
    }

    private sealed record RumbleSettingsDocument(int Version, bool[] Enabled);
}

internal sealed class ControllerRumbleSettingsProvider
{
    private const int RefreshIntervalMs = 250;

    private readonly string _path;
    private readonly object _sync = new();
    private bool[] _enabled = Enumerable.Repeat(true, ControllerRumbleSettingsStore.ControllerCount).ToArray();
    private long _nextRefreshTick;

    public ControllerRumbleSettingsProvider(string path)
    {
        _path = path;
        Refresh();
    }

    public bool IsEnabled(int zeroBasedControllerIndex)
    {
        if (zeroBasedControllerIndex < 0 ||
            zeroBasedControllerIndex >= ControllerRumbleSettingsStore.ControllerCount)
        {
            return false;
        }

        var now = Environment.TickCount64;
        if (now >= Volatile.Read(ref _nextRefreshTick))
        {
            lock (_sync)
            {
                now = Environment.TickCount64;
                if (now >= _nextRefreshTick)
                {
                    Refresh();
                }
            }
        }

        return Volatile.Read(ref _enabled)[zeroBasedControllerIndex];
    }

    private void Refresh()
    {
        Volatile.Write(ref _enabled, ControllerRumbleSettingsStore.Load(_path));
        Volatile.Write(ref _nextRefreshTick, Environment.TickCount64 + RefreshIntervalMs);
    }
}
