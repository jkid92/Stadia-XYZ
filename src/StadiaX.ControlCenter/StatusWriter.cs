namespace StadiaX.ControlCenter;

internal sealed class StatusWriter
{
    private static readonly object Sync = new();

    private readonly AppPaths _paths;
    private readonly string _logPath;

    public StatusWriter(AppPaths paths, string logFileName)
    {
        _paths = paths;
        Directory.CreateDirectory(paths.LogDirectory);
        _logPath = Path.Combine(paths.LogDirectory, logFileName);
    }

    public void Reset(string code, string message)
    {
        Directory.CreateDirectory(_paths.LogDirectory);
        var line = BuildLine(code, message);
        lock (Sync)
        {
            File.WriteAllText(_logPath, line);
            if (!_logPath.Equals(_paths.StatusLog, StringComparison.OrdinalIgnoreCase))
            {
                File.WriteAllText(_paths.StatusLog, line);
            }
        }
    }

    public void Write(string code, string message)
    {
        Directory.CreateDirectory(_paths.LogDirectory);
        var line = BuildLine(code, message);
        lock (Sync)
        {
            File.AppendAllText(_logPath, line);
            if (!_logPath.Equals(_paths.StatusLog, StringComparison.OrdinalIgnoreCase))
            {
                File.AppendAllText(_paths.StatusLog, line);
            }
        }
    }

    public void WritePhase(string flow, int step, int total, string phase, string state, string message)
    {
        Write(
            "PHASE",
            $"flow={Sanitize(flow)} step={Math.Clamp(step, 1, Math.Max(1, total))}/{Math.Max(1, total)} phase={Sanitize(phase)} state={Sanitize(state)} detail={Sanitize(message)}");
        AppDiagnosticsLogger.Record(
            "CONNECTION_PHASE",
            ("flow", flow),
            ("step", $"{Math.Clamp(step, 1, Math.Max(1, total))}/{Math.Max(1, total)}"),
            ("phase", phase),
            ("state", state),
            ("detail", message));
    }

    private static string BuildLine(string code, string message)
    {
        return $"[{DateTimeOffset.Now:O}] STATUS:{Sanitize(code)}|{Sanitize(message)}{Environment.NewLine}";
    }

    private static string Sanitize(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? "-"
            : value.Replace("\r", " ", StringComparison.Ordinal)
                .Replace("\n", " ", StringComparison.Ordinal)
                .Replace("|", "/", StringComparison.Ordinal)
                .Trim();
    }
}
