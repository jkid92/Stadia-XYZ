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
        UserActionLog = Path.Combine(LogDirectory, "user-actions.log");
        AppDiagnosticsLog = Path.Combine(LogDirectory, "app-diagnostics.log");
        ControllerState = Path.Combine(LogDirectory, "controller-state.json");
        BluetoothDiagnostics = Path.Combine(LogDirectory, "bluetooth-diagnostics.txt");
        ReceiverLog = Path.Combine(LogDirectory, "receiver.log");
        SelectedBluetoothBusId = Path.Combine(root, "selected_bt_busid.txt");
        SelectedControllerMacs = Path.Combine(root, "selected_controller_macs.txt");
        SelectedWslDistro = Path.Combine(root, "selected_wsl_distro.txt");
        ControllerProfiles = Path.Combine(root, "controller_profiles.json");
        SupportBundleDirectory = Path.Combine(root, "support-bundles");
        MacroConfig = Path.Combine(root, "stadia_buttons.ini");
        VersionFile = Path.Combine(root, "VERSION.txt");
        AppExecutable = Path.Combine(root, "StadiaX.exe");
        StartScript = Path.Combine(root, "Start-Stadia.bat");
        StopScript = Path.Combine(root, "Stop-Stadia.bat");
        PowerShellGuiScript = Path.Combine(root, "StadiaX-GUI.ps1");
        SelfTestScript = Path.Combine(root, "Test-StadiaX.ps1");
        WslResolverScript = Path.Combine(root, "Resolve-WslDistro.ps1");
    }

    public string Root { get; }
    public string LogDirectory { get; }
    public string StatusLog { get; }
    public string LinuxStatusLog { get; }
    public string LinuxLog { get; }
    public string UserActionLog { get; }
    public string AppDiagnosticsLog { get; }
    public string ControllerState { get; }
    public string BluetoothDiagnostics { get; }
    public string ReceiverLog { get; }
    public string SelectedBluetoothBusId { get; }
    public string SelectedControllerMacs { get; }
    public string SelectedWslDistro { get; }
    public string ControllerProfiles { get; }
    public string SupportBundleDirectory { get; }
    public string MacroConfig { get; }
    public string VersionFile { get; }
    public string AppExecutable { get; }
    public string StartScript { get; }
    public string StopScript { get; }
    public string PowerShellGuiScript { get; }
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

    public IReadOnlyList<string> ResolveAssetCandidates(string fileName)
    {
        fileName = Path.GetFileName(fileName);
        var candidates = new List<string>();

        AddAssetCandidate(candidates, Root, fileName);
        AddAssetCandidate(candidates, AppContext.BaseDirectory, fileName);
        AddAssetCandidate(candidates, Path.GetDirectoryName(Environment.ProcessPath), fileName);
        AddAssetCandidate(candidates, Directory.GetCurrentDirectory(), fileName);

        var current = new DirectoryInfo(AppContext.BaseDirectory);
        for (var depth = 0; depth < 8 && current is not null; depth++)
        {
            AddAssetCandidate(candidates, current.FullName, fileName);
            current = current.Parent;
        }

        return candidates
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public string ResolveAssetPath(string fileName)
    {
        return ResolveAssetCandidates(fileName).FirstOrDefault(File.Exists) ??
               Path.Combine(Root, "assets", Path.GetFileName(fileName));
    }

    private static void AddAssetCandidate(List<string> candidates, string? root, string fileName)
    {
        if (string.IsNullOrWhiteSpace(root))
        {
            return;
        }

        candidates.Add(Path.Combine(root, "assets", fileName));
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
