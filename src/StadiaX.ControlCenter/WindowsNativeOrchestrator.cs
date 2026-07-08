using System.Diagnostics;
using System.Security.Principal;
using System.Text;

namespace StadiaX.ControlCenter;

internal sealed class WindowsNativeOrchestrator
{
    private const int MaxControllers = 4;
    private const int StartPhaseCount = 5;
    private static readonly TimeSpan StartLockMaxAge = TimeSpan.FromSeconds(60);

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
        status.Reset("WINDOWS_NATIVE_START_REQUESTED", $"Windows Native start requested pid={Environment.ProcessId}");
        status.WritePhase("Windows Native", 1, StartPhaseCount, "Prerequisites", "START", "Checking HidHide and ViGEmBus");
        ClearStopSignal();
        if (WindowsNativeRuntime.TryGetActiveReceiver(_paths, out var activePid, out var activeControllers))
        {
            status.Write("WINDOWS_NATIVE_ALREADY_RUNNING", $"Windows Native receiver is already running pid={activePid} controllers={activeControllers}");
            status.Write("WINDOWS_NATIVE_READY", $"Windows Native input already running for {activeControllers} controller(s)");
            status.WritePhase("Windows Native", 4, StartPhaseCount, "Virtual pads", "OK", $"Receiver already active pid={activePid}");
            return 0;
        }
        using var startLock = TryAcquireStartLock(status);
        if (startLock is null)
        {
            status.Write("WINDOWS_NATIVE_START_BUSY", "Another Windows Native start request is already in progress");
            status.WritePhase("Windows Native", 1, StartPhaseCount, "Prerequisites", "WAIT", "Another start request is already in progress");
            return 2;
        }

        var hidHide = new HidHideManager(_paths, _runner);
        if (!await EnsureHidHideAsync(hidHide, status).ConfigureAwait(false))
        {
            status.WritePhase("Windows Native", 1, StartPhaseCount, "Prerequisites", "FAIL", "HidHide is not ready");
            return 2;
        }

        if (!await EnsureVigemBusAsync(status).ConfigureAwait(false))
        {
            status.WritePhase("Windows Native", 1, StartPhaseCount, "Prerequisites", "FAIL", "ViGEmBus is not ready");
            return 2;
        }
        status.WritePhase("Windows Native", 1, StartPhaseCount, "Prerequisites", "OK", "Required Windows drivers are ready");

        var scanner = new WindowsNativeHidScanner(hidHide);
        status.WritePhase("Windows Native", 2, StartPhaseCount, "Device discovery", "START", "Scanning Windows HID for Stadia controllers");
        status.Write("WINDOWS_NATIVE_SCAN_START", "Scanning Windows HID devices for Stadia controllers");
        var scan = await scanner.ScanStadiaControllersAsync().ConfigureAwait(false);
        var devices = scan.Devices.Take(MaxControllers).ToArray();
        status.Write(
            "WINDOWS_NATIVE_SCAN_RESULT",
            $"Detected {devices.Length} Stadia HID candidate(s); raw={scan.RawCandidateCount} duplicateIgnored={scan.DuplicateCandidateCount}");
        if (scan.DuplicateCandidateCount > 0)
        {
            status.Write("WINDOWS_NATIVE_SCAN_DEDUP", $"Ignored {scan.DuplicateCandidateCount} duplicate HID candidate(s) before assigning virtual pads");
        }
        if (devices.Length == 0)
        {
            status.Write("WINDOWS_NATIVE_NOT_READY", "No Stadia HID controller is visible to Windows");
            status.WritePhase("Windows Native", 2, StartPhaseCount, "Device discovery", "WAIT", "No Stadia controller visible; opening Windows Bluetooth settings");
            TryOpenBluetoothSettings(status);
            await WriteProbeAsync(scanner).ConfigureAwait(false);
            return 2;
        }
        status.WritePhase("Windows Native", 2, StartPhaseCount, "Device discovery", "OK", $"Detected {devices.Length} Stadia HID candidate(s)");

        for (var i = 0; i < devices.Length; i++)
        {
            var device = devices[i];
            status.Write(
                "WINDOWS_NATIVE_DEVICE",
                $"P{i + 1} {DeviceName(device)} vidpid={device.VendorId:X4}:{device.ProductId:X4} input={device.MaxInputReportLength} output={device.MaxOutputReportLength} hidhide={(string.IsNullOrWhiteSpace(device.DeviceInstancePath) ? "missing" : "matched")}");
        }

        var missingHidePath = devices.Where(device => string.IsNullOrWhiteSpace(device.DeviceInstancePath)).ToArray();
        if (missingHidePath.Length > 0)
        {
            status.Write("WINDOWS_NATIVE_NOT_READY", "Stadia HID was found, but HidHide did not expose a matching device path");
            status.WritePhase("Windows Native", 3, StartPhaseCount, "Input isolation", "FAIL", "HidHide did not expose the device instance path needed to hide physical input");
            await WriteProbeAsync(scanner).ConfigureAwait(false);
            return 2;
        }

        var appPath = ResolveCurrentAppPath();
        status.WritePhase("Windows Native", 3, StartPhaseCount, "Input isolation", "START", "Registering app and hiding physical Stadia HID input");
        status.Write("WINDOWS_NATIVE_HIDHIDE_START", $"Registering {appPath} and hiding {devices.Length} physical Stadia HID device(s)");
        var hide = await hidHide.ConfigureStadiaDevicesAsync(
            appPath,
            devices.Select(device => device.DeviceInstancePath).ToArray(),
            elevated: !IsAdministrator()).ConfigureAwait(false);
        status.Write("WINDOWS_NATIVE_HIDHIDE_RESULT", $"exit={hide.ExitCode} output={Shorten(FirstNonEmpty(hide.Output, hide.Error, "none"), 260)}");

        if (hide.ExitCode != 0)
        {
            status.Write("WINDOWS_NATIVE_HIDHIDE_FAILED", FirstNonEmpty(hide.Error, hide.Output, "HidHide configuration failed"));
            status.WritePhase("Windows Native", 3, StartPhaseCount, "Input isolation", "FAIL", "Could not configure HidHide");
            return 1;
        }

        status.WritePhase("Windows Native", 3, StartPhaseCount, "Input isolation", "OK", "Physical Stadia input is hidden from games");
        status.Write("WINDOWS_NATIVE_HIDHIDE_OK", "Physical Stadia HID devices are hidden; HidHide cloak remains enabled");
        using var cancellation = new CancellationTokenSource();
        using var stopWatcher = StartStopWatcher(cancellation);
        var receiver = new WindowsNativeReceiver(_paths, status, scanner, devices);
        status.WritePhase("Windows Native", 4, StartPhaseCount, "Virtual pads", "START", $"Creating {devices.Length} virtual Xbox 360 controller slot(s)");
        status.Write("WINDOWS_NATIVE_RECEIVER_START", $"Starting receiver for {devices.Length} controller slot(s)");
        var exitCode = await receiver.RunAsync(cancellation.Token).ConfigureAwait(false);
        await RestorePhysicalInputAsync(hidHide, status).ConfigureAwait(false);
        status.WritePhase("Windows Native", 5, StartPhaseCount, "Shutdown", exitCode == 0 ? "OK" : "FAIL", $"Receiver exited with code {exitCode}");
        status.Write("WINDOWS_NATIVE_EXITED", $"Windows Native receiver exited with code {exitCode}; physical input restore requested");
        return exitCode;
    }

    public async Task<int> StopAsync()
    {
        var status = new StatusWriter(_paths, "windows-native.log");
        status.WritePhase("Windows Native", 5, StartPhaseCount, "Shutdown", "START", "Stop requested");
        status.Write("WINDOWS_NATIVE_STOP_REQUESTED", "Windows Native stop requested");
        SignalStop();
        status.Write("WINDOWS_NATIVE_STOP_SIGNAL", "Stop signal written; waiting for receiver shutdown");
        var stopped = await WaitForReceiverStopAsync(status).ConfigureAwait(false);
        status.Write(
            stopped ? "WINDOWS_NATIVE_STOP_CONFIRMED" : "WINDOWS_NATIVE_STOP_WAIT_TIMEOUT",
            stopped ? "No active Windows Native receiver remains after stop signal" : "Receiver still appears active after waiting for shutdown");
        await RestorePhysicalInputAsync(new HidHideManager(_paths, _runner), status).ConfigureAwait(false);
        status.WritePhase("Windows Native", 5, StartPhaseCount, "Shutdown", stopped ? "OK" : "WARN", "Stop completed and physical input restore requested");
        return 0;
    }

    private async Task<bool> EnsureHidHideAsync(HidHideManager hidHide, StatusWriter status)
    {
        status.WritePhase("Windows Native", 1, StartPhaseCount, "HidHide", "START", "Checking HidHide CLI");
        if (hidHide.IsInstalled)
        {
            status.Write("WINDOWS_NATIVE_HIDHIDE_OK", "HidHide is installed: " + await hidHide.GetVersionAsync().ConfigureAwait(false));
            status.WritePhase("Windows Native", 1, StartPhaseCount, "HidHide", "OK", "HidHide is installed");
            return true;
        }

        status.Write("WINDOWS_NATIVE_HIDHIDE_INSTALL", "HidHide missing; trying winget install");
        status.WritePhase("Windows Native", 1, StartPhaseCount, "HidHide", "INSTALL", "HidHide missing; installing with winget");
        if (!await EnsureWingetAsync(status).ConfigureAwait(false))
        {
            status.WritePhase("Windows Native", 1, StartPhaseCount, "HidHide", "FAIL", "winget is missing, so HidHide cannot be installed automatically");
            return false;
        }

        var install = await _runner.RunAsync(
            "winget",
            new[] { "install", "-e", "--id", "Nefarius.HidHide", "--accept-package-agreements", "--accept-source-agreements" },
            _paths.Root,
            180000,
            createNoWindow: false).ConfigureAwait(false);
        status.Write("WINDOWS_NATIVE_HIDHIDE_INSTALL_RESULT", $"exit={install.ExitCode} output={Shorten(FirstNonEmpty(install.Output, install.Error, "none"), 260)}");

        if (hidHide.IsInstalled)
        {
            status.Write("WINDOWS_NATIVE_HIDHIDE_OK", "HidHide installed");
            status.WritePhase("Windows Native", 1, StartPhaseCount, "HidHide", "OK", "HidHide installed");
            return true;
        }

        status.Write("WINDOWS_NATIVE_NOT_READY", "HidHide is required to prevent duplicate physical controller input");
        status.WritePhase("Windows Native", 1, StartPhaseCount, "HidHide", "FAIL", "HidHide is still missing after install attempt");
        return false;
    }

    private async Task<bool> EnsureVigemBusAsync(StatusWriter status)
    {
        status.WritePhase("Windows Native", 1, StartPhaseCount, "ViGEmBus", "START", "Checking virtual controller driver");
        if (await IsVigemBusInstalledAsync().ConfigureAwait(false))
        {
            status.Write("WINDOWS_NATIVE_VIGEM_OK", "ViGEmBus driver is installed");
            status.WritePhase("Windows Native", 1, StartPhaseCount, "ViGEmBus", "OK", "ViGEmBus driver is installed");
            return true;
        }

        status.Write("WINDOWS_NATIVE_VIGEM_INSTALL", "ViGEmBus missing; trying winget install");
        status.WritePhase("Windows Native", 1, StartPhaseCount, "ViGEmBus", "INSTALL", "ViGEmBus missing; installing with winget");
        if (!await EnsureWingetAsync(status).ConfigureAwait(false))
        {
            status.WritePhase("Windows Native", 1, StartPhaseCount, "ViGEmBus", "FAIL", "winget is missing, so ViGEmBus cannot be installed automatically");
            return false;
        }

        var install = await _runner.RunAsync(
            "winget",
            new[] { "install", "-e", "--id", "Nefarius.ViGEmBus", "--accept-package-agreements", "--accept-source-agreements" },
            _paths.Root,
            180000,
            createNoWindow: false).ConfigureAwait(false);
        status.Write("WINDOWS_NATIVE_VIGEM_INSTALL_RESULT", $"exit={install.ExitCode} output={Shorten(FirstNonEmpty(install.Output, install.Error, "none"), 260)}");

        if (await IsVigemBusInstalledAsync().ConfigureAwait(false))
        {
            status.Write("WINDOWS_NATIVE_VIGEM_OK", "ViGEmBus driver is installed");
            status.WritePhase("Windows Native", 1, StartPhaseCount, "ViGEmBus", "OK", "ViGEmBus driver is installed");
            return true;
        }

        status.Write("WINDOWS_NATIVE_NOT_READY", "ViGEmBus is required for virtual Xbox 360 controllers");
        status.WritePhase("Windows Native", 1, StartPhaseCount, "ViGEmBus", "FAIL", "ViGEmBus is still missing after install attempt");
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

    private async Task<bool> EnsureWingetAsync(StatusWriter status)
    {
        if (await _runner.CommandExistsAsync("winget", _paths.Root).ConfigureAwait(false))
        {
            return true;
        }

        status.Write("WINDOWS_NATIVE_WINGET_MISSING", "winget was not found; automatic driver installation is unavailable");
        return false;
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

    private static void TryOpenBluetoothSettings(StatusWriter status)
    {
        try
        {
            Process.Start(new ProcessStartInfo("ms-settings:bluetooth") { UseShellExecute = true });
            status.Write("WINDOWS_NATIVE_BLUETOOTH_SETTINGS_OPENED", "Opened Windows Bluetooth settings for pairing");
        }
        catch (Exception ex)
        {
            status.Write("WINDOWS_NATIVE_BLUETOOTH_SETTINGS_FAILED", "Could not open Windows Bluetooth settings: " + ex.Message);
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

    private async Task<bool> WaitForReceiverStopAsync(StatusWriter status)
    {
        var loggedWait = false;
        for (var attempt = 0; attempt < 40; attempt++)
        {
            if (!WindowsNativeRuntime.TryGetActiveReceiver(_paths, out var pid, out var controllers))
            {
                return true;
            }

            if (!loggedWait)
            {
                status.Write("WINDOWS_NATIVE_STOP_WAITING", $"Waiting for receiver pid={pid} controllers={controllers}");
                loggedWait = true;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250)).ConfigureAwait(false);
        }

        return !WindowsNativeRuntime.TryGetActiveReceiver(_paths, out _, out _);
    }

    private async Task RestorePhysicalInputAsync(HidHideManager hidHide, StatusWriter status)
    {
        if (!hidHide.IsInstalled)
        {
            status.Write("WINDOWS_NATIVE_HIDHIDE_RESTORE_SKIPPED", "HidHide is not installed; no cloak to disable");
            return;
        }

        status.WritePhase("Windows Native", 5, StartPhaseCount, "Input restore", "START", "Disabling HidHide cloak without prompting for elevation");
        var restore = await hidHide.DisableCloakAsync(elevated: false).ConfigureAwait(false);
        status.Write("WINDOWS_NATIVE_HIDHIDE_RESTORE_RESULT", $"exit={restore.ExitCode} output={Shorten(FirstNonEmpty(restore.Output, restore.Error, "none"), 260)}");
        status.WritePhase(
            "Windows Native",
            5,
            StartPhaseCount,
            "Input restore",
            restore.ExitCode == 0 ? "OK" : "WARN",
            restore.ExitCode == 0 ? "Physical Stadia input restored" : "Could not confirm HidHide cloak restore");
    }

    private void ClearStopSignal()
    {
        var path = StopSignalPath();
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private IDisposable? TryAcquireStartLock(StatusWriter status)
    {
        Directory.CreateDirectory(_paths.LogDirectory);
        var path = StartLockPath();
        if (File.Exists(path))
        {
            var age = DateTimeOffset.UtcNow - File.GetLastWriteTimeUtc(path);
            if (age <= StartLockMaxAge)
            {
                status.Write("WINDOWS_NATIVE_START_LOCK_BUSY", $"Existing start lock ageSeconds={(int)age.TotalSeconds}");
                return null;
            }

            try
            {
                File.Delete(path);
                status.Write("WINDOWS_NATIVE_START_LOCK_STALE", $"Removed stale start lock ageSeconds={(int)age.TotalSeconds}");
            }
            catch (Exception ex)
            {
                status.Write("WINDOWS_NATIVE_START_LOCK_FAILED", "Could not remove stale start lock: " + ex.Message);
                return null;
            }
        }

        try
        {
            var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            using var writer = new StreamWriter(stream, Encoding.UTF8, 1024, leaveOpen: true);
            writer.WriteLine($"{DateTimeOffset.Now:O}|pid={Environment.ProcessId}");
            writer.Flush();
            stream.Flush();
            status.Write("WINDOWS_NATIVE_START_LOCK_ACQUIRED", $"pid={Environment.ProcessId}");
            return new FileLock(stream, path);
        }
        catch (IOException ex)
        {
            status.Write("WINDOWS_NATIVE_START_LOCK_BUSY", "Could not acquire start lock: " + ex.Message);
            return null;
        }
    }

    private string StopSignalPath()
    {
        return Path.Combine(_paths.LogDirectory, "windows-native.stop");
    }

    private string StartLockPath()
    {
        return Path.Combine(_paths.LogDirectory, "windows-native.start.lock");
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

    private static string DeviceName(WindowsNativeHidDevice device)
    {
        return !string.IsNullOrWhiteSpace(device.FriendlyName)
            ? device.FriendlyName
            : !string.IsNullOrWhiteSpace(device.ProductName)
                ? device.ProductName
                : "Stadia Controller";
    }

    private static string Shorten(string value, int maxLength)
    {
        value = value.ReplaceLineEndings(" ").Trim();
        return value.Length <= maxLength ? value : value[..maxLength] + "...";
    }

    private sealed class FileLock : IDisposable
    {
        private readonly FileStream _stream;
        private readonly string _path;

        public FileLock(FileStream stream, string path)
        {
            _stream = stream;
            _path = path;
        }

        public void Dispose()
        {
            _stream.Dispose();
            try { File.Delete(_path); } catch { }
        }
    }
}
