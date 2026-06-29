namespace StadiaX.ControlCenter;

internal static class LogReader
{
    private const int MinTailBytes = 64 * 1024;
    private const int MaxTailBytes = 1024 * 1024;
    private const int BytesPerLineHint = 256;

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
                FileOptions.RandomAccess);
            if (stream.Length == 0)
            {
                return "";
            }

            var targetBytes = Math.Clamp((long)maxLines * BytesPerLineHint, MinTailBytes, MaxTailBytes);
            var bytesToRead = Math.Min(stream.Length, targetBytes);
            var startsMidFile = stream.Length > bytesToRead;
            stream.Seek(-bytesToRead, SeekOrigin.End);

            using var reader = new StreamReader(stream);
            var text = reader.ReadToEnd();
            var lines = text.Replace("\r\n", "\n").Split('\n');
            var startIndex = startsMidFile && lines.Length > 0 ? 1 : 0;
            var usableCount = text.EndsWith('\n') ? lines.Length - 1 : lines.Length;

            return string.Join(Environment.NewLine, lines.Take(usableCount).Skip(startIndex).TakeLast(maxLines));
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
