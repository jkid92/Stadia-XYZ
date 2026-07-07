using System.Security.Principal;

namespace StadiaX.ControlCenter;

internal sealed class WindowsNativeOrchestrator
{
    private const int MaxControllers = 4;

    private readonly AppPaths _paths;
    private readonly ProcessRunner _runner;

    public WindowsNativeOrchestrator(AppPaths paths, ProcessRunner runner)
    {
        _paths = paths;
        _runner = runner;
    }

    public async Task<int> StartAsync()
    {
        var status = new StatusWriter(_paths, "windows-native.log");
        status.Reset("WINDOWS_NATIVE_START_REQUESTED", "Windows Native start requested");
        ClearStopSignal();

        var hidHide = new HidHideManager(_paths, _runner);
        if (!await EnsureHidHideAsync(hidHide, status).ConfigureAwait(false))
        {
            return 2;
        }

        if (!await EnsureVigemBusAsync(status).ConfigureAwait(false))
        {
            return 2;
        }

        var scanner = new WindowsNativeHidScanner(hidHide);
        status.Write("WINDOWS_NATIVE_SCAN_START", "Scanning Windows HID devices for Stadia controllers");
        var devices = (await scanner.FindStadiaControllersAsync().ConfigureAwait(false)).Take(MaxControllers).ToArray();
        if (devices.Length == 0)
        {
            status.Write("WINDOWS_NATIVE_NOT_READY", "No Stadia HID controller is visible to Windows");
            await WriteProbeAsync(scanner).ConfigureAwait(false);
            return 2;
        }

        var missingHidePath = devices.Where(device => string.IsNullOrWhiteSpace(device.DeviceInstancePath)).ToArray();
        if (missingHidePath.Length > 0)
        {
            status.Write("WINDOWS_NATIVE_NOT_READY", "Stadia HID was found, but HidHide did not expose a matching device path");
            await WriteProbeAsync(scanner).ConfigureAwait(false);
            return 2;
        }

        var appPath = ResolveCurrentAppPath();
        status.Write("WINDOWS_NATIVE_HIDHIDE_START", $"Registering {Path.GetFileName(appPath)} and hiding {devices.Length} physical Stadia HID device(s)");
        var hide = await hidHide.ConfigureStadiaDevicesAsync(
            appPath,
            devices.Select(device => device.DeviceInstancePath).ToArray(),
            elevated: !IsAdministrator()).ConfigureAwait(false);

        if (hide.ExitCode != 0)
        {
            status.Write("WINDOWS_NATIVE_HIDHIDE_FAILED", FirstNonEmpty(hide.Error, hide.Output, "HidHide configuration failed"));
            return 1;
        }

        status.Write("WINDOWS_NATIVE_HIDHIDE_OK", "Physical Stadia HID devices are hidden; HidHide cloak remains enabled");
        using var cancellation = new CancellationTokenSource();
        using var stopWatcher = StartStopWatcher(cancellation);
        var receiver = new WindowsNativeReceiver(_paths, status, scanner, devices);
        var exitCode = await receiver.RunAsync(cancellation.Token).ConfigureAwait(false);
        status.Write("WINDOWS_NATIVE_EXITED", $"Windows Native receiver exited with code {exitCode}; HidHide remains enabled");
        return exitCode;
    }

    public Task<int> StopAsync()
    {
        var status = new StatusWriter(_paths, "windows-native.log");
        status.Write("WINDOWS_NATIVE_STOP_REQUESTED", "Windows Native stop requested");
        SignalStop();
        status.Write("WINDOWS_NATIVE_STOP_SIGNAL", "Stop signal written; HidHide cloak is intentionally left enabled");
        return Task.FromResult(0);
    }

    private async Task<bool> EnsureHidHideAsync(HidHideManager hidHide, StatusWriter status)
    {
        if (hidHide.IsInstalled)
        {
            status.Write("WINDOWS_NATIVE_HIDHIDE_OK", "HidHide is installed: " + await hidHide.GetVersionAsync().ConfigureAwait(false));
            return true;
        }

        status.Write("WINDOWS_NATIVE_HIDHIDE_INSTALL", "HidHide missing; trying winget install");
        await _runner.RunAsync(
            "winget",
            new[] { "install", "-e", "--id", "Nefarius.HidHide", "--accept-package-agreements", "--accept-source-agreements" },
            _paths.Root,
            180000,
            createNoWindow: false).ConfigureAwait(false);

        if (hidHide.IsInstalled)
        {
            status.Write("WINDOWS_NATIVE_HIDHIDE_OK", "HidHide installed");
            return true;
        }

        status.Write("WINDOWS_NATIVE_NOT_READY", "HidHide is required to prevent duplicate physical controller input");
        return false;
    }

    private async Task<bool> EnsureVigemBusAsync(StatusWriter status)
    {
        if (await IsVigemBusInstalledAsync().ConfigureAwait(false))
        {
            status.Write("WINDOWS_NATIVE_VIGEM_OK", "ViGEmBus driver is installed");
            return true;
        }

        status.Write("WINDOWS_NATIVE_VIGEM_INSTALL", "ViGEmBus missing; trying winget install");
        await _runner.RunAsync(
            "winget",
            new[] { "install", "-e", "--id", "Nefarius.ViGEmBus", "--accept-package-agreements", "--accept-source-agreements" },
            _paths.Root,
            180000,
            createNoWindow: false).ConfigureAwait(false);

        if (await IsVigemBusInstalledAsync().ConfigureAwait(false))
        {
            status.Write("WINDOWS_NATIVE_VIGEM_OK", "ViGEmBus driver is installed");
            return true;
        }

        status.Write("WINDOWS_NATIVE_NOT_READY", "ViGEmBus is required for virtual Xbox 360 controllers");
        return false;
    }

    private async Task<bool> IsVigemBusInstalledAsync()
    {
        var result = await _runner.RunAsync(
            "powershell.exe",
            new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", "if (Get-Service -Name ViGEmBus -ErrorAction SilentlyContinue) { 'OK' }" },
            _paths.Root,
            15000).ConfigureAwait(false);
        return result.Output.Contains("OK", StringComparison.OrdinalIgnoreCase);
    }

    private async Task WriteProbeAsync(WindowsNativeHidScanner scanner)
    {
        try
        {
            var report = await scanner.CreateProbeReportAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
            await File.WriteAllTextAsync(Path.Combine(_paths.LogDirectory, "windows-native-probe.txt"), report).ConfigureAwait(false);
        }
        catch
        {
            // Probe output is helpful, but startup status should remain the source of truth.
        }
    }

    private System.Threading.Timer StartStopWatcher(CancellationTokenSource cancellation)
    {
        return new System.Threading.Timer(_ =>
        {
            try
            {
                if (File.Exists(StopSignalPath()))
                {
                    cancellation.Cancel();
                }
            }
            catch
            {
                cancellation.Cancel();
            }
        }, null, TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(500));
    }

    private void SignalStop()
    {
        Directory.CreateDirectory(_paths.LogDirectory);
        File.WriteAllText(StopSignalPath(), DateTimeOffset.Now.ToString("O") + Environment.NewLine);
    }

    private void ClearStopSignal()
    {
        var path = StopSignalPath();
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private string StopSignalPath()
    {
        return Path.Combine(_paths.LogDirectory, "windows-native.stop");
    }

    private string ResolveCurrentAppPath()
    {
        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(processPath) && File.Exists(processPath))
        {
            return processPath;
        }

        if (File.Exists(_paths.AppExecutable))
        {
            return _paths.AppExecutable;
        }

        throw new InvalidOperationException("Could not resolve the Stadia X executable path for HidHide.");
    }

    private static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static string FirstNonEmpty(params string[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "";
    }
}
