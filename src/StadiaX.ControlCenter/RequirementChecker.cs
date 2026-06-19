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
        foreach (var file in new[]
        {
            "Start-Stadia.bat",
            "Stop-Stadia.bat",
            "StadiaX-GUI.ps1",
            "Test-StadiaX.ps1",
            "Resolve-WslDistro.ps1",
            "start.sh",
            "stadia_buttons.ini"
        })
        {
            var path = Path.Combine(_paths.Root, file);
            checks.Add(new CheckResult($"File: {file}", File.Exists(path) ? CheckState.Ok : CheckState.Missing, path));
        }

        foreach (var runtime in new[] { "StadiaX.exe", "stadia_receiver.exe", "ViGEmClient.dll", "stadia_bridge" })
        {
            var path = Path.Combine(_paths.Root, runtime);
            checks.Add(new CheckResult($"Runtime: {runtime}", File.Exists(path) ? CheckState.Ok : CheckState.Warn, File.Exists(path) ? path : "Missing in source checkout; release package should include it."));
        }

        checks.Add(new CheckResult("Command: usbipd", await _runner.CommandExistsAsync("usbipd", _paths.Root) ? CheckState.Ok : CheckState.Missing, "Required for Bluetooth USB/IP handoff."));
        checks.Add(new CheckResult("Command: wsl", await _runner.CommandExistsAsync("wsl", _paths.Root) ? CheckState.Ok : CheckState.Missing, "Required for the Linux bridge."));

        var vigem = await _runner.RunAsync(
            "powershell.exe",
            "-NoProfile -ExecutionPolicy Bypass -Command \"if (Get-Service -Name ViGEmBus -ErrorAction SilentlyContinue) { 'OK' }\"",
            _paths.Root,
            15000).ConfigureAwait(false);
        checks.Add(new CheckResult("ViGEmBus driver", vigem.Output.Contains("OK", StringComparison.OrdinalIgnoreCase) ? CheckState.Ok : CheckState.Missing, "Required for virtual Xbox 360 controllers."));

        if (File.Exists(_paths.WslResolverScript))
        {
            var distro = await _runner.RunAsync(
                "powershell.exe",
                $"-NoProfile -ExecutionPolicy Bypass -File \"{_paths.WslResolverScript}\"",
                _paths.Root,
                15000).ConfigureAwait(false);
            var resolved = distro.Output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            checks.Add(new CheckResult("WSL distro", string.IsNullOrWhiteSpace(resolved) ? CheckState.Warn : CheckState.Ok, string.IsNullOrWhiteSpace(resolved) ? "No distro resolved yet; first start can install Ubuntu." : resolved));
        }

        var macroPath = Path.Combine(_paths.Root, "stadia_buttons.ini");
        if (File.Exists(macroPath))
        {
            var macroText = File.ReadAllText(macroPath);
            checks.Add(new CheckResult("Macro config", macroText.Contains("[Buttons]", StringComparison.OrdinalIgnoreCase) ? CheckState.Ok : CheckState.Warn, "stadia_buttons.ini should contain a [Buttons] section."));
        }

        return checks;
    }
}
