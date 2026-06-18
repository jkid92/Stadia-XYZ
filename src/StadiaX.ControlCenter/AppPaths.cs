namespace StadiaX.ControlCenter;

internal sealed class AppPaths
{
    private AppPaths(string root)
    {
        Root = root;
        LogDirectory = Path.Combine(root, "logs");
        StatusLog = Path.Combine(LogDirectory, "status.log");
        LinuxStatusLog = Path.Combine(LogDirectory, "linux-status.log");
        LinuxLog = Path.Combine(LogDirectory, "linux.log");
        ControllerState = Path.Combine(LogDirectory, "controller-state.json");
        VersionFile = Path.Combine(root, "VERSION.txt");
        StartScript = Path.Combine(root, "Start-Stadia.bat");
        StopScript = Path.Combine(root, "Stop-Stadia.bat");
        PowerShellGuiLauncher = Path.Combine(root, "Start-GUI.bat");
        SelfTestScript = Path.Combine(root, "Test-StadiaX.ps1");
        WslResolverScript = Path.Combine(root, "Resolve-WslDistro.ps1");
    }

    public string Root { get; }
    public string LogDirectory { get; }
    public string StatusLog { get; }
    public string LinuxStatusLog { get; }
    public string LinuxLog { get; }
    public string ControllerState { get; }
    public string VersionFile { get; }
    public string StartScript { get; }
    public string StopScript { get; }
    public string PowerShellGuiLauncher { get; }
    public string SelfTestScript { get; }
    public string WslResolverScript { get; }

    public string Version
    {
        get
        {
            if (!File.Exists(VersionFile))
            {
                return "local";
            }

            var text = File.ReadAllText(VersionFile).Trim();
            return string.IsNullOrWhiteSpace(text) ? "local" : text;
        }
    }

    public static AppPaths Discover()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Start-Stadia.bat")) &&
                File.Exists(Path.Combine(current.FullName, "stadia_buttons.ini")))
            {
                return new AppPaths(current.FullName);
            }

            current = current.Parent;
        }

        return new AppPaths(AppContext.BaseDirectory);
    }
}
