using System.Diagnostics;
using System.Text;

namespace StadiaX.ControlCenter;

internal sealed record CommandResult(int ExitCode, string Output, string Error);

internal sealed class ProcessRunner
{
    public async Task<CommandResult> RunAsync(
        string fileName,
        string arguments,
        string workingDirectory,
        int timeoutMilliseconds = 30000,
        CancellationToken cancellationToken = default)
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

        return await RunProcessAsync(startInfo, timeoutMilliseconds, cancellationToken).ConfigureAwait(false);
    }

    public async Task<CommandResult> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        string workingDirectory,
        int timeoutMilliseconds = 30000,
        IReadOnlyDictionary<string, string>? environment = null,
        bool createNoWindow = true,
        CancellationToken cancellationToken = default)
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

        return await RunProcessAsync(startInfo, timeoutMilliseconds, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<CommandResult> RunProcessAsync(
        ProcessStartInfo startInfo,
        int timeoutMilliseconds,
        CancellationToken cancellationToken)
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
        var outputLock = new object();
        var errorLock = new object();
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                lock (outputLock) output.AppendLine(e.Data);
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                lock (errorLock) error.AppendLine(e.Data);
            }
        };

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
        using var waitCancellation = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, cancellationToken);
        try
        {
            await process.WaitForExitAsync(waitCancellation.Token).ConfigureAwait(false);
            process.WaitForExit();
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            var cleanupObservedExit = false;
            try
            {
                using var cleanupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await process.WaitForExitAsync(cleanupTimeout.Token).ConfigureAwait(false);
                process.WaitForExit();
                cleanupObservedExit = true;
            }
            catch
            {
                try { process.CancelOutputRead(); } catch { }
                try { process.CancelErrorRead(); } catch { }
            }
            var outputText = Snapshot(output, outputLock);
            var errorText = Snapshot(error, errorLock);
            var cancelledByCaller = cancellationToken.IsCancellationRequested;
            AppDiagnosticsLogger.Record(
                cancelledByCaller ? "PROCESS_CANCELLED" : "PROCESS_TIMEOUT",
                ("id", commandId),
                ("file", startInfo.FileName),
                ("elapsedMs", startedAt.ElapsedMilliseconds.ToString()),
                ("cleanupObservedExit", cleanupObservedExit.ToString()),
                ("outputBytes", outputText.Length.ToString()),
                ("errorBytes", errorText.Length.ToString()));
            return cancelledByCaller
                ? new CommandResult(-2, outputText, "Cancelled by stop request.")
                : new CommandResult(-1, outputText, $"Timed out after {timeoutMilliseconds} ms.");
        }

        var result = new CommandResult(process.ExitCode, Snapshot(output, outputLock), Snapshot(error, errorLock));
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

    private static string Snapshot(StringBuilder builder, object sync)
    {
        lock (sync)
        {
            return builder.ToString();
        }
    }

    public async Task<bool> CommandExistsAsync(
        string commandName,
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        var result = await RunAsync("where.exe", commandName, workingDirectory, 10000, cancellationToken).ConfigureAwait(false);
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
