namespace StadiaX.ControlCenter;

internal static class LogReader
{
    public static string Tail(string path, int maxLines)
    {
        if (maxLines <= 0)
        {
            return "";
        }

        if (!File.Exists(path))
        {
            return $"Not created yet: {path}";
        }

        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 4096,
                FileOptions.SequentialScan);
            using var reader = new StreamReader(stream);
            var lines = new Queue<string>(maxLines);

            while (reader.ReadLine() is { } line)
            {
                if (lines.Count == maxLines)
                {
                    lines.Dequeue();
                }

                lines.Enqueue(line);
            }

            return string.Join(Environment.NewLine, lines);
        }
        catch (IOException ex)
        {
            return $"Log temporarily unavailable: {path}{Environment.NewLine}{ex.Message}";
        }
        catch (UnauthorizedAccessException ex)
        {
            return $"Log temporarily unavailable: {path}{Environment.NewLine}{ex.Message}";
        }
    }
}
