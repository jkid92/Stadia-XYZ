namespace StadiaX.ControlCenter;

internal sealed class StatusWriter
{
    private static readonly object Sync = new();

    private readonly AppPaths _paths;
    private readonly string _logPath;

    public StatusWriter(AppPaths paths, string logFileName)
    {
        _paths = paths;
        _logPath = Path.Combine(paths.LogDirectory, logFileName);
    }

    public void Reset(string code, string message)
    {
        var line = BuildLine(code, message);
        lock (Sync)
        {
            WriteLineSafely(_logPath, line, append: false);
            if (!_logPath.Equals(_paths.StatusLog, StringComparison.OrdinalIgnoreCase))
            {
                WriteLineSafely(_paths.StatusLog, line, append: false);
            }
        }
    }

    public void Write(string code, string message)
    {
        var line = BuildLine(code, message);
        lock (Sync)
        {
            WriteLineSafely(_logPath, line, append: true);
            if (!_logPath.Equals(_paths.StatusLog, StringComparison.OrdinalIgnoreCase))
            {
                WriteLineSafely(_paths.StatusLog, line, append: true);
            }
        }
    }

    private static void WriteLineSafely(string path, string line, bool append)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if (append)
            {
                File.AppendAllText(path, line);
            }
            else
            {
                File.WriteAllText(path, line);
            }
        }
        catch (Exception ex)
        {
            AppDiagnosticsLogger.Record(
                "STATUS_LOG_WRITE_WARN",
                ("file", Path.GetFileName(path)),
                ("mode", append ? "append" : "reset"),
                ("error", ex.Message));
        }
    }

    public void WritePhase(string flow, int step, int total, string phase, string state, string message)
    {
        var safeTotal = Math.Max(1, total);
        var safeStep = Math.Clamp(step, 1, safeTotal);
        Write(
            "PHASE",
            $"flow={Sanitize(flow)} step={safeStep}/{safeTotal} phase={Sanitize(phase)} state={Sanitize(state)} detail={Sanitize(message)}");
        AppDiagnosticsLogger.Record(
            "CONNECTION_PHASE",
            ("flow", flow),
            ("step", $"{safeStep}/{safeTotal}"),
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
