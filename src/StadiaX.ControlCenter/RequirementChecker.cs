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

        foreach (var runtime in new[] { "StadiaX.exe", "ViGEmClient.dll" })
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
            Path.Combine(_paths.Root, "dependencies", "ViGEmBus_1.22.0_x64_x86_arm64.exe")
        };
        checks.Add(new CheckResult(
            "Bundled driver setup",
            bundledDependencies.All(File.Exists) ? CheckState.Ok : CheckState.Warn,
            bundledDependencies.All(File.Exists)
                ? "Signed HidHide and ViGEmBus setup files are available locally."
                : "The installed setup should include signed HidHide and ViGEmBus installers; winget is only a development fallback."));

        var vigem = await _runner.RunAsync(
            "powershell.exe",
            "-NoProfile -ExecutionPolicy Bypass -Command \"if (Get-Service -Name ViGEmBus -ErrorAction SilentlyContinue) { 'OK' }\"",
            _paths.Root,
            15000).ConfigureAwait(false);
        checks.Add(new CheckResult(
            "ViGEmBus driver",
            vigem.Output.Contains("OK", StringComparison.OrdinalIgnoreCase) ? CheckState.Ok : CheckState.Warn,
            "Required for virtual Xbox 360 controllers; Start installs the bundled signed setup automatically."));

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
