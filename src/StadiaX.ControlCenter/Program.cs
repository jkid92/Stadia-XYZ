namespace StadiaX.ControlCenter;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Contains("--compact-ui", StringComparer.OrdinalIgnoreCase) ||
            args.Contains("--dpi-preview=100", StringComparer.OrdinalIgnoreCase))
        {
            Environment.SetEnvironmentVariable("STADIAX_UI_DENSITY", "compact");
        }
        if (args.Contains("--constrained-ui", StringComparer.OrdinalIgnoreCase) ||
            args.Contains("--dpi-preview=200", StringComparer.OrdinalIgnoreCase))
        {
            Environment.SetEnvironmentVariable("STADIAX_UI_DENSITY", "compact");
            Environment.SetEnvironmentVariable("STADIAX_UI_CONSTRAINED", "1");
        }
        if (args.Contains("--comfortable-ui", StringComparer.OrdinalIgnoreCase) ||
            args.Contains("--classic-ui", StringComparer.OrdinalIgnoreCase))
        {
            Environment.SetEnvironmentVariable("STADIAX_UI_DENSITY", "comfortable");
        }
        if (args.Contains("--demo-bluetooth", StringComparer.OrdinalIgnoreCase))
        {
            Environment.SetEnvironmentVariable("STADIAX_DEMO_BLUETOOTH", "1");
        }

        var paths = AppPaths.Discover();
        AppDiagnosticsLogger.Initialize(paths);

        if (args.Contains("--start-bridge", StringComparer.OrdinalIgnoreCase) ||
            args.Contains("--start", StringComparer.OrdinalIgnoreCase))
        {
            Environment.Exit(RunBridgeCommand(start: true));
            return;
        }
        if (args.Contains("--stop-bridge", StringComparer.OrdinalIgnoreCase) ||
            args.Contains("--stop", StringComparer.OrdinalIgnoreCase))
        {
            Environment.Exit(RunBridgeCommand(start: false));
            return;
        }
        if (args.Contains("--smoke-test", StringComparer.OrdinalIgnoreCase))
        {
            ApplicationConfiguration.Initialize();
            using var form = new MainForm(AppPaths.Discover());
            Environment.Exit(0);
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm(paths));
    }

    private static int RunBridgeCommand(bool start)
    {
        var paths = AppPaths.Discover();
        AppDiagnosticsLogger.Initialize(paths);
        var runner = new ProcessRunner();
        var orchestrator = new BridgeOrchestrator(paths, runner);
        return (start ? orchestrator.StartAsync() : orchestrator.StopAsync()).GetAwaiter().GetResult();
    }
}
