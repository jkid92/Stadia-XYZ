using System.Text.RegularExpressions;

namespace StadiaX.ControlCenter;

internal sealed record ConnectionPhaseSnapshot(
    DateTimeOffset Timestamp,
    string Flow,
    int Step,
    int Total,
    string Phase,
    string State,
    string Detail)
{
    public int ProgressPercent
    {
        get
        {
            if (State.Equals("FAIL", StringComparison.OrdinalIgnoreCase) ||
                State.Equals("TIMEOUT", StringComparison.OrdinalIgnoreCase))
            {
                return 100;
            }

            var phaseCompleted = State.Equals("OK", StringComparison.OrdinalIgnoreCase) ||
                                 State.Equals("WARN", StringComparison.OrdinalIgnoreCase);
            var completedSteps = phaseCompleted ? Step : Step - 1;
            return Math.Clamp((int)Math.Round(completedSteps * 100d / Math.Max(1, Total)), 3, 100);
        }
    }

    public bool IsTerminal =>
        State.Equals("FAIL", StringComparison.OrdinalIgnoreCase) ||
        State.Equals("TIMEOUT", StringComparison.OrdinalIgnoreCase) ||
        Step >= Total && (State.Equals("OK", StringComparison.OrdinalIgnoreCase) ||
                          State.Equals("WARN", StringComparison.OrdinalIgnoreCase));

    public bool IsTimeout =>
        State.Equals("TIMEOUT", StringComparison.OrdinalIgnoreCase) ||
        Detail.Contains("timed out", StringComparison.OrdinalIgnoreCase) ||
        Detail.Contains("timeout", StringComparison.OrdinalIgnoreCase);
}

internal static partial class ConnectionPhaseParser
{
    [GeneratedRegex(
        @"STATUS:PHASE\|flow=(?<flow>.*?) step=(?<step>\d+)/(?<total>\d+) phase=(?<phase>.*?) state=(?<state>\S+) detail=(?<detail>.*)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PhaseLineRegex();

    public static bool TryParseLatest(string text, string expectedFlow, out ConnectionPhaseSnapshot? phase)
    {
        phase = null;
        foreach (var line in text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries).Reverse())
        {
            var match = PhaseLineRegex().Match(line);
            if (!match.Success || !match.Groups["flow"].Value.Equals(expectedFlow, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!int.TryParse(match.Groups["step"].Value, out var step) ||
                !int.TryParse(match.Groups["total"].Value, out var total))
            {
                continue;
            }

            var timestamp = DateTimeOffset.MinValue;
            var closingBracket = line.IndexOf(']');
            if (line.StartsWith("[", StringComparison.Ordinal) && closingBracket > 1)
            {
                _ = DateTimeOffset.TryParse(line[1..closingBracket], out timestamp);
            }

            total = Math.Max(1, total);
            phase = new ConnectionPhaseSnapshot(
                timestamp,
                match.Groups["flow"].Value.Trim(),
                Math.Clamp(step, 1, total),
                total,
                match.Groups["phase"].Value.Trim(),
                match.Groups["state"].Value.Trim().ToUpperInvariant(),
                match.Groups["detail"].Value.Trim());
            return true;
        }

        return false;
    }
}

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
