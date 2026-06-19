namespace StadiaX.ControlCenter;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
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
        if (args.Contains("--smoke-test", StringComparer.OrdinalIgnoreCase))
        {
            ApplicationConfiguration.Initialize();
            using var form = new MainForm(AppPaths.Discover());
            Environment.Exit(0);
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm(AppPaths.Discover()));
    }

    private static int RunBridgeCommand(bool start)
    {
        var paths = AppPaths.Discover();
        var runner = new ProcessRunner();
        var orchestrator = new BridgeOrchestrator(paths, runner);
        return (start ? orchestrator.StartAsync() : orchestrator.StopAsync()).GetAwaiter().GetResult();
    }
}
