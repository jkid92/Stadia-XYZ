namespace StadiaX.ControlCenter;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ConfigureDpiPreview(args);
        ConfigureLanguagePreview(args);
        if (args.Contains("--compact-ui", StringComparer.OrdinalIgnoreCase))
        {
            Environment.SetEnvironmentVariable("STADIAX_UI_DENSITY", "compact");
            Environment.SetEnvironmentVariable("STADIAX_UI_CONSTRAINED", "0");
        }
        if (args.Contains("--constrained-ui", StringComparer.OrdinalIgnoreCase))
        {
            Environment.SetEnvironmentVariable("STADIAX_UI_DENSITY", "compact");
            Environment.SetEnvironmentVariable("STADIAX_UI_CONSTRAINED", "1");
        }
        if (args.Contains("--comfortable-ui", StringComparer.OrdinalIgnoreCase) ||
            args.Contains("--classic-ui", StringComparer.OrdinalIgnoreCase))
        {
            Environment.SetEnvironmentVariable("STADIAX_UI_DENSITY", "comfortable");
            Environment.SetEnvironmentVariable("STADIAX_UI_CONSTRAINED", "0");
        }
        if (args.Contains("--demo-bluetooth", StringComparer.OrdinalIgnoreCase))
        {
            Environment.SetEnvironmentVariable("STADIAX_DEMO_BLUETOOTH", "1");
        }

        var paths = AppPaths.Discover();
        AppDiagnosticsLogger.Initialize(paths);

        if (args.Contains("--start-bridge", StringComparer.OrdinalIgnoreCase))
        {
            Environment.Exit(RunBridgeCommand(start: true));
            return;
        }
        if (args.Contains("--stop-bridge", StringComparer.OrdinalIgnoreCase))
        {
            Environment.Exit(RunBridgeCommand(start: false));
            return;
        }
        if (args.Contains("--start-windows-native", StringComparer.OrdinalIgnoreCase))
        {
            Environment.Exit(RunWindowsNativeCommand(paths, start: true));
            return;
        }
        if (args.Contains("--stop-windows-native", StringComparer.OrdinalIgnoreCase))
        {
            Environment.Exit(RunWindowsNativeCommand(paths, start: false));
            return;
        }
        if (args.Contains("--windows-native-probe", StringComparer.OrdinalIgnoreCase))
        {
            Environment.Exit(RunWindowsNativeProbe(paths));
            return;
        }
        if (args.Contains("--smoke-test", StringComparer.OrdinalIgnoreCase))
        {
            ApplicationConfiguration.Initialize();
            using var form = new MainForm(AppPaths.Discover());
            Environment.Exit(0);
            return;
        }
        if (args.Contains("--internal-self-test", StringComparer.OrdinalIgnoreCase))
        {
            UpdateService.RunSelfTest();
            WindowsNativeBatteryReader.RunSelfTest();
            Environment.Exit(0);
            return;
        }
        if (args.Contains("--ui-layout-test", StringComparer.OrdinalIgnoreCase))
        {
            ApplicationConfiguration.Initialize();
            Environment.Exit(UiLayoutAudit.Run(paths));
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm(paths));
    }

    private static void ConfigureDpiPreview(IEnumerable<string> args)
    {
        const string prefix = "--dpi-preview=";
        var value = args.FirstOrDefault(argument => argument.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        if (value is null || !int.TryParse(value[prefix.Length..], out var percent))
        {
            return;
        }
        Environment.SetEnvironmentVariable("STADIAX_UI_SCALE_PERCENT", Math.Clamp(percent, 100, 200).ToString());
    }

    private static void ConfigureLanguagePreview(IEnumerable<string> args)
    {
        const string prefix = "--language=";
        var value = args.FirstOrDefault(argument => argument.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        if (value is null)
        {
            return;
        }

        var language = value[prefix.Length..].Equals("it", StringComparison.OrdinalIgnoreCase) ? "it" : "en";
        Environment.SetEnvironmentVariable("STADIAX_UI_LANGUAGE", language);
    }

    private static int RunBridgeCommand(bool start)
    {
        var paths = AppPaths.Discover();
        AppDiagnosticsLogger.Initialize(paths);
        var runner = new ProcessRunner();
        var orchestrator = new BridgeOrchestrator(paths, runner);
        return (start ? orchestrator.StartAsync() : orchestrator.StopAsync()).GetAwaiter().GetResult();
    }

    private static int RunWindowsNativeCommand(AppPaths paths, bool start)
    {
        AppDiagnosticsLogger.Initialize(paths);
        var runner = new ProcessRunner();
        var orchestrator = new WindowsNativeOrchestrator(paths, runner);
        return (start ? orchestrator.StartAsync() : orchestrator.StopAsync()).GetAwaiter().GetResult();
    }

    private static int RunWindowsNativeProbe(AppPaths paths)
    {
        try
        {
            Directory.CreateDirectory(paths.LogDirectory);
            var runner = new ProcessRunner();
            var hidHide = new HidHideManager(paths, runner);
            var scanner = new WindowsNativeHidScanner(hidHide);
            var report = scanner.CreateProbeReportAsync(TimeSpan.FromSeconds(8)).GetAwaiter().GetResult();
            var reportPath = Path.Combine(paths.LogDirectory, "windows-native-probe.txt");
            File.WriteAllText(reportPath, report);
            return 0;
        }
        catch (Exception ex)
        {
            AppDiagnosticsLogger.Record("WINDOWS_NATIVE_PROBE_FAILED", ("error", ex.Message));
            return 1;
        }
    }
}
