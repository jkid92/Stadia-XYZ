namespace StadiaX.ControlCenter;

internal enum CheckState
{
    Ok,
    Info,
    Warn,
    Missing
}

internal sealed record CheckResult(string Name, CheckState State, string Details);

internal sealed class RequirementChecker
{
    private readonly AppPaths _paths;
    private readonly ProcessRunner _runner;

    public RequirementChecker(AppPaths paths, ProcessRunner runner)
    {
        _paths = paths;
        _runner = runner;
    }

    public async Task<IReadOnlyList<CheckResult>> RunAsync()
    {
        var checks = new List<CheckResult>();

        foreach (var runtime in new[]
                 {
                     "StadiaX.exe",
                     ViiperRuntime.BinaryRelativePath
                 })
        {
            var path = Path.Combine(_paths.Root, runtime);
            checks.Add(new CheckResult(
                $"Runtime: {runtime}",
                File.Exists(path) ? CheckState.Ok : CheckState.Warn,
                File.Exists(path) ? path : "Missing in this folder; the Windows Native installer should include it."));
        }

        var bundledDependencies = new[]
        {
            Path.Combine(_paths.Root, "dependencies", "HidHide_1.5.230_x64.exe"),
            Path.Combine(_paths.Root, "dependencies", "USBip-0.9.7.8-x64.exe")
        };
        checks.Add(new CheckResult(
            "Bundled driver setup",
            bundledDependencies.All(File.Exists) ? CheckState.Ok : CheckState.Warn,
            bundledDependencies.All(File.Exists)
                ? "Signed HidHide and usbip-win2 setup files are available locally."
                : "The installed setup should include signed HidHide and usbip-win2 installers."));

        var usbipVersion = ViiperRuntime.UsbipInstalledVersion;
        checks.Add(new CheckResult(
            "usbip-win2 driver",
            ViiperRuntime.IsUsbipDriverInstalled ? CheckState.Ok : CheckState.Warn,
            ViiperRuntime.IsUsbipDriverInstalled
                ? $"{ViiperRuntime.UsbipDriverPath} version {usbipVersion}"
                : "Required by VIIPER; Start installs the bundled signed setup automatically and requests one restart."));

        var viiperVersion = await ViiperRuntime.TryPingAsync().ConfigureAwait(false);
        checks.Add(new CheckResult(
            "VIIPER local server",
            viiperVersion is null ? CheckState.Info : CheckState.Ok,
            viiperVersion is null
                ? "Stopped as expected while no virtual controller session is active."
                : $"VIIPER {viiperVersion} is listening on {ViiperRuntime.Host}:{ViiperRuntime.ApiPort}."));

        var hidHidePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "Nefarius Software Solutions",
            "HidHide",
            "x64",
            "HidHideCLI.exe");
        checks.Add(new CheckResult(
            "HidHide driver",
            File.Exists(hidHidePath) ? CheckState.Ok : CheckState.Warn,
            File.Exists(hidHidePath) ? hidHidePath : "Required to prevent duplicated buttons; Start installs the bundled signed setup automatically."));

        return checks;
    }
}
