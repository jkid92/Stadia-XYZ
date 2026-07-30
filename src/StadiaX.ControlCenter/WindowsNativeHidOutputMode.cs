using System.Text.Json;

namespace StadiaX.ControlCenter;

internal enum WindowsNativeHidOutputMode
{
    Auto,
    HidSharpStream,
    Win32WriteFile,
    HidDSetOutputReport,
    HidDSetFeature
}

internal static class WindowsNativeHidOutputModeStore
{
    private static readonly object Sync = new();
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly WindowsNativeHidOutputMode[] Modes =
    [
        WindowsNativeHidOutputMode.Auto,
        WindowsNativeHidOutputMode.HidSharpStream,
        WindowsNativeHidOutputMode.Win32WriteFile,
        WindowsNativeHidOutputMode.HidDSetOutputReport,
        WindowsNativeHidOutputMode.HidDSetFeature
    ];

    public static WindowsNativeHidOutputMode Load(string path)
    {
        lock (Sync)
        {
            return LoadCore(path);
        }
    }

    public static void Save(string path, WindowsNativeHidOutputMode mode)
    {
        Validate(mode);
        lock (Sync)
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var temporaryPath = path + $".tmp.{Environment.ProcessId}";
            try
            {
                var document = new HidOutputModeDocument(1, mode.ToString());
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
    }

    public static WindowsNativeHidOutputMode Next(WindowsNativeHidOutputMode mode)
    {
        var index = Array.IndexOf(Modes, mode);
        return Modes[(index < 0 ? 0 : index + 1) % Modes.Length];
    }

    public static string ShortName(WindowsNativeHidOutputMode mode) => mode switch
    {
        WindowsNativeHidOutputMode.Auto => "Auto",
        WindowsNativeHidOutputMode.HidSharpStream => "Stream",
        WindowsNativeHidOutputMode.Win32WriteFile => "WriteFile",
        WindowsNativeHidOutputMode.HidDSetOutputReport => "Output",
        WindowsNativeHidOutputMode.HidDSetFeature => "Feature",
        _ => "Auto"
    };

    public static string TechnicalName(WindowsNativeHidOutputMode mode) => mode switch
    {
        WindowsNativeHidOutputMode.Auto => "Auto",
        WindowsNativeHidOutputMode.HidSharpStream => "HidSharpStream",
        WindowsNativeHidOutputMode.Win32WriteFile => "Win32WriteFile",
        WindowsNativeHidOutputMode.HidDSetOutputReport => "HidD_SetOutputReport",
        WindowsNativeHidOutputMode.HidDSetFeature => "HidD_SetFeature",
        _ => "Auto"
    };

    public static void RunSelfTest()
    {
        var directory = Path.Combine(Path.GetTempPath(), "StadiaX-HidOutputMode-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "hid-output-mode.json");
        try
        {
            if (Load(path) != WindowsNativeHidOutputMode.Auto)
            {
                throw new InvalidOperationException("Windows HID output mode default self-test failed.");
            }

            Save(path, WindowsNativeHidOutputMode.Win32WriteFile);
            if (Load(path) != WindowsNativeHidOutputMode.Win32WriteFile)
            {
                throw new InvalidOperationException("Windows HID output mode persistence self-test failed.");
            }

            var provider = new WindowsNativeHidOutputModeProvider(path);
            Save(path, WindowsNativeHidOutputMode.HidDSetOutputReport);
            Thread.Sleep(RefreshIntervalForSelfTestMs);
            if (provider.GetMode() != WindowsNativeHidOutputMode.HidDSetOutputReport)
            {
                throw new InvalidOperationException("Windows HID output mode hot-reload self-test failed.");
            }

            var mode = WindowsNativeHidOutputMode.Auto;
            foreach (var expected in Modes.Skip(1).Append(WindowsNativeHidOutputMode.Auto))
            {
                mode = Next(mode);
                if (mode != expected)
                {
                    throw new InvalidOperationException("Windows HID output mode cycle self-test failed.");
                }
            }

            File.WriteAllText(path, """{"Version":1,"Mode":"NotARealMode"}""");
            if (Load(path) != WindowsNativeHidOutputMode.Auto)
            {
                throw new InvalidOperationException("Windows HID output mode recovery self-test failed.");
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

    private static WindowsNativeHidOutputMode LoadCore(string path)
    {
        if (!File.Exists(path))
        {
            return WindowsNativeHidOutputMode.Auto;
        }

        try
        {
            var document = JsonSerializer.Deserialize<HidOutputModeDocument>(File.ReadAllText(path));
            if (document is not null &&
                Enum.TryParse<WindowsNativeHidOutputMode>(document.Mode, ignoreCase: true, out var mode) &&
                Enum.IsDefined(mode))
            {
                return mode;
            }
        }
        catch (Exception ex)
        {
            AppDiagnosticsLogger.Record(
                "WINDOWS_NATIVE_HID_OUTPUT_MODE_READ_WARN",
                ("file", path),
                ("error", ex.Message));
        }

        return WindowsNativeHidOutputMode.Auto;
    }

    private static void Validate(WindowsNativeHidOutputMode mode)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }
    }

    private const int RefreshIntervalForSelfTestMs = 240;

    private sealed record HidOutputModeDocument(int Version, string Mode);
}

internal sealed class WindowsNativeHidOutputModeProvider
{
    private const int RefreshIntervalMs = 200;

    private readonly string _path;
    private readonly object _sync = new();
    private WindowsNativeHidOutputMode _mode;
    private long _nextRefreshTick;

    public WindowsNativeHidOutputModeProvider(string path)
    {
        _path = path;
        Refresh();
    }

    public WindowsNativeHidOutputMode GetMode()
    {
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

        return _mode;
    }

    private void Refresh()
    {
        _mode = WindowsNativeHidOutputModeStore.Load(_path);
        Volatile.Write(ref _nextRefreshTick, Environment.TickCount64 + RefreshIntervalMs);
    }
}
