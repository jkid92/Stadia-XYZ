using System.Text;

namespace StadiaX.ControlCenter;

internal sealed class UserActionLogger
{
    private readonly AppPaths _paths;
    private readonly object _sync = new();

    public UserActionLogger(AppPaths paths)
    {
        _paths = paths;
    }

    public void Record(string action, params (string Key, string? Value)[] details)
    {
        Directory.CreateDirectory(_paths.LogDirectory);
        var line = new StringBuilder()
            .Append(DateTimeOffset.Now.ToString("O"))
            .Append(" | ")
            .Append(Sanitize(action));

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

        lock (_sync)
        {
            File.AppendAllText(_paths.UserActionLog, line.AppendLine().ToString(), Encoding.UTF8);
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
