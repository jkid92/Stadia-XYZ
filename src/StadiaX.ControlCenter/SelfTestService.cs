using System.Text.Json;

namespace StadiaX.ControlCenter;

internal sealed class SelfTestService
{
    private readonly AppPaths _paths;
    private readonly RequirementChecker _checker;

    public SelfTestService(AppPaths paths, RequirementChecker checker)
    {
        _paths = paths;
        _checker = checker;
    }

    public async Task<(string TextPath, string Text, int ExitCode)> RunAsync(bool json)
    {
        Directory.CreateDirectory(_paths.LogDirectory);
        var checks = await _checker.RunAsync().ConfigureAwait(false);
        var missing = checks.Count(check => check.State == CheckState.Missing);
        var warn = checks.Count(check => check.State == CheckState.Warn);
        var overall = missing > 0 ? "FAIL" : warn > 0 ? "WARN" : "OK";

        var lines = new List<string>
        {
            "Stadia X self-test",
            $"Created: {DateTimeOffset.Now:o}",
            $"Root: {_paths.Root}",
            $"Overall: {overall}",
            ""
        };
        lines.AddRange(checks.Select(check => $"{check.Name,-34} {check.State.ToString().ToUpperInvariant(),-8} {check.Details}"));

        var text = string.Join(Environment.NewLine, lines) + Environment.NewLine;
        var textPath = Path.Combine(_paths.LogDirectory, "self-test.txt");
        await File.WriteAllTextAsync(textPath, text).ConfigureAwait(false);

        if (json)
        {
            var jsonPath = Path.Combine(_paths.LogDirectory, "self-test.json");
            var payload = new
            {
                Created = DateTimeOffset.Now,
                Root = _paths.Root,
                Overall = overall,
                Results = checks
            };
            await File.WriteAllTextAsync(jsonPath, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true })).ConfigureAwait(false);
        }

        return (textPath, text, missing > 0 ? 1 : 0);
    }
}
