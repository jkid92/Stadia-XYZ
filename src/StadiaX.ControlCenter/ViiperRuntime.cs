using System.Diagnostics;
using Microsoft.Win32;
using Viiper.Client;

namespace StadiaX.ControlCenter;

internal sealed class ViiperServerLease : IDisposable
{
    private bool _disposed;

    internal ViiperServerLease(string version)
    {
        Version = version;
    }

    public string Version { get; }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        ViiperRuntime.Release();
    }
}

internal static class ViiperRuntime
{
    public const string Host = "127.0.0.1";
    public const int UsbPort = 32431;
    public const int ApiPort = 32432;
    public const string BinaryRelativePath = @"dependencies\VIIPER\viiper.exe";
    public const string UsbipDriverServiceName = "usbip2_ude";

    private static readonly SemaphoreSlim Gate = new(1, 1);

    private static Process? _ownedProcess;
    private static int _leaseCount;

    public static string UsbipDriverPath => ResolveUsbipDriverPath();

    public static bool IsUsbipDriverInstalled => File.Exists(UsbipDriverPath);

    public static string UsbipExecutablePath => new[]
        {
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "USBip",
                "usbip.exe"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "USBip",
                "usbip.exe")
        }
        .FirstOrDefault(File.Exists) ?? "";

    public static string? UsbipInstalledVersion
    {
        get
        {
            var executable = UsbipExecutablePath;
            if (string.IsNullOrWhiteSpace(executable))
            {
                return null;
            }

            return FileVersionInfo.GetVersionInfo(executable).FileVersion?.Trim();
        }
    }

    public static string ResolveBinaryPath(AppPaths paths)
    {
        var candidates = new[]
        {
            Path.Combine(paths.Root, BinaryRelativePath),
            Path.Combine(AppContext.BaseDirectory, BinaryRelativePath),
            Path.Combine(paths.Root, "viiper.exe"),
            Path.Combine(AppContext.BaseDirectory, "viiper.exe")
        };

        return candidates
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(File.Exists) ??
            Path.Combine(paths.Root, BinaryRelativePath);
    }

    public static async Task<ViiperServerLease> AcquireAsync(
        AppPaths paths,
        CancellationToken cancellationToken = default)
    {
        await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var version = await TryPingAsync(cancellationToken).ConfigureAwait(false);
            if (version is null)
            {
                var binary = ResolveBinaryPath(paths);
                if (!File.Exists(binary))
                {
                    throw new FileNotFoundException(
                        "VIIPER runtime is missing. Reinstall or repair Stadia X.",
                        binary);
                }

                Directory.CreateDirectory(paths.LogDirectory);
                StopExitedOwnedProcess();
                var logPath = Path.Combine(paths.LogDirectory, "viiper.log");
                var startInfo = new ProcessStartInfo
                {
                    FileName = binary,
                    WorkingDirectory = Path.GetDirectoryName(binary) ?? paths.Root,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                startInfo.ArgumentList.Add("server");
                startInfo.ArgumentList.Add($"--usb.addr={Host}:{UsbPort}");
                startInfo.ArgumentList.Add($"--api.addr={Host}:{ApiPort}");
                startInfo.ArgumentList.Add("--api.auto-attach-local-client=true");
                startInfo.ArgumentList.Add("--api.auto-attach-windows-native=false");
                startInfo.ArgumentList.Add("--api.require-local-host-auth=false");
                startInfo.ArgumentList.Add("--update-notify=none");
                startInfo.ArgumentList.Add("--log.level=warn");
                startInfo.ArgumentList.Add($"--log.file={logPath}");
                var usbipDirectory = Path.GetDirectoryName(UsbipExecutablePath);
                if (!string.IsNullOrWhiteSpace(usbipDirectory))
                {
                    startInfo.Environment["PATH"] =
                        usbipDirectory + Path.PathSeparator +
                        (startInfo.Environment.TryGetValue("PATH", out var currentPath)
                            ? currentPath
                            : Environment.GetEnvironmentVariable("PATH") ?? "");
                }

                _ownedProcess = Process.Start(startInfo) ??
                    throw new InvalidOperationException("VIIPER server process did not start.");
                AppDiagnosticsLogger.Record(
                    "VIIPER_SERVER_STARTED",
                    ("pid", _ownedProcess.Id.ToString()),
                    ("api", $"{Host}:{ApiPort}"),
                    ("usb", $"{Host}:{UsbPort}"));

                version = await WaitUntilReadyAsync(_ownedProcess, cancellationToken).ConfigureAwait(false);
            }

            _leaseCount++;
            return new ViiperServerLease(version);
        }
        finally
        {
            Gate.Release();
        }
    }

    public static async Task<string?> TryPingAsync(CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(800));
        try
        {
            using var client = new ViiperClient(Host, ApiPort);
            var response = await client.PingAsync(timeout.Token).ConfigureAwait(false);
            return response.Server.Equals("VIIPER", StringComparison.OrdinalIgnoreCase)
                ? response.Version
                : null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch
        {
            return null;
        }
    }

    internal static void Release()
    {
        Gate.Wait();
        try
        {
            _leaseCount = Math.Max(0, _leaseCount - 1);
            if (_leaseCount != 0 || _ownedProcess is null)
            {
                return;
            }

            try
            {
                if (!_ownedProcess.HasExited)
                {
                    _ownedProcess.Kill(entireProcessTree: true);
                    _ownedProcess.WaitForExit(3000);
                }
            }
            catch (Exception ex)
            {
                AppDiagnosticsLogger.Record(
                    "VIIPER_SERVER_STOP_WARN",
                    ("error", ex.Message));
            }
            finally
            {
                _ownedProcess.Dispose();
                _ownedProcess = null;
            }
        }
        finally
        {
            Gate.Release();
        }
    }

    private static async Task<string> WaitUntilReadyAsync(
        Process process,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (process.HasExited)
            {
                throw new InvalidOperationException(
                    $"VIIPER server stopped during startup with exit code {process.ExitCode}. See logs\\viiper.log.");
            }

            var version = await TryPingAsync(cancellationToken).ConfigureAwait(false);
            if (version is not null)
            {
                return version;
            }

            await Task.Delay(200, cancellationToken).ConfigureAwait(false);
        }

        throw new TimeoutException("VIIPER did not expose its local API within 10 seconds. See logs\\viiper.log.");
    }

    private static void StopExitedOwnedProcess()
    {
        if (_ownedProcess is null || !_ownedProcess.HasExited)
        {
            return;
        }

        _ownedProcess.Dispose();
        _ownedProcess = null;
    }

    private static string ResolveUsbipDriverPath()
    {
        var fallback = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "System32",
            "drivers",
            "usbip2_ude.sys");
        try
        {
            var imagePath = Registry.GetValue(
                $@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\{UsbipDriverServiceName}",
                "ImagePath",
                null) as string;
            if (string.IsNullOrWhiteSpace(imagePath))
            {
                return fallback;
            }

            var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            imagePath = Environment.ExpandEnvironmentVariables(imagePath.Trim().Trim('"'));
            if (imagePath.StartsWith(@"\SystemRoot\", StringComparison.OrdinalIgnoreCase))
            {
                imagePath = Path.Combine(windows, imagePath[@"\SystemRoot\".Length..]);
            }
            else if (imagePath.StartsWith(@"System32\", StringComparison.OrdinalIgnoreCase))
            {
                imagePath = Path.Combine(windows, imagePath);
            }

            return Path.GetFullPath(imagePath);
        }
        catch
        {
            return fallback;
        }
    }
}
