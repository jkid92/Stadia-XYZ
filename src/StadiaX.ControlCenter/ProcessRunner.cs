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

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        var output = new StringBuilder();
        var error = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) output.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) error.AppendLine(e.Data); };

        if (!process.Start())
        {
            return new CommandResult(-1, "", "Process did not start.");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeout = new CancellationTokenSource(timeoutMilliseconds);
        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            return new CommandResult(-1, output.ToString(), $"Timed out after {timeoutMilliseconds} ms.");
        }

        return new CommandResult(process.ExitCode, output.ToString(), error.ToString());
    }

    public async Task<bool> CommandExistsAsync(string commandName, string workingDirectory)
    {
        var result = await RunAsync("where.exe", commandName, workingDirectory, 10000).ConfigureAwait(false);
        return result.ExitCode == 0 && !string.IsNullOrWhiteSpace(result.Output);
    }
}
