using System.Diagnostics;
using System.Security.Principal;
using System.Text;

namespace StadiaX.ControlCenter;

internal sealed class WindowsNativeOrchestrator
{
    private const int MaxControllers = 4;
    private const int StartPhaseCount = 5;
    private static readonly TimeSpan StartLockMaxAge = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan StopSignalMaxAge = TimeSpan.FromMinutes(10);

    private readonly AppPaths _paths;
    private readonly ProcessRunner _runner;

    public WindowsNativeOrchestrator(AppPaths paths, ProcessRunner runner)
    {
        _paths = paths;
        _runner = runner;
    }

    public async Task<int> StartAsync()
    {
        var startRequestedAt = DateTimeOffset.UtcNow;
        var status = new StatusWriter(_paths, "windows-native.log");
        status.Reset("WINDOWS_NATIVE_START_REQUESTED", $"Windows Native start requested pid={Environment.ProcessId}");
        status.WritePhase("Windows Native", 1, StartPhaseCount, "Prerequisites", "START", "Checking HidHide and ViGEmBus");
        if (WindowsNativeRuntime.TryGetActiveReceiver(_paths, out var activePid, out var activeControllers))
        {
            ReportAlreadyRunning(status, activePid, activeControllers);
            return 0;
        }
        using var startLock = TryAcquireStartLock(status);
        if (startLock is null)
        {
            status.Write("WINDOWS_NATIVE_START_BUSY", "Another Windows Native start request is already in progress");
            status.WritePhase("Windows Native", 1, StartPhaseCount, "Prerequisites", "WAIT", "Another start request is already in progress");
            return 2;
        }

        if (WindowsNativeRuntime.TryGetActiveReceiver(_paths, out activePid, out activeControllers))
        {
            ReportAlreadyRunning(status, activePid, activeControllers);
            return 0;
        }

        if (!ClearStopSignal(status, startRequestedAt))
        {
            status.WritePhase("Windows Native", 1, StartPhaseCount, "Prerequisites", "FAIL", "Could not clear the previous receiver stop signal");
            return 1;
        }
        using var cancellation = new CancellationTokenSource();
        await using var stopWatcher = StartStopWatcher(cancellation);
        ClearControllerState(status, "START");

        var hidHide = new HidHideManager(_paths, _runner);
        if (!await EnsureHidHideAsync(hidHide, status, cancellation.Token).ConfigureAwait(false))
        {
            if (ReportStartCancelled(cancellation.Token, status, 1, "Prerequisites")) return 0;
            status.WritePhase("Windows Native", 1, StartPhaseCount, "Prerequisites", "FAIL", "HidHide is not ready");
            return 2;
        }
        if (ReportStartCancelled(cancellation.Token, status, 1, "Prerequisites")) return 0;

        if (!await EnsureVigemBusAsync(status, cancellation.Token).ConfigureAwait(false))
        {
            if (ReportStartCancelled(cancellation.Token, status, 1, "Prerequisites")) return 0;
            status.WritePhase("Windows Native", 1, StartPhaseCount, "Prerequisites", "FAIL", "ViGEmBus is not ready");
            return 2;
        }
        if (ReportStartCancelled(cancellation.Token, status, 1, "Prerequisites")) return 0;
        status.WritePhase("Windows Native", 1, StartPhaseCount, "Prerequisites", "OK", "Required Windows drivers are ready");

        var scanner = new WindowsNativeHidScanner(hidHide);
        status.WritePhase("Windows Native", 2, StartPhaseCount, "Device discovery", "START", "Scanning Windows HID for Stadia controllers");
        status.Write("WINDOWS_NATIVE_SCAN_START", "Scanning Windows HID devices for Stadia controllers");
        WindowsNativeHidScanResult scan;
        try
        {
            scan = await scanner.ScanStadiaControllersAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            status.Write("WINDOWS_NATIVE_SCAN_FAILED", "Windows HID scan failed: " + ex.Message);
            status.WritePhase("Windows Native", 2, StartPhaseCount, "Device discovery", "FAIL", "Windows could not enumerate Stadia HID devices");
            AppDiagnosticsLogger.Record("WINDOWS_NATIVE_SCAN_FAILED", ("error", ex.ToString()));
            return 1;
        }

        if (ReportStartCancelled(cancellation.Token, status, 2, "Device discovery")) return 0;

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
        CommandResult hide;
        try
        {
            hide = await hidHide.ConfigureStadiaDevicesAsync(
                appPath,
                devices.Select(device => device.DeviceInstancePath).ToArray(),
                elevated: !IsAdministrator(),
                cancellationToken: cancellation.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            status.Write("WINDOWS_NATIVE_HIDHIDE_FAILED", "HidHide configuration raised an exception: " + ex.Message);
            AppDiagnosticsLogger.Record("WINDOWS_NATIVE_HIDHIDE_EXCEPTION", ("error", ex.ToString()));
            await RollbackPartialHidHideConfigurationAsync(hidHide, status).ConfigureAwait(false);
            status.WritePhase("Windows Native", 3, StartPhaseCount, "Input isolation", "FAIL", "Could not configure HidHide");
            return 1;
        }
        status.Write("WINDOWS_NATIVE_HIDHIDE_RESULT", $"exit={hide.ExitCode} output={Shorten(FirstNonEmpty(hide.Output, hide.Error, "none"), 260)}");

        if (cancellation.IsCancellationRequested)
        {
            await RollbackPartialHidHideConfigurationAsync(hidHide, status).ConfigureAwait(false);
            ReportStartCancelled(cancellation.Token, status, 3, "Input isolation");
            return 0;
        }

        if (hide.ExitCode != 0)
        {
            status.Write("WINDOWS_NATIVE_HIDHIDE_FAILED", FirstNonEmpty(hide.Error, hide.Output, "HidHide configuration failed"));
            await RollbackPartialHidHideConfigurationAsync(hidHide, status).ConfigureAwait(false);
            status.WritePhase("Windows Native", 3, StartPhaseCount, "Input isolation", "FAIL", "Could not configure HidHide");
            return 1;
        }

        status.WritePhase("Windows Native", 3, StartPhaseCount, "Input isolation", "OK", "Physical Stadia input is hidden from games");
        status.Write("WINDOWS_NATIVE_HIDHIDE_OK", "Physical Stadia HID devices are hidden; HidHide cloak remains enabled");
        var exitCode = 1;
        var inputRestored = false;
        try
        {
            var receiver = new WindowsNativeReceiver(_paths, status, scanner, devices);
            status.WritePhase("Windows Native", 4, StartPhaseCount, "Virtual pads", "START", $"Creating {devices.Length} virtual Xbox 360 controller slot(s)");
            status.Write("WINDOWS_NATIVE_RECEIVER_START", $"Starting receiver for {devices.Length} controller slot(s)");
            exitCode = await receiver.RunAsync(cancellation.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            status.Write("WINDOWS_NATIVE_RECEIVER_CRASHED", ex.Message);
            AppDiagnosticsLogger.Record("WINDOWS_NATIVE_RECEIVER_CRASHED", ("error", ex.ToString()));
            exitCode = 1;
        }
        finally
        {
            inputRestored = await RestorePhysicalInputSafelyAsync(hidHide, status).ConfigureAwait(false);
        }

        var finalExitCode = exitCode == 0 && inputRestored ? 0 : 1;
        status.WritePhase(
            "Windows Native",
            5,
            StartPhaseCount,
            "Shutdown",
            finalExitCode == 0 ? "OK" : "FAIL",
            $"Receiver exit={exitCode}; physical input restored={inputRestored}");
        status.Write(
            "WINDOWS_NATIVE_EXITED",
            $"Windows Native receiver exited with code {finalExitCode}; receiver={exitCode} physicalInputRestored={inputRestored}");
        return finalExitCode;
    }

    public async Task<int> StopAsync()
    {
        var status = new StatusWriter(_paths, "windows-native.log");
        using var stopLock = TryAcquireNamedMutex(
            @"Local\StadiaX.WindowsNativeStop",
            TimeSpan.Zero,
            status,
            "WINDOWS_NATIVE_STOP_LOCK");
        if (stopLock is null)
        {
            status.Write("WINDOWS_NATIVE_STOP_BUSY", "Another Windows Native stop request is already in progress");
            status.WritePhase("Windows Native", 5, StartPhaseCount, "Shutdown", "WAIT", "Another stop request is already in progress");
            return 2;
        }

        status.WritePhase("Windows Native", 5, StartPhaseCount, "Shutdown", "START", "Stop requested");
        status.Write("WINDOWS_NATIVE_STOP_REQUESTED", "Windows Native stop requested");
        var stopSignaled = SignalStop(status);
        ClearControllerState(status, "STOP");
        status.Write(
            stopSignaled ? "WINDOWS_NATIVE_STOP_SIGNAL" : "WINDOWS_NATIVE_STOP_SIGNAL_FALLBACK",
            stopSignaled ? "Stop signal written; waiting for receiver shutdown" : "Stop signal could not be written; termination fallback may be required");
        var stopped = await WaitForReceiverStopAsync(status).ConfigureAwait(false);
        if (!stopped)
        {
            stopped = await TerminateReceiverIfActiveAsync(status).ConfigureAwait(false);
        }

        ClearControllerState(status, "STOP_FINAL");
        status.Write(
            stopped ? "WINDOWS_NATIVE_STOP_CONFIRMED" : "WINDOWS_NATIVE_STOP_WAIT_TIMEOUT",
            stopped ? "No active Windows Native receiver remains after stop signal" : "Receiver still appears active after termination attempt");
        var inputRestored = await RestorePhysicalInputSafelyAsync(new HidHideManager(_paths, _runner), status).ConfigureAwait(false);
        var stopCompleted = stopped && inputRestored;
        status.WritePhase(
            "Windows Native",
            5,
            StartPhaseCount,
            "Shutdown",
            stopCompleted ? "OK" : "WARN",
            $"Stop signal written={stopSignaled}; receiver stopped={stopped}; physical input restored={inputRestored}");
        return stopCompleted ? 0 : 1;
    }

    private static bool ReportStartCancelled(
        CancellationToken cancellationToken,
        StatusWriter status,
        int phase,
        string phaseName)
    {
        if (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }

        status.Write("WINDOWS_NATIVE_START_CANCELLED", $"Stop requested during {phaseName}");
        status.WritePhase("Windows Native", phase, StartPhaseCount, phaseName, "WAIT", "Start cancelled by Stop request");
        return true;
    }
    private static void ReportAlreadyRunning(StatusWriter status, int pid, int controllers)
    {
        status.Write("WINDOWS_NATIVE_ALREADY_RUNNING", $"Windows Native receiver is already running pid={pid} controllers={controllers}");
        status.Write("WINDOWS_NATIVE_READY", $"Windows Native input already running for {controllers} controller(s)");
        status.WritePhase("Windows Native", 4, StartPhaseCount, "Virtual pads", "OK", $"Receiver already active pid={pid}");
    }

    private async Task<bool> EnsureHidHideAsync(
        HidHideManager hidHide,
        StatusWriter status,
        CancellationToken cancellationToken)
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
        if (!await EnsureWingetAsync(status, cancellationToken).ConfigureAwait(false))
        {
            status.WritePhase("Windows Native", 1, StartPhaseCount, "HidHide", "FAIL", "winget is missing, so HidHide cannot be installed automatically");
            return false;
        }

        var install = await _runner.RunAsync(
            "winget",
            new[] { "install", "-e", "--id", "Nefarius.HidHide", "--accept-package-agreements", "--accept-source-agreements" },
            _paths.Root,
            180000,
            createNoWindow: false,
            cancellationToken: cancellationToken).ConfigureAwait(false);
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

    private async Task<bool> EnsureVigemBusAsync(
        StatusWriter status,
        CancellationToken cancellationToken)
    {
        status.WritePhase("Windows Native", 1, StartPhaseCount, "ViGEmBus", "START", "Checking virtual controller driver");
        if (await IsVigemBusInstalledAsync(cancellationToken).ConfigureAwait(false))
        {
            status.Write("WINDOWS_NATIVE_VIGEM_OK", "ViGEmBus driver is installed");
            status.WritePhase("Windows Native", 1, StartPhaseCount, "ViGEmBus", "OK", "ViGEmBus driver is installed");
            return true;
        }

        status.Write("WINDOWS_NATIVE_VIGEM_INSTALL", "ViGEmBus missing; trying winget install");
        status.WritePhase("Windows Native", 1, StartPhaseCount, "ViGEmBus", "INSTALL", "ViGEmBus missing; installing with winget");
        if (!await EnsureWingetAsync(status, cancellationToken).ConfigureAwait(false))
        {
            status.WritePhase("Windows Native", 1, StartPhaseCount, "ViGEmBus", "FAIL", "winget is missing, so ViGEmBus cannot be installed automatically");
            return false;
        }

        var install = await _runner.RunAsync(
            "winget",
            new[] { "install", "-e", "--id", "Nefarius.ViGEmBus", "--accept-package-agreements", "--accept-source-agreements" },
            _paths.Root,
            180000,
            createNoWindow: false,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        status.Write("WINDOWS_NATIVE_VIGEM_INSTALL_RESULT", $"exit={install.ExitCode} output={Shorten(FirstNonEmpty(install.Output, install.Error, "none"), 260)}");

        if (await IsVigemBusInstalledAsync(cancellationToken).ConfigureAwait(false))
        {
            status.Write("WINDOWS_NATIVE_VIGEM_OK", "ViGEmBus driver is installed");
            status.WritePhase("Windows Native", 1, StartPhaseCount, "ViGEmBus", "OK", "ViGEmBus driver is installed");
            return true;
        }

        status.Write("WINDOWS_NATIVE_NOT_READY", "ViGEmBus is required for virtual Xbox 360 controllers");
        status.WritePhase("Windows Native", 1, StartPhaseCount, "ViGEmBus", "FAIL", "ViGEmBus is still missing after install attempt");
        return false;
    }

    private async Task<bool> IsVigemBusInstalledAsync(CancellationToken cancellationToken)
    {
        var result = await _runner.RunAsync(
            "powershell.exe",
            new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", "if (Get-Service -Name ViGEmBus -ErrorAction SilentlyContinue) { 'OK' }" },
            _paths.Root,
            15000,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return result.Output.Contains("OK", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<bool> EnsureWingetAsync(
        StatusWriter status,
        CancellationToken cancellationToken)
    {
        if (await _runner.CommandExistsAsync("winget", _paths.Root, cancellationToken).ConfigureAwait(false))
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
                if (StopSignalIsActive())
                {
                    TryCancel(cancellation);
                }
            }
            catch (Exception ex)
            {
                AppDiagnosticsLogger.Record("WINDOWS_NATIVE_STOP_WATCHER_WARN", ("error", ex.Message));
                TryCancel(cancellation);
            }
        }, null, TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(500));
    }

    private static void TryCancel(CancellationTokenSource cancellation)
    {
        try
        {
            cancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private static IDisposable? TryAcquireNamedMutex(
        string name,
        TimeSpan timeout,
        StatusWriter status,
        string eventPrefix)
    {
        Mutex? mutex = null;
        try
        {
            mutex = new Mutex(false, name);
            var acquired = false;
            try
            {
                acquired = mutex.WaitOne(timeout);
            }
            catch (AbandonedMutexException)
            {
                acquired = true;
            }

            if (!acquired)
            {
                mutex.Dispose();
                status.Write(eventPrefix + "_BUSY", $"Timed out waiting for {name}");
                return null;
            }

            status.Write(eventPrefix + "_ACQUIRED", $"pid={Environment.ProcessId}");
            return new MutexLock(mutex);
        }
        catch (Exception ex)
        {
            mutex?.Dispose();
            status.Write(eventPrefix + "_FAILED", ex.Message);
            AppDiagnosticsLogger.Record(eventPrefix + "_FAILED", ("error", ex.Message));
            return null;
        }
    }
    private bool SignalStop(StatusWriter? status = null)
    {
        try
        {
            Directory.CreateDirectory(_paths.LogDirectory);
            File.WriteAllText(StopSignalPath(), DateTimeOffset.Now.ToString("O") + Environment.NewLine);
            return true;
        }
        catch (Exception ex)
        {
            status?.Write("WINDOWS_NATIVE_STOP_SIGNAL_WARN", "Could not write windows-native.stop: " + ex.Message);
            AppDiagnosticsLogger.Record("WINDOWS_NATIVE_STOP_SIGNAL_WRITE_WARN", ("error", ex.Message));
            return false;
        }
    }

    private bool StopSignalIsActive()
    {
        var path = StopSignalPath();
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            var text = File.ReadLines(path).FirstOrDefault()?.Trim();
            var timestamp = DateTimeOffset.TryParse(text, out var parsed)
                ? parsed
                : new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero);
            var age = DateTimeOffset.UtcNow - timestamp.ToUniversalTime();
            if (age <= StopSignalMaxAge)
            {
                return true;
            }

            File.Delete(path);
            AppDiagnosticsLogger.Record("WINDOWS_NATIVE_STOP_SIGNAL_STALE", ("ageSeconds", ((int)age.TotalSeconds).ToString()));
            return false;
        }
        catch (Exception ex)
        {
            AppDiagnosticsLogger.Record("WINDOWS_NATIVE_STOP_SIGNAL_READ_WARN", ("error", ex.Message));
            return true;
        }
    }

    private void ClearControllerState(StatusWriter status, string phase)
    {
        var cleanup = WindowsNativeRuntime.ClearControllerStateFiles(_paths);
        foreach (var file in cleanup.Removed)
        {
            status.Write("WINDOWS_NATIVE_CONTROLLER_STATE_CLEARED", $"{phase}: removed {file} from previous session");
        }

        foreach (var warning in cleanup.Warnings)
        {
            status.Write("WINDOWS_NATIVE_CONTROLLER_STATE_CLEAR_WARN", $"{phase}: could not remove {warning}");
        }
    }

    private async Task<bool> WaitForReceiverStopAsync(StatusWriter status)
    {
        var loggedWait = false;
        for (var attempt = 0; attempt < 40; attempt++)
        {
            var receiverActive = WindowsNativeRuntime.TryGetActiveReceiver(_paths, out var pid, out var controllers);
            var startInProgress = File.Exists(StartLockPath());
            if (!receiverActive && !startInProgress)
            {
                return true;
            }

            if (!loggedWait)
            {
                var detail = receiverActive
                    ? $"receiver pid={pid} controllers={controllers}"
                    : "startup process";
                status.Write("WINDOWS_NATIVE_STOP_WAITING", $"Waiting for {detail}");
                loggedWait = true;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250)).ConfigureAwait(false);
        }

        return !WindowsNativeRuntime.TryGetActiveReceiver(_paths, out _, out _) &&
               !File.Exists(StartLockPath());
    }

    private async Task<bool> TerminateReceiverIfActiveAsync(StatusWriter status)
    {
        if (!WindowsNativeRuntime.TryOpenActiveReceiverProcess(_paths, out var process, out var pid, out var controllers) || process is null)
        {
            status.Write("WINDOWS_NATIVE_STOP_TERMINATE_SKIPPED", "No validated active Windows Native receiver process found");
            return !WindowsNativeRuntime.TryGetActiveReceiver(_paths, out _, out _) &&
                   !File.Exists(StartLockPath());
        }

        using (process)
        {
            status.Write("WINDOWS_NATIVE_STOP_TERMINATE_START", $"Terminating hidden receiver pid={pid} controllers={controllers}");
            try
            {
                process.Kill(entireProcessTree: true);
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                try
                {
                    await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    status.Write("WINDOWS_NATIVE_STOP_TERMINATE_TIMEOUT", $"Receiver pid={pid} did not exit within timeout");
                    return false;
                }

                WindowsNativeRuntime.ClearReadyMarker(_paths);
                status.Write("WINDOWS_NATIVE_STOP_TERMINATE_OK", $"Hidden receiver pid={pid} was terminated");
                return true;
            }
            catch (Exception ex)
            {
                status.Write("WINDOWS_NATIVE_STOP_TERMINATE_WARN", $"Could not terminate receiver pid={pid}: {ex.Message}");
                return false;
            }
        }
    }

    private async Task<bool> RestorePhysicalInputSafelyAsync(HidHideManager hidHide, StatusWriter status)
    {
        using var restoreLock = TryAcquireNamedMutex(
            @"Local\StadiaX.HidHideRestore",
            TimeSpan.FromSeconds(15),
            status,
            "WINDOWS_NATIVE_HIDHIDE_RESTORE_LOCK");
        if (restoreLock is null)
        {
            status.Write("WINDOWS_NATIVE_HIDHIDE_RESTORE_WARN", "Another physical-input restore did not finish within 15 seconds");
            return false;
        }

        try
        {
            return await RestorePhysicalInputAsync(hidHide, status).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            status.Write("WINDOWS_NATIVE_HIDHIDE_RESTORE_WARN", "Physical input restore failed unexpectedly: " + ex.Message);
            status.WritePhase("Windows Native", 5, StartPhaseCount, "Input restore", "WARN", "Could not confirm HidHide cloak restore");
            return false;
        }
    }

    private async Task RollbackPartialHidHideConfigurationAsync(HidHideManager hidHide, StatusWriter status)
    {
        status.Write("WINDOWS_NATIVE_HIDHIDE_ROLLBACK_START", "Disabling the HidHide cloak after a partial input-isolation failure");
        try
        {
            var rollback = await hidHide.DisableCloakAsync(elevated: !IsAdministrator()).ConfigureAwait(false);
            status.Write(
                rollback.ExitCode == 0 ? "WINDOWS_NATIVE_HIDHIDE_ROLLBACK_OK" : "WINDOWS_NATIVE_HIDHIDE_ROLLBACK_WARN",
                $"exit={rollback.ExitCode} output={Shorten(FirstNonEmpty(rollback.Output, rollback.Error, "none"), 260)}");
        }
        catch (Exception ex)
        {
            status.Write("WINDOWS_NATIVE_HIDHIDE_ROLLBACK_WARN", "Unexpected HidHide rollback failure: " + ex.Message);
            AppDiagnosticsLogger.Record("WINDOWS_NATIVE_HIDHIDE_ROLLBACK_FAILED", ("error", ex.Message));
        }
    }

    private async Task<bool> RestorePhysicalInputAsync(HidHideManager hidHide, StatusWriter status)
    {
        if (!hidHide.IsInstalled)
        {
            status.Write("WINDOWS_NATIVE_HIDHIDE_RESTORE_SKIPPED", "HidHide is not installed; no cloak to disable");
            return true;
        }

        status.WritePhase("Windows Native", 5, StartPhaseCount, "Input restore", "START", "Disabling HidHide cloak without prompting for elevation");
        var restore = await hidHide.DisableCloakAsync(elevated: !IsAdministrator()).ConfigureAwait(false);
        status.Write("WINDOWS_NATIVE_HIDHIDE_RESTORE_RESULT", $"exit={restore.ExitCode} output={Shorten(FirstNonEmpty(restore.Output, restore.Error, "none"), 260)}");
        status.WritePhase(
            "Windows Native",
            5,
            StartPhaseCount,
            "Input restore",
            restore.ExitCode == 0 ? "OK" : "WARN",
            restore.ExitCode == 0 ? "Physical Stadia input restored" : "Could not confirm HidHide cloak restore");
        return restore.ExitCode == 0;
    }

    private bool ClearStopSignal(StatusWriter status, DateTimeOffset startRequestedAt)
    {
        var path = StopSignalPath();
        try
        {
            if (File.Exists(path))
            {
                var text = File.ReadLines(path).FirstOrDefault()?.Trim();
                var timestamp = DateTimeOffset.TryParse(text, out var parsed)
                    ? parsed.ToUniversalTime()
                    : new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero);
                if (timestamp >= startRequestedAt)
                {
                    status.Write("WINDOWS_NATIVE_STOP_SIGNAL_PENDING", "Retained Stop request received during startup");
                    return true;
                }

                File.Delete(path);
                status.Write("WINDOWS_NATIVE_STOP_SIGNAL_CLEARED", "Removed windows-native.stop from a previous session");
            }

            return true;
        }
        catch (Exception ex)
        {
            status.Write("WINDOWS_NATIVE_STOP_SIGNAL_CLEAR_WARN", "Could not remove windows-native.stop: " + ex.Message);
            AppDiagnosticsLogger.Record("WINDOWS_NATIVE_STOP_SIGNAL_CLEAR_FAILED", ("error", ex.Message));
            return false;
        }
    }

    private IDisposable? TryAcquireStartLock(StatusWriter status)
    {
        try
        {
            Directory.CreateDirectory(_paths.LogDirectory);
        }
        catch (Exception ex)
        {
            status.Write("WINDOWS_NATIVE_START_LOCK_FAILED", "Could not prepare the start lock directory: " + ex.Message);
            AppDiagnosticsLogger.Record("WINDOWS_NATIVE_START_LOCK_DIRECTORY_FAILED", ("error", ex.Message));
            return null;
        }

        var path = StartLockPath();
        if (File.Exists(path))
        {
            TimeSpan age;
            try
            {
                age = DateTimeOffset.UtcNow - File.GetLastWriteTimeUtc(path);
            }
            catch (Exception ex)
            {
                status.Write("WINDOWS_NATIVE_START_LOCK_FAILED", "Could not inspect the existing start lock: " + ex.Message);
                AppDiagnosticsLogger.Record("WINDOWS_NATIVE_START_LOCK_INSPECT_FAILED", ("path", path), ("error", ex.Message));
                return null;
            }

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

        FileStream? stream = null;
        try
        {
            stream = new FileStream(
                path,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.DeleteOnClose);
            using var writer = new StreamWriter(stream, Encoding.UTF8, 1024, leaveOpen: true);
            writer.WriteLine($"{DateTimeOffset.Now:O}|pid={Environment.ProcessId}");
            writer.Flush();
            stream.Flush();
            status.Write("WINDOWS_NATIVE_START_LOCK_ACQUIRED", $"pid={Environment.ProcessId}");
            return new FileLock(stream);
        }
        catch (Exception ex)
        {
            stream?.Dispose();
            status.Write("WINDOWS_NATIVE_START_LOCK_BUSY", "Could not acquire start lock: " + ex.Message);
            AppDiagnosticsLogger.Record("WINDOWS_NATIVE_START_LOCK_ACQUIRE_FAILED", ("path", path), ("error", ex.Message));
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

    private sealed class MutexLock : IDisposable
    {
        private Mutex? _mutex;

        public MutexLock(Mutex mutex)
        {
            _mutex = mutex;
        }

        public void Dispose()
        {
            var mutex = Interlocked.Exchange(ref _mutex, null);
            if (mutex is null)
            {
                return;
            }

            try { mutex.ReleaseMutex(); } catch (ApplicationException) { }
            mutex.Dispose();
        }
    }
    private sealed class FileLock : IDisposable
    {
        private readonly FileStream _stream;

        public FileLock(FileStream stream)
        {
            _stream = stream;
        }

        public void Dispose()
        {
            _stream.Dispose();
        }
    }
}
