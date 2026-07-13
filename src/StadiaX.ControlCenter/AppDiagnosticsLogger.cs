using System.Text;

namespace StadiaX.ControlCenter;

internal static class AppDiagnosticsLogger
{
    private static readonly object Sync = new();
    private static readonly string SessionId = Guid.NewGuid().ToString("N")[..8];
    private static string? _path;

    public static string CurrentSessionId => SessionId;

    public static void Initialize(AppPaths paths)
    {
        lock (Sync)
        {
            if (string.Equals(_path, paths.AppDiagnosticsLog, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _path = paths.AppDiagnosticsLog;
        }

        Record("LOGGER_INIT", ("root", paths.Root), ("version", paths.Version));
    }

    public static void Record(string action, params (string Key, string? Value)[] details)
    {
        var path = _path;
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var line = new StringBuilder()
                .Append(DateTimeOffset.Now.ToString("O"))
                .Append(" | ")
                .Append(Sanitize(action))
                .Append(" | session=")
                .Append(SessionId)
                .Append(" | pid=")
                .Append(Environment.ProcessId);

            foreach (var detail in details)
            {
                if (string.IsNullOrWhiteSpace(detail.Value))
                {
                    continue;
                }

                line.Append(" | ")
                    .Append(Sanitize(detail.Key))
                    .Append('=')
                    .Append(Sanitize(detail.Value));
            }

            lock (Sync)
            {
                File.AppendAllText(path, line.AppendLine().ToString(), Encoding.UTF8);
            }
        }
        catch
        {
            // Diagnostics must not disturb controller input or UI actions.
        }
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
