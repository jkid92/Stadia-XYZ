namespace StadiaX.ControlCenter;

internal sealed class StatusWriter
{
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
        File.WriteAllText(_logPath, $"[{DateTime.Now}] STATUS:{code}|{message}{Environment.NewLine}");
        File.WriteAllText(_paths.StatusLog, $"[{DateTime.Now}] STATUS:{code}|{message}{Environment.NewLine}");
    }

    public void Write(string code, string message)
    {
        Directory.CreateDirectory(_paths.LogDirectory);
        var line = $"[{DateTime.Now}] STATUS:{code}|{message}{Environment.NewLine}";
        File.AppendAllText(_logPath, line);
        File.AppendAllText(_paths.StatusLog, line);
    }
}
