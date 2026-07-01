using System.Diagnostics;
using System.Text;

namespace StadiaX.ControlCenter;

internal sealed record CommandResult(int ExitCode, string Output, string Error);

internal sealed class ProcessRunner
{
    public async Task<CommandResult> RunAsync(string fileName, string arguments, string workingDirectory, int timeoutMilliseconds = 30000)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        return await RunProcessAsync(startInfo, timeoutMilliseconds).ConfigureAwait(false);
    }

    public async Task<CommandResult> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        string workingDirectory,
        int timeoutMilliseconds = 30000,
        IReadOnlyDictionary<string, string>? environment = null,
        bool createNoWindow = true)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = createNoWindow,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }
        if (environment is not null)
        {
            foreach (var pair in environment)
            {
                startInfo.Environment[pair.Key] = pair.Value;
            }
        }

        return await RunProcessAsync(startInfo, timeoutMilliseconds).ConfigureAwait(false);
    }

    private static async Task<CommandResult> RunProcessAsync(ProcessStartInfo startInfo, int timeoutMilliseconds)
    {
        var startedAt = Stopwatch.StartNew();
        var commandId = Guid.NewGuid().ToString("N")[..8];
        AppDiagnosticsLogger.Record(
            "PROCESS_START",
            ("id", commandId),
            ("file", startInfo.FileName),
            ("args", Shorten(ProcessArguments(startInfo), 700)),
            ("cwd", startInfo.WorkingDirectory),
            ("timeoutMs", timeoutMilliseconds.ToString()));

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        var output = new StringBuilder();
        var error = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) output.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) error.AppendLine(e.Data); };

        if (!TryStart(process, out var startError))
        {
            AppDiagnosticsLogger.Record(
                "PROCESS_START_FAILED",
                ("id", commandId),
                ("file", startInfo.FileName),
                ("elapsedMs", startedAt.ElapsedMilliseconds.ToString()),
                ("error", startError));
            return new CommandResult(-1, "", startError);
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeout = new CancellationTokenSource(timeoutMilliseconds);
        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            process.WaitForExit();
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            try
            {
                await process.WaitForExitAsync().ConfigureAwait(false);
                process.WaitForExit();
            }
            catch
            {
                // Preserve the original timeout result even if cleanup cannot observe process exit.
            }
            AppDiagnosticsLogger.Record(
                "PROCESS_TIMEOUT",
                ("id", commandId),
                ("file", startInfo.FileName),
                ("elapsedMs", startedAt.ElapsedMilliseconds.ToString()),
                ("outputBytes", output.Length.ToString()),
                ("errorBytes", error.Length.ToString()));
            return new CommandResult(-1, output.ToString(), $"Timed out after {timeoutMilliseconds} ms.");
        }

        var result = new CommandResult(process.ExitCode, output.ToString(), error.ToString());
        AppDiagnosticsLogger.Record(
            "PROCESS_EXIT",
            ("id", commandId),
            ("file", startInfo.FileName),
            ("exitCode", result.ExitCode.ToString()),
            ("elapsedMs", startedAt.ElapsedMilliseconds.ToString()),
            ("outputBytes", output.Length.ToString()),
            ("errorBytes", error.Length.ToString()),
            ("errorTail", Tail(result.Error, 180)));
        return result;
    }

    public async Task<bool> CommandExistsAsync(string commandName, string workingDirectory)
    {
        var result = await RunAsync("where.exe", commandName, workingDirectory, 10000).ConfigureAwait(false);
        return result.ExitCode == 0 && !string.IsNullOrWhiteSpace(result.Output);
    }

    private static bool TryStart(Process process, out string error)
    {
        try
        {
            if (process.Start())
            {
                error = "";
                return true;
            }

            error = "Process did not start.";
            return false;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static string ProcessArguments(ProcessStartInfo startInfo)
    {
        if (startInfo.ArgumentList.Count > 0)
        {
            return string.Join(" ", startInfo.ArgumentList.Select(argument => argument.Contains(' ') ? "\"" + argument + "\"" : argument));
        }

        return startInfo.Arguments;
    }

    private static string Tail(string value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        value = value.Trim();
        return value.Length <= maxLength ? value : value[^maxLength..];
    }

    private static string Shorten(string value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length <= maxLength)
        {
            return value;
        }

        return value[..maxLength] + "...";
    }
}
