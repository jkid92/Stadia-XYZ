namespace StadiaX.ControlCenter;

internal sealed record WslDistro(string Name, string State, int Version, bool IsUbuntu);

internal sealed class WslDistroResolver
{
    private readonly AppPaths _paths;
    private readonly ProcessRunner _runner;

    public WslDistroResolver(AppPaths paths, ProcessRunner runner)
    {
        _paths = paths;
        _runner = runner;
    }

    public async Task<IReadOnlyList<WslDistro>> GetDistrosAsync()
    {
        var result = await _runner.RunAsync("wsl.exe", new[] { "-l", "-v" }, _paths.Root, 10000).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            return Array.Empty<WslDistro>();
        }

        var distros = new List<WslDistro>();
        foreach (var rawLine in result.Output.Replace("\0", "", StringComparison.Ordinal).Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith("NAME", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            if (line.StartsWith('*'))
            {
                line = line[1..].Trim();
            }

            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 3 && int.TryParse(parts[^1], out var version))
            {
                var state = parts[^2];
                var name = string.Join(" ", parts.Take(parts.Length - 2));
                distros.Add(new WslDistro(name, state, version, name.StartsWith("Ubuntu", StringComparison.OrdinalIgnoreCase)));
            }
        }

        return distros;
    }

    public async Task<string> ResolveAsync(string? requestedDistro = null)
    {
        var distros = await GetDistrosAsync().ConfigureAwait(false);
        if (distros.Count == 0)
        {
            return "";
        }

        foreach (var candidate in GetCandidates(requestedDistro))
        {
            var match = distros.FirstOrDefault(d => d.Name.Equals(candidate, StringComparison.Ordinal));
            if (match is not null)
            {
                return match.Name;
            }
        }

        return distros
            .OrderByDescending(d => d.IsUbuntu && d.Version == 2)
            .ThenByDescending(d => d.Version == 2)
            .ThenByDescending(d => d.IsUbuntu)
            .ThenBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .First()
            .Name;
    }

    private IEnumerable<string> GetCandidates(string? requestedDistro)
    {
        if (IsSafeDistroName(requestedDistro))
        {
            yield return requestedDistro!.Trim();
        }

        var selectedPath = Path.Combine(_paths.Root, "selected_wsl_distro.txt");
        if (File.Exists(selectedPath))
        {
            var saved = File.ReadAllText(selectedPath).Trim();
            if (IsSafeDistroName(saved))
            {
                yield return saved;
            }
        }
    }

    private static bool IsSafeDistroName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        return name.Trim().All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '.' or '-');
    }
}
