namespace StadiaX.ControlCenter;

internal static class LogReader
{
    public static string Tail(string path, int maxLines)
    {
        if (!File.Exists(path))
        {
            return $"Not created yet: {path}";
        }

        var lines = File.ReadLines(path).TakeLast(maxLines);
        return string.Join(Environment.NewLine, lines);
    }
}
