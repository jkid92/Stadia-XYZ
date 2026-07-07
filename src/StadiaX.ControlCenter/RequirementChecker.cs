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

        checks.Add(new CheckResult(
            "Command: winget",
            await _runner.CommandExistsAsync("winget", _paths.Root) ? CheckState.Ok : CheckState.Warn,
            "Used to install HidHide and ViGEmBus automatically when they are missing."));

        var vigem = await _runner.RunAsync(
            "powershell.exe",
            "-NoProfile -ExecutionPolicy Bypass -Command \"if (Get-Service -Name ViGEmBus -ErrorAction SilentlyContinue) { 'OK' }\"",
            _paths.Root,
            15000).ConfigureAwait(false);
        checks.Add(new CheckResult(
            "ViGEmBus driver",
            vigem.Output.Contains("OK", StringComparison.OrdinalIgnoreCase) ? CheckState.Ok : CheckState.Warn,
            "Required for virtual Xbox 360 controllers; Start native can install it when winget is available."));

        var hidHidePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "Nefarius Software Solutions",
            "HidHide",
            "x64",
            "HidHideCLI.exe");
        checks.Add(new CheckResult(
            "HidHide driver",
            File.Exists(hidHidePath) ? CheckState.Ok : CheckState.Warn,
            File.Exists(hidHidePath) ? hidHidePath : "Required to hide physical controller input and prevent duplicated buttons; Start native can install it when winget is available."));

        return checks;
    }
}
