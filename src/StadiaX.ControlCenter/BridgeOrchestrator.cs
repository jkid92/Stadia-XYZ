using System.Diagnostics;
using System.Text;

namespace StadiaX.ControlCenter;

internal sealed class BridgeOrchestrator
{
    private const int StartPhaseCount = 6;
    private const int StopPhaseCount = 3;
    private static readonly TimeSpan ReceiverStopSignalMaxAge = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan StartLockMaxAge = TimeSpan.FromSeconds(60);

    private readonly AppPaths _paths;
    private readonly ProcessRunner _runner;
    private readonly WslDistroResolver _resolver;

    public BridgeOrchestrator(AppPaths paths, ProcessRunner runner)
    {
        _paths = paths;
        _runner = runner;
        _resolver = new WslDistroResolver(paths, runner);
    }

    public async Task<int> StartAsync()
    {
        var status = new StatusWriter(_paths, "start.log");
        status.Reset("START_REQUESTED", "Start requested from StadiaX.exe");
        status.WritePhase("Linux bridge", 1, StartPhaseCount, "Prerequisites", "START", "Checking runtime files and host tools");
        if (TryGetActiveIntegratedReceiver(out var activePid, out var activeDetail))
        {
            ReportAlreadyRunning(status, activePid, activeDetail);
            return 0;
        }

        using var startLock = TryAcquireStartLock(status);
        if (startLock is null)
        {
            status.Write("START_BUSY", "Another Linux bridge start request is already in progress");
            status.WritePhase("Linux bridge", 1, StartPhaseCount, "Prerequisites", "WAIT", "Another start request is already in progress");
            return 2;
        }

        if (TryGetActiveIntegratedReceiver(out activePid, out activeDetail))
        {
            ReportAlreadyRunning(status, activePid, activeDetail);
            return 0;
        }

        if (!ClearReceiverStopSignal(status))
        {
            status.WritePhase("Linux bridge", 1, StartPhaseCount, "Prerequisites", "FAIL", "Could not clear the previous receiver stop signal");
            return 1;
        }
        ClearReceiverReadyMarker(status, "START");
        ClearControllerState(status, "START");

        var missing = RequiredRuntimeFiles().Where(file => !File.Exists(Path.Combine(_paths.Root, file))).ToArray();
        if (missing.Length > 0)
        {
            status.Write("MISSING_RUNTIME", "Missing runtime file(s): " + string.Join(", ", missing));
            status.WritePhase("Linux bridge", 1, StartPhaseCount, "Prerequisites", "FAIL", "Missing runtime file(s): " + string.Join(", ", missing));
            return 1;
        }

        if (!await _runner.CommandExistsAsync("usbipd", _paths.Root).ConfigureAwait(false))
        {
            status.Write("USBIPD_MISSING", "usbipd was not found; launching winget install");
            status.WritePhase("Linux bridge", 1, StartPhaseCount, "Prerequisites", "INSTALL", "usbipd is missing; requesting winget install");
            await _runner.RunAsync("winget", new[] { "install", "usbipd" }, _paths.Root, 120000, createNoWindow: false).ConfigureAwait(false);
            status.Write("RESTART_REQUIRED", "usbipd install was requested; restart Windows before starting again");
            status.WritePhase("Linux bridge", 1, StartPhaseCount, "Prerequisites", "WAIT", "Restart Windows after usbipd install");
            return 2;
        }

        if (!await _runner.CommandExistsAsync("wsl", _paths.Root).ConfigureAwait(false))
        {
            status.Write("WSL_MISSING", "wsl.exe is missing");
            status.WritePhase("Linux bridge", 1, StartPhaseCount, "Prerequisites", "FAIL", "wsl.exe is missing");
            return 1;
        }
        status.WritePhase("Linux bridge", 1, StartPhaseCount, "Prerequisites", "OK", "Runtime files, usbipd, and WSL are present");

        status.WritePhase("Linux bridge", 2, StartPhaseCount, "WSL distro", "START", "Resolving Linux distro");
        var requestedDistro = Environment.GetEnvironmentVariable("STADIA_X_WSL_DISTRO");
        var distro = await _resolver.ResolveAsync(requestedDistro).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(distro))
        {
            status.Write("WSL_DISTRO_MISSING", "No usable WSL distro found; launching Ubuntu install");
            status.WritePhase("Linux bridge", 2, StartPhaseCount, "WSL distro", "INSTALL", "No usable distro found; requesting Ubuntu install");
            await _runner.RunAsync("wsl", new[] { "--install", "-d", "Ubuntu" }, _paths.Root, 120000, createNoWindow: false).ConfigureAwait(false);
            status.Write("RESTART_REQUIRED", "Ubuntu install was requested; restart Windows before starting again");
            status.WritePhase("Linux bridge", 2, StartPhaseCount, "WSL distro", "WAIT", "Restart Windows after Ubuntu install");
            return 2;
        }

        var wslCheck = await _runner.RunAsync("wsl", new[] { "-d", distro, "echo", "ok" }, _paths.Root, 15000).ConfigureAwait(false);
        if (wslCheck.ExitCode != 0)
        {
            status.Write("WSL_DISTRO_START_FAILED", $"WSL distro {distro} did not start correctly");
            status.WritePhase("Linux bridge", 2, StartPhaseCount, "WSL distro", "FAIL", $"WSL distro {distro} did not start correctly");
            return 1;
        }
        status.Write("WSL_DISTRO_SELECTED", $"Using WSL distro {distro}");
        status.WritePhase("Linux bridge", 2, StartPhaseCount, "WSL distro", "OK", $"Using WSL distro {distro}");

        if (!await EnsureKernelAsync(distro, status).ConfigureAwait(false))
        {
            status.WritePhase("Linux bridge", 2, StartPhaseCount, "WSL distro", "FAIL", "WSL USB/HID kernel support is not ready");
            return 1;
        }
        await CleanupOldSessionAsync(distro, status).ConfigureAwait(false);

        status.WritePhase("Linux bridge", 2, StartPhaseCount, "WSL network", "START", $"Starting WSL distro {distro}");
        status.Write("WSL_START", $"Starting WSL distro {distro}");
        await _runner.RunAsync("wsl", new[] { "-d", distro, "echo", "WSL Booted" }, _paths.Root, 15000).ConfigureAwait(false);
        if (!await WaitForWslNetworkAsync(distro).ConfigureAwait(false))
        {
            status.Write("WSL_NETWORK_TIMEOUT", "Timed out waiting for WSL network");
            status.WritePhase("Linux bridge", 2, StartPhaseCount, "WSL network", "FAIL", "Timed out waiting for WSL network");
            return 1;
        }
        status.Write("WSL_NETWORK_READY", "WSL network is ready");
        status.WritePhase("Linux bridge", 2, StartPhaseCount, "WSL network", "OK", "WSL network is ready");

        status.WritePhase("Linux bridge", 3, StartPhaseCount, "Bluetooth adapter", "START", "Resolving Bluetooth adapter BUSID");
        var busId = await ResolveBluetoothBusIdAsync(status).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(busId))
        {
            status.Write("BT_MISSING", "No Bluetooth BUSID was selected or detected");
            status.WritePhase("Linux bridge", 3, StartPhaseCount, "Bluetooth adapter", "FAIL", "No Bluetooth BUSID was selected or auto-detected");
            return 1;
        }
        if (!TryWriteBluetoothSessionFile(busId, status))
        {
            status.WritePhase("Linux bridge", 3, StartPhaseCount, "Bluetooth adapter", "FAIL", "Could not persist the Bluetooth BUSID for safe teardown");
            return 1;
        }
        status.Write("BT_SELECTED", $"Using Bluetooth BUSID {busId}");
        status.WritePhase("Linux bridge", 3, StartPhaseCount, "Bluetooth adapter", "OK", $"Using Bluetooth BUSID {busId}");

        if (!await AttachBluetoothAsync(distro, busId, status).ConfigureAwait(false))
        {
            status.WritePhase("Linux bridge", 3, StartPhaseCount, "Bluetooth adapter", "FAIL", "Could not attach Bluetooth adapter to WSL");
            await RollbackFailedStartAsync(status, "Bluetooth adapter attach").ConfigureAwait(false);
            return 1;
        }

        status.WritePhase("Linux bridge", 4, StartPhaseCount, "Linux core", "START", "Deploying and starting BlueZ bridge");
        if (!await DeployLinuxBridgeAsync(distro, status).ConfigureAwait(false))
        {
            status.WritePhase("Linux bridge", 4, StartPhaseCount, "Linux core", "FAIL", "Could not deploy Linux bridge files");
            await RollbackFailedStartAsync(status, "Linux bridge deployment").ConfigureAwait(false);
            return 1;
        }

        var linuxStarted = await StartLinuxCoreAsync(distro, status).ConfigureAwait(false);
        if (!linuxStarted)
        {
            status.WritePhase("Linux bridge", 4, StartPhaseCount, "Linux core", "FAIL", "Could not start Linux core");
            await RollbackFailedStartAsync(status, "Linux core launch").ConfigureAwait(false);
            return 1;
        }
        status.WritePhase("Linux bridge", 4, StartPhaseCount, "Linux core", "OK", "Linux core launch requested");

        status.WritePhase("Linux bridge", 5, StartPhaseCount, "Controller discovery", "START", "Waiting for BlueZ pairing and input readiness");
        await Task.Delay(TimeSpan.FromSeconds(8)).ConfigureAwait(false);
        var wslIp = await GetWslIpAsync(distro).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(wslIp))
        {
            wslIp = "127.0.0.1";
            status.Write("WSL_IP_FALLBACK", "Could not detect WSL IP; using 127.0.0.1");
            status.WritePhase("Linux bridge", 5, StartPhaseCount, "Controller discovery", "WARN", "Could not detect WSL IP; using 127.0.0.1");
        }
        else
        {
            status.Write("WSL_IP_READY", $"Detected WSL IP {wslIp}");
            status.WritePhase("Linux bridge", 5, StartPhaseCount, "Controller discovery", "OK", $"Detected WSL IP {wslIp}");
        }

        status.WritePhase("Linux bridge", 6, StartPhaseCount, "Windows receiver", "START", "Starting integrated Windows receiver");
        status.Write("RECEIVER_START", "Starting Windows receiver");
        var receiverExit = await RunIntegratedReceiverAndStopAsync(wslIp, status).ConfigureAwait(false);
        status.WritePhase("Linux bridge", 6, StartPhaseCount, "Windows receiver", receiverExit == 0 ? "OK" : "FAIL", $"Integrated receiver exited with code {receiverExit}");
        return receiverExit;
    }

    public async Task<int> StopAsync()
    {
        var status = new StatusWriter(_paths, "teardown.log");
        var warnings = false;
        var receiverWasActive = TryGetActiveIntegratedReceiver(out _, out _);
        var busFile = Path.Combine(_paths.Root, "bt_busid.txt");
        var hadBusSession = File.Exists(busFile);
        status.WritePhase("Linux bridge", 1, StopPhaseCount, "Stop receiver", "START", "Stopping receiver and Linux session");
        status.Write("STOP_START", "Stopping Stadia X and restoring Bluetooth");

        var stopSignaled = SignalReceiverStop(status);
        status.Write(
            stopSignaled ? "RECEIVER_STOP_SIGNAL" : "RECEIVER_STOP_SIGNAL_FALLBACK",
            stopSignaled ? "Stop signal written; waiting for receiver shutdown" : "Stop signal could not be written; termination fallback may be required");
        warnings |= !stopSignaled && (receiverWasActive || hadBusSession);
        ClearControllerState(status, "STOP");
        KillProcess("stadia_receiver");
        KillProcess("stadia-vigem-x86");

        var receiverStopped = await WaitForIntegratedReceiverStopAsync(status).ConfigureAwait(false);
        if (!receiverStopped)
        {
            receiverStopped = await TerminateIntegratedReceiverIfActiveAsync(status).ConfigureAwait(false);
        }

        var wslShutdown = await _runner.RunAsync("wsl", new[] { "--shutdown" }, _paths.Root, 20000).ConfigureAwait(false);
        var wslStopped = wslShutdown.ExitCode == 0;
        status.Write(
            wslStopped ? "WSL_SHUTDOWN_OK" : "WSL_SHUTDOWN_WARN",
            $"exit={wslShutdown.ExitCode} detail={Shorten(FirstNonEmpty(wslShutdown.Error, wslShutdown.Output, "none"), 220)}");
        warnings |= !receiverStopped || !wslStopped;
        await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        status.WritePhase(
            "Linux bridge",
            1,
            StopPhaseCount,
            "Stop receiver",
            receiverStopped && wslStopped ? "OK" : "WARN",
            receiverStopped && wslStopped
                ? "Receiver stopped and WSL shutdown completed"
                : $"Receiver stopped={receiverStopped}; WSL shutdown completed={wslStopped}");

        status.WritePhase("Linux bridge", 2, StopPhaseCount, "Restore Bluetooth", "START", "Resolving adapter to detach");
        var busId = await ResolveBusIdForDetachAsync(status).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(busId))
        {
            var restoreExpected = receiverWasActive || hadBusSession;
            status.Write(
                restoreExpected ? "BT_RESTORE_UNKNOWN" : "BT_RESTORE_NOT_NEEDED",
                restoreExpected ? "No Bluetooth BUSID found for detach" : "No active Bluetooth adapter session was recorded");
            status.WritePhase(
                "Linux bridge",
                2,
                StopPhaseCount,
                "Restore Bluetooth",
                restoreExpected ? "WARN" : "OK",
                restoreExpected ? "No Bluetooth BUSID found for detach" : "No Bluetooth adapter session required restore");
            warnings |= restoreExpected;
        }
        else
        {
            status.Write("BT_RESTORE_START", $"Releasing Bluetooth BUSID {busId}");
            var detach = await _runner.RunAsync("usbipd", new[] { "detach", "--busid", busId }, _paths.Root, 15000).ConfigureAwait(false);
            status.Write("BT_RESTORE_DETACH_RESULT", $"exit={detach.ExitCode} detail={Shorten(FirstNonEmpty(detach.Error, detach.Output, "none"), 240)}");
            await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
            var unbind = await _runner.RunAsync("usbipd", new[] { "unbind", "--busid", busId }, _paths.Root, 15000).ConfigureAwait(false);
            CommandResult? forceUnbind = null;
            if (await SupportsForceUnbindAsync().ConfigureAwait(false))
            {
                forceUnbind = await _runner.RunAsync("usbipd", new[] { "unbind", "--busid", busId, "--force" }, _paths.Root, 15000).ConfigureAwait(false);
            }

            status.Write(
                "BT_RESTORE_UNBIND_RESULT",
                $"exit={unbind.ExitCode} forceExit={(forceUnbind is null ? "skipped" : forceUnbind.ExitCode)} detail={Shorten(FirstNonEmpty(forceUnbind?.Error, forceUnbind?.Output, unbind.Error, unbind.Output, "none"), 240)}");
            var restoreLooksOk = detach.ExitCode == 0 || unbind.ExitCode == 0 || forceUnbind?.ExitCode == 0;
            status.Write(restoreLooksOk ? "BT_RESTORE_OK" : "BT_RESTORE_WARN", "Bluetooth adapter restore commands completed");
            status.WritePhase("Linux bridge", 2, StopPhaseCount, "Restore Bluetooth", restoreLooksOk ? "OK" : "WARN", $"Bluetooth BUSID {busId} restore commands completed");
            if (restoreLooksOk)
            {
                TryDeleteSessionFile(busFile, status, "BT_RESTORE_SESSION_FILE");
            }
            else
            {
                warnings = true;
                status.Write("BT_RESTORE_SESSION_FILE_PRESERVED", "Kept bt_busid.txt so Bluetooth restore can be retried");
            }
        }

        await Task.Delay(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
        status.Write(warnings ? "STOP_DONE_WARN" : "STOP_DONE", warnings ? "Teardown completed with warnings" : "Teardown complete");
        status.WritePhase("Linux bridge", 3, StopPhaseCount, "Teardown", warnings ? "WARN" : "OK", warnings ? "Teardown completed with warnings" : "Teardown complete");
        return warnings ? 1 : 0;
    }

    private static IEnumerable<string> RequiredRuntimeFiles()
    {
        yield return "start.sh";
        yield return "stadia_bridge";
        yield return "ViGEmClient.dll";
    }

    private async Task<bool> EnsureKernelAsync(string distro, StatusWriter status)
    {
        status.Write("WSL_KERNEL_CHECK", "Checking WSL USB/HID kernel support");
        var kernel = await _runner.RunAsync("wsl", new[] { "-d", distro, "-u", "root", "bash", "-lc", "modprobe vhci-hcd 2>/dev/null; lsmod | grep -q vhci_hcd" }, _paths.Root, 20000).ConfigureAwait(false);
        if (kernel.ExitCode == 0)
        {
            status.Write("WSL_KERNEL_OK", "WSL kernel already supports USB HID");
            return true;
        }

        var sourceKernel = Path.Combine(_paths.Root, "build", "wsl_kernel");
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(userProfile))
        {
            status.Write("WSL_KERNEL_DEPLOY_FAILED", "Could not resolve the Windows user profile directory");
            return false;
        }

        var targetKernel = Path.Combine(userProfile, "wsl_kernel");
        if (!File.Exists(targetKernel))
        {
            if (!File.Exists(sourceKernel))
            {
                status.Write("WSL_KERNEL_MISSING", "Custom WSL kernel is required but build/wsl_kernel is missing");
                return false;
            }

            try
            {
                File.Copy(sourceKernel, targetKernel, overwrite: true);
                status.Write("WSL_KERNEL_COPIED", "Copied the custom WSL kernel to the Windows user profile");
            }
            catch (Exception ex)
            {
                status.Write("WSL_KERNEL_DEPLOY_FAILED", "Could not copy the custom WSL kernel: " + ex.Message);
                AppDiagnosticsLogger.Record("WSL_KERNEL_COPY_FAILED", ("source", sourceKernel), ("target", targetKernel), ("error", ex.Message));
                return false;
            }
        }

        var wslConfig = Path.Combine(userProfile, ".wslconfig");
        var backup = Path.Combine(userProfile, ".wslconfig.stadia-x.bak");
        var kernelSetting = $"kernel={EscapeWslConfigPath(targetKernel)}";
        try
        {
            var existingConfig = File.Exists(wslConfig) ? File.ReadAllText(wslConfig) : "";
            var kernelConfigured = existingConfig
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Any(line => string.Equals(line.Trim(), kernelSetting, StringComparison.OrdinalIgnoreCase));
            if (!kernelConfigured)
            {
                if (File.Exists(wslConfig) && !File.Exists(backup))
                {
                    File.Copy(wslConfig, backup);
                    status.Write("WSL_CONFIG_BACKUP", "Backed up existing .wslconfig");
                }

                File.WriteAllText(wslConfig, "[wsl2]\n" +
                                            kernelSetting + "\n" +
                                            "memory=800MB\nprocessors=2\nswap=800MB\n");
                status.Write("WSL_KERNEL_CONFIGURED", "Configured .wslconfig to use the custom WSL kernel");
            }
        }
        catch (Exception ex)
        {
            status.Write("WSL_KERNEL_DEPLOY_FAILED", "Could not configure the custom WSL kernel: " + ex.Message);
            AppDiagnosticsLogger.Record("WSL_KERNEL_CONFIG_FAILED", ("config", wslConfig), ("error", ex.Message));
            return false;
        }

        status.Write("WSL_KERNEL_DEPLOY", "Custom WSL kernel is configured; restarting WSL");
        var shutdown = await _runner.RunAsync("wsl", new[] { "--shutdown" }, _paths.Root, 20000).ConfigureAwait(false);
        if (shutdown.ExitCode != 0)
        {
            status.Write("WSL_KERNEL_RESTART_FAILED", "Could not restart WSL after configuring the custom kernel: " + Shorten(shutdown.Error, 240));
            return false;
        }

        await Task.Delay(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
        return true;
    }

    private async Task CleanupOldSessionAsync(string distro, StatusWriter status)
    {
        SignalReceiverStop(status);
        var legacyReceiverRunning = Process.GetProcessesByName("stadia_receiver").Length > 0;
        var linuxSessionRunning = false;
        if (!string.IsNullOrWhiteSpace(distro))
        {
            var probe = await _runner.RunAsync(
                "wsl",
                new[] { "-d", distro, "-u", "root", "bash", "-lc", "pgrep -x stadia_bridge >/dev/null || pgrep -f '^/bin/bash /opt/stadia-x/start.sh' >/dev/null" },
                _paths.Root,
                10000).ConfigureAwait(false);
            linuxSessionRunning = probe.ExitCode == 0;
        }

        if (!legacyReceiverRunning && !linuxSessionRunning)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(750)).ConfigureAwait(false);
            ClearReceiverStopSignal(status);
            return;
        }

        status.Write("CLEANUP_OLD_SESSION", "Stopping existing receiver session");
        KillProcess("stadia_receiver");
        if (linuxSessionRunning)
        {
            await _runner.RunAsync(
                "wsl",
                new[] { "-d", distro, "-u", "root", "bash", "-lc", "pkill -TERM -x stadia_bridge 2>/dev/null || true; pkill -TERM -f '^/bin/bash /opt/stadia-x/start.sh' 2>/dev/null || true" },
                _paths.Root,
                10000).ConfigureAwait(false);
        }
        await _runner.RunAsync("wsl", new[] { "--shutdown" }, _paths.Root, 20000).ConfigureAwait(false);
        await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        ClearReceiverStopSignal(status);
    }

    private async Task<bool> WaitForWslNetworkAsync(string distro)
    {
        for (var attempt = 0; attempt < 30; attempt++)
        {
            var result = await _runner.RunAsync("wsl", new[] { "-d", distro, "bash", "-lc", "ip addr show eth0 2>/dev/null | grep -q 'inet '" }, _paths.Root, 8000).ConfigureAwait(false);
            if (result.ExitCode == 0)
            {
                return true;
            }
            await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        }

        return false;
    }

    private async Task<string> ResolveBluetoothBusIdAsync(StatusWriter status)
    {
        var list = await _runner.RunAsync("usbipd", new[] { "list" }, _paths.Root, 15000).ConfigureAwait(false);
        status.Write("BT_DETECT_LIST_RESULT", $"exit={list.ExitCode} bytes={list.Output.Length}");
        var usbipdDevices = ParseUsbipdList(list.Output).ToArray();
        var canVerifyUsbipd = list.ExitCode == 0 && usbipdDevices.Length > 0;

        var requested = Environment.GetEnvironmentVariable("STADIA_X_BT_BUSID");
        if (IsBusId(requested))
        {
            var requestedDevice = FindUsbipdBus(usbipdDevices, requested!);
            if (!canVerifyUsbipd || requestedDevice is not null)
            {
                status.Write(
                    requestedDevice is not null && !LooksLikeBluetoothUsbipdLine(requestedDevice.Line) ? "BT_SELECTED_SOURCE_WARN" : "BT_SELECTED_SOURCE",
                    requestedDevice is not null
                        ? $"Using Bluetooth BUSID {requested} from STADIA_X_BT_BUSID ({Shorten(requestedDevice.Line, 180)})"
                        : $"Using Bluetooth BUSID {requested} from STADIA_X_BT_BUSID without usbipd list verification");
                return requested!;
            }

            status.Write("BT_SELECTED_SOURCE_IGNORED", $"Ignoring STADIA_X_BT_BUSID {requested}; BUSID was not found in usbipd list");
        }
        else if (!string.IsNullOrWhiteSpace(requested))
        {
            status.Write("BT_SELECTED_SOURCE_IGNORED", "Ignoring invalid STADIA_X_BT_BUSID value");
        }

        if (File.Exists(_paths.SelectedBluetoothBusId))
        {
            var saved = File.ReadAllText(_paths.SelectedBluetoothBusId).Trim();
            if (IsBusId(saved))
            {
                var savedDevice = FindUsbipdBus(usbipdDevices, saved);
                if (!canVerifyUsbipd || (savedDevice is not null && LooksLikeBluetoothUsbipdLine(savedDevice.Line)))
                {
                    status.Write(
                        "BT_SELECTED_SOURCE",
                        savedDevice is not null
                            ? $"Using saved Bluetooth BUSID {saved} ({Shorten(savedDevice.Line, 180)})"
                            : $"Using saved Bluetooth BUSID {saved} without usbipd list verification");
                    return saved;
                }

                status.Write(
                    "BT_SELECTED_SOURCE_IGNORED",
                    savedDevice is null
                        ? $"Ignoring saved BUSID {saved}; BUSID was not found in usbipd list"
                        : $"Ignoring saved BUSID {saved}; usbipd no longer reports it as a Bluetooth adapter");
            }
            else if (!string.IsNullOrWhiteSpace(saved))
            {
                status.Write("BT_SELECTED_SOURCE_IGNORED", "Ignoring invalid saved Bluetooth BUSID");
            }
        }

        foreach (var device in usbipdDevices)
        {
            if (LooksLikeBluetoothUsbipdLine(device.Line))
            {
                status.Write("BT_SELECTED_SOURCE", $"Auto-detected Bluetooth BUSID {device.BusId} ({Shorten(device.Line, 180)})");
                return device.BusId;
            }
        }

        status.Write("BT_DETECT_FAILED", "Could not auto-detect Bluetooth adapter from usbipd list");
        return "";
    }

    private static IReadOnlyList<UsbipdListEntry> ParseUsbipdList(string output)
    {
        var devices = new List<UsbipdListEntry>();
        foreach (var rawLine in output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            var first = line.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (IsBusId(first))
            {
                devices.Add(new UsbipdListEntry(first!, line));
            }
        }

        return devices;
    }

    private static UsbipdListEntry? FindUsbipdBus(IReadOnlyList<UsbipdListEntry> devices, string busId)
    {
        return devices.FirstOrDefault(device => device.BusId.Equals(busId, StringComparison.OrdinalIgnoreCase));
    }

    private static bool LooksLikeBluetoothUsbipdLine(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        var lower = line.ToLowerInvariant();
        return lower.Contains("bluetooth") ||
               lower.Contains("intel wireless") ||
               lower.Contains("realtek") ||
               lower.Contains("mediatek") ||
               lower.Contains("qualcomm");
    }

    private sealed record UsbipdListEntry(string BusId, string Line);

    private async Task<bool> AttachBluetoothAsync(string distro, string busId, StatusWriter status)
    {
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            status.Write("BT_ATTACH_START", $"Attempt {attempt}/3 attaching Bluetooth BUSID {busId} to WSL distro {distro}");
            var bind = await _runner.RunAsync("usbipd", new[] { "bind", "--busid", busId, "--force" }, _paths.Root, 20000).ConfigureAwait(false);
            status.Write("BT_ATTACH_BIND_RESULT", $"attempt={attempt} exit={bind.ExitCode} detail={Shorten(FirstNonEmpty(bind.Error, bind.Output, "none"), 240)}");
            var help = await _runner.RunAsync("usbipd", new[] { "attach", "--help" }, _paths.Root, 15000).ConfigureAwait(false);
            var attachArgs = help.Output.Contains("--distribution", StringComparison.OrdinalIgnoreCase)
                ? new[] { "attach", "--wsl", "--busid", busId, "--distribution", distro }
                : help.Output.Contains("<[DISTRIBUTION]>", StringComparison.OrdinalIgnoreCase)
                    ? new[] { "attach", "--wsl", distro, "--busid", busId }
                    : new[] { "attach", "--wsl", "--busid", busId };
            status.Write("BT_ATTACH_ARGS", $"attempt={attempt} args={string.Join(" ", attachArgs)}");
            var attach = await _runner.RunAsync("usbipd", attachArgs, _paths.Root, 30000, createNoWindow: false).ConfigureAwait(false);
            if (attach.ExitCode != 0 && attachArgs.Any(arg => arg.Equals("--distribution", StringComparison.OrdinalIgnoreCase)))
            {
                status.Write("BT_ATTACH_DISTRO_FALLBACK", $"Attach with explicit distro failed: {Shorten(FirstNonEmpty(attach.Error, attach.Output, "none"), 240)}");
                attach = await _runner.RunAsync("usbipd", new[] { "attach", "--wsl", "--busid", busId }, _paths.Root, 30000, createNoWindow: false).ConfigureAwait(false);
            }
            status.Write("BT_ATTACH_RESULT", $"attempt={attempt} exit={attach.ExitCode} detail={Shorten(FirstNonEmpty(attach.Error, attach.Output, "none"), 240)}");
            if (attach.ExitCode == 0)
            {
                if (await WaitForBluetoothAttachmentAsync(distro, busId, status).ConfigureAwait(false))
                {
                    status.Write("BT_ATTACH_OK", "Bluetooth adapter attachment was confirmed");
                    return true;
                }

                status.Write("BT_ATTACH_UNCONFIRMED", $"Attach command succeeded on attempt {attempt}, but the adapter did not become visible");
                var detach = await _runner.RunAsync("usbipd", new[] { "detach", "--busid", busId }, _paths.Root, 15000).ConfigureAwait(false);
                status.Write("BT_ATTACH_RETRY_DETACH", $"attempt={attempt} exit={detach.ExitCode} detail={Shorten(FirstNonEmpty(detach.Error, detach.Output, "none"), 180)}");
            }

            status.Write("BT_ATTACH_RETRY", $"Bluetooth attachment was not ready on attempt {attempt}; retrying after cleanup delay");
            await Task.Delay(TimeSpan.FromSeconds(4)).ConfigureAwait(false);
        }

        status.Write("BT_ATTACH_FAILED", "Could not attach Bluetooth adapter to WSL");
        return false;
    }

    private async Task<bool> WaitForBluetoothAttachmentAsync(string distro, string busId, StatusWriter status)
    {
        for (var verifyAttempt = 1; verifyAttempt <= 5; verifyAttempt++)
        {
            var verify = await _runner.RunAsync("usbipd", new[] { "list" }, _paths.Root, 15000).ConfigureAwait(false);
            var line = verify.Output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault(value => value.TrimStart().StartsWith(busId + " ", StringComparison.OrdinalIgnoreCase));
            var hostAttached = line is not null && line.Contains("Attached", StringComparison.OrdinalIgnoreCase);
            var wslReady = await VerifyBluetoothInsideWslAsync(distro, status, verifyAttempt).ConfigureAwait(false);
            if (hostAttached || wslReady)
            {
                status.Write(
                    "BT_ATTACH_VERIFY_OK",
                    hostAttached
                        ? $"usbipd reports BUSID {busId} as attached"
                        : $"WSL reports a Bluetooth HCI device for BUSID {busId}");
                return true;
            }

            status.Write("BT_ATTACH_VERIFY_WAIT", $"Confirmation {verifyAttempt}/5 pending for Bluetooth BUSID {busId}");
            if (verifyAttempt < 5)
            {
                await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            }
        }

        status.Write("BT_ATTACH_VERIFY_WARN", $"Bluetooth BUSID {busId} was not confirmed by usbipd or WSL");
        return false;
    }

    private async Task<bool> VerifyBluetoothInsideWslAsync(string distro, StatusWriter status, int attempt)
    {
        var probe = await _runner.RunAsync(
            "wsl",
            new[] { "-d", distro, "-u", "root", "bash", "-lc", "if compgen -G '/sys/class/bluetooth/hci*' >/dev/null; then echo STADIAX_HCI_READY; fi; if command -v bluetoothctl >/dev/null 2>&1; then timeout 6s bluetoothctl list 2>/dev/null || timeout 6s bluetoothctl show 2>/dev/null || true; elif command -v hciconfig >/dev/null 2>&1; then hciconfig -a 2>/dev/null || true; fi" },
            _paths.Root,
            10000).ConfigureAwait(false);

        var detail = Shorten(FirstNonEmpty(probe.Output, probe.Error, "no Bluetooth controller reported yet"), 260);
        var looksReady = probe.Output.Contains("STADIAX_HCI_READY", StringComparison.Ordinal) ||
                         probe.Output.Contains("Controller", StringComparison.OrdinalIgnoreCase) ||
                         probe.Output.Contains("hci", StringComparison.OrdinalIgnoreCase);
        status.Write(looksReady ? "BT_WSL_PROBE_OK" : "BT_WSL_PROBE_WAIT", $"attempt={attempt}/5 detail={detail}");
        return looksReady;
    }

    private async Task<bool> DeployLinuxBridgeAsync(string distro, StatusWriter status)
    {
        status.Write("DEPLOY_START", "Deploying Linux bridge files");
        var wslRoot = await ConvertToWslPathAsync(distro, _paths.Root).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(wslRoot))
        {
            status.Write("DEPLOY_FAILED", "Could not convert install path to WSL path");
            return false;
        }

        var script = "mkdir -p /opt/stadia-x && " +
                     $"cp {BashQuote(wslRoot + "/start.sh")} /opt/stadia-x/start.sh && " +
                     $"cp {BashQuote(wslRoot + "/stadia_bridge")} /opt/stadia-x/stadia_bridge && " +
                     "sed -i 's/\\r//g' /opt/stadia-x/start.sh && chmod +x /opt/stadia-x/start.sh /opt/stadia-x/stadia_bridge";
        var result = await _runner.RunAsync("wsl", new[] { "-d", distro, "-u", "root", "bash", "-lc", script }, _paths.Root, 30000).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            status.Write("DEPLOY_FAILED", (result.Error + result.Output).Trim());
            return false;
        }

        status.Write("DEPLOY_OK", "Linux bridge files deployed");
        return true;
    }

    private async Task<bool> StartLinuxCoreAsync(string distro, StatusWriter status)
    {
        status.Write("LINUX_START", "Starting Linux core");
        var wslLogDir = await ConvertToWslPathAsync(distro, _paths.LogDirectory).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(wslLogDir))
        {
            status.Write("LINUX_START_FAILED", "Could not convert log path to WSL path");
            return false;
        }

        Directory.CreateDirectory(_paths.LogDirectory);
        var controllerMacs = ReadSelectedControllerMacs();
        var linuxLog = wslLogDir + "/linux.log";
        var command = $"STADIA_X_STATUS_LOG={BashQuote(wslLogDir + "/linux-status.log")} " +
                      $"STADIA_X_LINUX_LOG={BashQuote(linuxLog)} " +
                      $"STADIA_X_BT_DIAG_LOG={BashQuote(wslLogDir + "/bluetooth-diagnostics.txt")} " +
                      $"STADIA_X_CONTROLLER_MACS={BashQuote(controllerMacs)} " +
                      $"/opt/stadia-x/start.sh 2>&1 | tee -a {BashQuote(linuxLog)}";

        var startInfo = new ProcessStartInfo
        {
            FileName = "wsl.exe",
            WorkingDirectory = _paths.Root,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var arg in new[] { "-d", distro, "-u", "root", "bash", "-lc", command })
        {
            startInfo.ArgumentList.Add(arg);
        }

        try
        {
            var process = Process.Start(startInfo);
            if (process is null)
            {
                status.Write("LINUX_START_FAILED", "wsl.exe did not return a process handle");
                return false;
            }

            status.Write("LINUX_START_OK", $"Linux core launch requested pid={process.Id}");
            process.Dispose();
            return true;
        }
        catch (Exception ex)
        {
            status.Write("LINUX_START_FAILED", "Could not launch Linux core: " + ex.Message);
            return false;
        }
    }

    private async Task<string> GetWslIpAsync(string distro)
    {
        var result = await _runner.RunAsync("wsl", new[] { "-d", distro, "bash", "-lc", "hostname -I 2>/dev/null" }, _paths.Root, 10000).ConfigureAwait(false);
        return result.Output.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? "";
    }

    private async Task<int> RunIntegratedReceiverAndStopAsync(string wslIp, StatusWriter status)
    {
        try
        {
            Directory.CreateDirectory(_paths.LogDirectory);
            await File.AppendAllTextAsync(_paths.ReceiverLog, $"[{DateTime.Now}] Starting integrated receiver for {wslIp}{Environment.NewLine}").ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            status.Write("RECEIVER_LOG_WRITE_WARN", "Could not append the integrated receiver startup log: " + ex.Message);
            AppDiagnosticsLogger.Record("RECEIVER_START_LOG_WRITE_WARN", ("error", ex.Message));
        }

        using var cancellation = new CancellationTokenSource();
        using var stopWatcher = StartReceiverStopWatcher(cancellation);
        var receiver = new IntegratedReceiver(_paths, wslIp, status);
        var exitCode = 1;
        var teardownSucceeded = false;
        try
        {
            exitCode = await receiver.RunAsync(cancellation.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            status.Write("RECEIVER_FAILED", ex.Message);
            exitCode = 1;
        }
        finally
        {
            status.Write("RECEIVER_EXITED", $"Integrated receiver exited with code {exitCode}");
            teardownSucceeded = await StopBridgeSafelyAsync(status).ConfigureAwait(false);
        }

        if (exitCode == 0 && !teardownSucceeded)
        {
            status.Write("RECEIVER_EXIT_TEARDOWN_FAILED", "Receiver stopped, but Linux bridge teardown did not complete cleanly");
            return 1;
        }

        return exitCode;
    }

    private async Task<bool> StopBridgeSafelyAsync(StatusWriter status)
    {
        try
        {
            var exitCode = await StopAsync().ConfigureAwait(false);
            if (exitCode != 0)
            {
                status.Write("STOP_AFTER_RECEIVER_WARN", $"Bridge teardown after receiver exit completed with code {exitCode}");
            }

            return exitCode == 0;
        }
        catch (Exception ex)
        {
            status.Write("STOP_AFTER_RECEIVER_WARN", "Bridge teardown after receiver exit failed: " + ex.Message);
            AppDiagnosticsLogger.Record("STOP_AFTER_RECEIVER_FAILED", ("error", ex.Message));
            return false;
        }
    }

    private async Task RollbackFailedStartAsync(StatusWriter status, string stage)
    {
        status.Write("START_ROLLBACK", $"Rolling back partial start after failure during {stage}");
        try
        {
            var exitCode = await StopAsync().ConfigureAwait(false);
            status.Write(
                exitCode == 0 ? "START_ROLLBACK_OK" : "START_ROLLBACK_WARN",
                $"Partial start rollback after {stage} exited with code {exitCode}");
        }
        catch (Exception ex)
        {
            status.Write("START_ROLLBACK_WARN", $"Partial start rollback after {stage} failed: {ex.Message}");
            AppDiagnosticsLogger.Record("START_ROLLBACK_FAILED", ("stage", stage), ("error", ex.Message));
        }
    }

    private static void ReportAlreadyRunning(StatusWriter status, int pid, string detail)
    {
        status.Write("BRIDGE_ALREADY_RUNNING", $"Integrated receiver is already active pid={pid} detail={detail}");
        status.Write("BRIDGE_READY", $"Linux bridge input is already running pid={pid}");
        status.WritePhase("Linux bridge", 6, StartPhaseCount, "Windows receiver", "OK", $"Integrated receiver already active pid={pid}");
    }

    private IDisposable? TryAcquireStartLock(StatusWriter status)
    {
        try
        {
            Directory.CreateDirectory(_paths.LogDirectory);
        }
        catch (Exception ex)
        {
            status.Write("START_LOCK_FAILED", "Could not prepare the start lock directory: " + ex.Message);
            AppDiagnosticsLogger.Record("START_LOCK_DIRECTORY_FAILED", ("error", ex.Message));
            return null;
        }

        var path = Path.Combine(_paths.LogDirectory, "bridge.start.lock");
        if (File.Exists(path))
        {
            TimeSpan age;
            try
            {
                age = DateTimeOffset.UtcNow - File.GetLastWriteTimeUtc(path);
            }
            catch (Exception ex)
            {
                status.Write("START_LOCK_FAILED", "Could not inspect the existing start lock: " + ex.Message);
                AppDiagnosticsLogger.Record("START_LOCK_INSPECT_FAILED", ("path", path), ("error", ex.Message));
                return null;
            }

            if (age <= StartLockMaxAge)
            {
                status.Write("START_LOCK_BUSY", $"Existing start lock ageSeconds={(int)age.TotalSeconds}");
                return null;
            }

            try
            {
                File.Delete(path);
                status.Write("START_LOCK_STALE", $"Removed stale start lock ageSeconds={(int)age.TotalSeconds}");
            }
            catch (Exception ex)
            {
                status.Write("START_LOCK_FAILED", "Could not remove stale start lock: " + ex.Message);
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
            status.Write("START_LOCK_ACQUIRED", $"pid={Environment.ProcessId}");
            return new FileLock(stream);
        }
        catch (Exception ex)
        {
            stream?.Dispose();
            status.Write("START_LOCK_BUSY", "Could not acquire start lock: " + ex.Message);
            AppDiagnosticsLogger.Record("START_LOCK_ACQUIRE_FAILED", ("path", path), ("error", ex.Message));
            return null;
        }
    }

    private bool TryWriteBluetoothSessionFile(string busId, StatusWriter status)
    {
        var path = Path.Combine(_paths.Root, "bt_busid.txt");
        try
        {
            File.WriteAllText(path, busId + Environment.NewLine);
            return true;
        }
        catch (Exception ex)
        {
            status.Write("BT_SESSION_FILE_WRITE_FAILED", "Could not write bt_busid.txt: " + ex.Message);
            AppDiagnosticsLogger.Record("BT_SESSION_FILE_WRITE_FAILED", ("path", path), ("busId", busId), ("error", ex.Message));
            return false;
        }
    }

    private System.Threading.Timer StartReceiverStopWatcher(CancellationTokenSource cancellation)
    {
        return new System.Threading.Timer(_ =>
        {
            try
            {
                if (ReceiverStopSignalIsActive())
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

    private bool SignalReceiverStop(StatusWriter? status = null)
    {
        try
        {
            Directory.CreateDirectory(_paths.LogDirectory);
            File.WriteAllText(ReceiverStopSignalPath(), DateTimeOffset.Now.ToString("O") + Environment.NewLine);
            return true;
        }
        catch (Exception ex)
        {
            status?.Write("RECEIVER_STOP_SIGNAL_WARN", "Could not write receiver.stop: " + ex.Message);
            AppDiagnosticsLogger.Record("RECEIVER_STOP_SIGNAL_WRITE_WARN", ("error", ex.Message));
            return false;
        }
    }

    private bool ClearReceiverStopSignal(StatusWriter status)
    {
        var path = ReceiverStopSignalPath();
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
                status.Write("RECEIVER_STOP_SIGNAL_CLEARED", "Removed receiver.stop from a previous session");
            }

            return true;
        }
        catch (Exception ex)
        {
            status.Write("RECEIVER_STOP_SIGNAL_CLEAR_WARN", "Could not remove receiver.stop: " + ex.Message);
            AppDiagnosticsLogger.Record("RECEIVER_STOP_SIGNAL_CLEAR_FAILED", ("error", ex.Message));
            return false;
        }
    }

    private async Task<bool> WaitForIntegratedReceiverStopAsync(StatusWriter status)
    {
        for (var i = 0; i < 20; i++)
        {
            if (!TryGetActiveIntegratedReceiver(out _, out _))
            {
                status.Write("RECEIVER_STOP_CONFIRMED", "Integrated receiver marker is clear");
                return true;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250)).ConfigureAwait(false);
        }

        if (TryGetActiveIntegratedReceiver(out var pid, out var detail))
        {
            status.Write("RECEIVER_STOP_TIMEOUT", $"Integrated receiver still active after stop signal: pid={pid} detail={detail}");
            return false;
        }

        status.Write("RECEIVER_STOP_CONFIRMED", "Integrated receiver marker cleared after timeout window");
        return true;
    }

    private async Task<bool> TerminateIntegratedReceiverIfActiveAsync(StatusWriter status)
    {
        if (!TryGetActiveIntegratedReceiver(out var pid, out var detail))
        {
            status.Write("RECEIVER_STOP_TERMINATE_SKIPPED", "No active integrated receiver marker found");
            return true;
        }

        status.Write("RECEIVER_STOP_TERMINATE_START", $"Terminating hidden integrated receiver pid={pid} detail={detail}");
        try
        {
            using var process = Process.GetProcessById(pid);
            var processStart = ReadReceiverReadyProcessStart(out var hasExactProcessStart);
            if (!LooksLikeIntegratedReceiverProcess(process) || !ProcessMatchesReadyMarker(process, processStart, hasExactProcessStart))
            {
                status.Write("RECEIVER_STOP_TERMINATE_REFUSED", $"Receiver marker pid={pid} no longer matches a hidden StadiaX receiver");
                return false;
            }

            process.Kill(entireProcessTree: true);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            try
            {
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                status.Write("RECEIVER_STOP_TERMINATE_TIMEOUT", $"Receiver pid={pid} did not exit within timeout");
                return false;
            }

            ClearReceiverReadyMarker(status, "STOP");
            status.Write("RECEIVER_STOP_TERMINATE_OK", $"Hidden integrated receiver pid={pid} was terminated");
            return true;
        }
        catch (ArgumentException)
        {
            ClearReceiverReadyMarker(status, "STOP");
            status.Write("RECEIVER_STOP_TERMINATE_OK", $"Receiver pid={pid} already exited");
            return true;
        }
        catch (Exception ex)
        {
            status.Write("RECEIVER_STOP_TERMINATE_WARN", $"Could not terminate receiver pid={pid}: {ex.Message}");
            return false;
        }
    }

    private bool TryGetActiveIntegratedReceiver(out int pid, out string detail)
    {
        pid = 0;
        detail = "no marker";
        var path = ReceiverReadyMarkerPath();
        if (!File.Exists(path))
        {
            return false;
        }

        DateTimeOffset processStart;
        bool hasExactProcessStart;
        try
        {
            processStart = ReadReceiverReadyProcessStart(out hasExactProcessStart);
            pid = ReadReceiverReadyPid();
            if (pid <= 0)
            {
                File.Delete(path);
                detail = "invalid pid";
                AppDiagnosticsLogger.Record("RECEIVER_READY_MARKER_INVALID", ("reason", "pid"));
                return false;
            }
        }
        catch (Exception ex)
        {
            TryDeleteFile(path);
            detail = "marker read failed: " + ex.Message;
            AppDiagnosticsLogger.Record("RECEIVER_READY_MARKER_READ_WARN", ("error", ex.Message));
            return false;
        }

        try
        {
            using var process = Process.GetProcessById(pid);
            if (process.HasExited)
            {
                TryDeleteFile(path);
                detail = "process already exited";
                return false;
            }

            if (!LooksLikeIntegratedReceiverProcess(process))
            {
                TryDeleteFile(path);
                detail = $"pid {pid} is not a hidden StadiaX process";
                AppDiagnosticsLogger.Record("RECEIVER_READY_MARKER_MISMATCH", ("reason", "process"));
                return false;
            }

            if (!ProcessMatchesReadyMarker(process, processStart, hasExactProcessStart))
            {
                TryDeleteFile(path);
                detail = $"pid {pid} start time does not match marker";
                AppDiagnosticsLogger.Record("RECEIVER_READY_MARKER_MISMATCH", ("reason", "startTime"));
                return false;
            }

            detail = "active";
            return true;
        }
        catch (ArgumentException)
        {
            TryDeleteFile(path);
            detail = "process not found";
            return false;
        }
        catch (Exception ex)
        {
            detail = "process probe failed: " + ex.Message;
            AppDiagnosticsLogger.Record("RECEIVER_READY_MARKER_PROCESS_WARN", ("error", ex.Message));
            return true;
        }
    }

    private DateTimeOffset ReadReceiverReadyTimestamp()
    {
        var path = ReceiverReadyMarkerPath();
        var value = ReadMarkerValue(path, "timestamp");
        if (DateTimeOffset.TryParse(value, out var parsed))
        {
            return parsed;
        }

        return new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero);
    }

    private int ReadReceiverReadyPid()
    {
        return int.TryParse(ReadMarkerValue(ReceiverReadyMarkerPath(), "pid"), out var pid) ? pid : 0;
    }

    private DateTimeOffset ReadReceiverReadyProcessStart(out bool hasExactProcessStart)
    {
        var value = ReadMarkerValue(ReceiverReadyMarkerPath(), "processStartUtc");
        if (DateTimeOffset.TryParse(value, out var parsed))
        {
            hasExactProcessStart = true;
            return parsed;
        }

        hasExactProcessStart = false;
        return ReadReceiverReadyTimestamp();
    }

    private static string ReadMarkerValue(string path, string key)
    {
        var prefix = key + "=";
        foreach (var line in File.ReadLines(path))
        {
            if (line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return line[prefix.Length..].Trim();
            }
        }

        return "";
    }

    private static bool LooksLikeIntegratedReceiverProcess(Process process)
    {
        try
        {
            return process.ProcessName.Equals("StadiaX", StringComparison.OrdinalIgnoreCase) &&
                   process.MainWindowHandle == IntPtr.Zero;
        }
        catch
        {
            return false;
        }
    }

    private static bool ProcessMatchesReadyMarker(Process process, DateTimeOffset expectedStart, bool hasExactProcessStart)
    {
        try
        {
            var actualStart = new DateTimeOffset(process.StartTime.ToUniversalTime(), TimeSpan.Zero);
            var expectedStartUtc = expectedStart.ToUniversalTime();
            return hasExactProcessStart
                ? (actualStart - expectedStartUtc).Duration() <= TimeSpan.FromSeconds(2)
                : actualStart <= expectedStartUtc.AddSeconds(15);
        }
        catch
        {
            return !hasExactProcessStart;
        }
    }

    private void ClearReceiverReadyMarker(StatusWriter status, string phase)
    {
        var path = ReceiverReadyMarkerPath();
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
                status.Write("RECEIVER_READY_MARKER_CLEARED", $"{phase}: removed receiver.ready from previous session");
            }
        }
        catch (Exception ex)
        {
            status.Write("RECEIVER_READY_MARKER_CLEAR_WARN", $"{phase}: could not remove receiver.ready: {ex.Message}");
        }
    }

    private bool ReceiverStopSignalIsActive()
    {
        var path = ReceiverStopSignalPath();
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
            if (age <= ReceiverStopSignalMaxAge)
            {
                return true;
            }

            File.Delete(path);
            AppDiagnosticsLogger.Record("RECEIVER_STOP_SIGNAL_STALE", ("ageSeconds", ((int)age.TotalSeconds).ToString()));
            return false;
        }
        catch (Exception ex)
        {
            AppDiagnosticsLogger.Record("RECEIVER_STOP_SIGNAL_READ_WARN", ("error", ex.Message));
            return true;
        }
    }

    private string ReceiverStopSignalPath()
    {
        return Path.Combine(_paths.LogDirectory, "receiver.stop");
    }

    private string ReceiverReadyMarkerPath()
    {
        return Path.Combine(_paths.LogDirectory, "receiver.ready");
    }

    private void ClearControllerState(StatusWriter status, string phase)
    {
        foreach (var path in new[] { _paths.ControllerState, _paths.ControllerState + ".tmp" })
        {
            try
            {
                if (!File.Exists(path))
                {
                    continue;
                }

                File.Delete(path);
                status.Write("CONTROLLER_STATE_CLEARED", $"{phase}: removed {Path.GetFileName(path)} from previous session");
            }
            catch (Exception ex)
            {
                status.Write("CONTROLLER_STATE_CLEAR_WARN", $"{phase}: could not remove {Path.GetFileName(path)}: {ex.Message}");
            }
        }
    }

    private async Task<string> ResolveBusIdForDetachAsync(StatusWriter status)
    {
        var busFile = Path.Combine(_paths.Root, "bt_busid.txt");
        var saved = "";
        try
        {
            if (File.Exists(busFile))
            {
                saved = File.ReadAllText(busFile).Trim();
            }
        }
        catch (Exception ex)
        {
            status.Write("BT_RESTORE_SOURCE_WARN", "Could not read saved Bluetooth BUSID: " + ex.Message);
        }

        var list = await _runner.RunAsync("usbipd", new[] { "list" }, _paths.Root, 15000).ConfigureAwait(false);
        status.Write("BT_RESTORE_LIST_RESULT", $"exit={list.ExitCode} bytes={list.Output.Length}");
        var devices = ParseUsbipdList(list.Output);
        var canVerifyUsbipd = list.ExitCode == 0 && devices.Count > 0;

        if (IsBusId(saved))
        {
            var savedDevice = FindUsbipdBus(devices, saved);
            if (savedDevice is not null)
            {
                status.Write("BT_RESTORE_SOURCE", $"Using session Bluetooth BUSID {saved} ({Shorten(savedDevice.Line, 180)})");
                return saved;
            }

            if (!canVerifyUsbipd)
            {
                status.Write("BT_RESTORE_SOURCE_WARN", $"Using session Bluetooth BUSID {saved} without usbipd list verification");
                return saved;
            }

            status.Write("BT_RESTORE_SOURCE_IGNORED", $"Ignoring session BUSID {saved}; BUSID was not found in usbipd list");
        }
        else if (!string.IsNullOrWhiteSpace(saved))
        {
            status.Write("BT_RESTORE_SOURCE_IGNORED", "Ignoring invalid session Bluetooth BUSID");
        }

        foreach (var device in devices)
        {
            if (LooksLikeBluetoothUsbipdLine(device.Line))
            {
                status.Write("BT_RESTORE_SOURCE", $"Auto-detected Bluetooth BUSID {device.BusId} for restore ({Shorten(device.Line, 180)})");
                return device.BusId;
            }
        }

        return "";
    }

    private async Task<bool> SupportsForceUnbindAsync()
    {
        var help = await _runner.RunAsync("usbipd", new[] { "unbind", "--help" }, _paths.Root, 15000).ConfigureAwait(false);
        return help.Output.Contains("--force", StringComparison.OrdinalIgnoreCase) ||
               help.Error.Contains("--force", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<string> ConvertToWslPathAsync(string distro, string windowsPath)
    {
        var result = await _runner.RunAsync("wsl", new[] { "-d", distro, "wslpath", "-u", windowsPath }, _paths.Root, 10000).ConfigureAwait(false);
        return result.ExitCode == 0 ? result.Output.Trim() : "";
    }

    private string ReadSelectedControllerMacs()
    {
        var path = Path.Combine(_paths.Root, "selected_controller_macs.txt");
        if (!File.Exists(path))
        {
            return "";
        }

        var text = File.ReadAllText(path).Trim();
        return text.All(c => char.IsAsciiHexDigit(c) || c is ':' or ',' or ';' or ' ') ? text : "";
    }

    private static bool IsBusId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var parts = value.Split('-');
        return parts.Length == 2 && parts.All(part => part.Length > 0 && part.All(char.IsDigit));
    }

    private static string EscapeWslConfigPath(string path)
    {
        return path.Replace("\\", "\\\\", StringComparison.Ordinal);
    }

    private static string BashQuote(string value)
    {
        return "'" + value.Replace("'", "'\"'\"'", StringComparison.Ordinal) + "'";
    }

    private static void KillProcess(string processName)
    {
        foreach (var process in Process.GetProcessesByName(processName))
        {
            try { process.Kill(entireProcessTree: true); } catch { }
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    private static void TryDeleteSessionFile(string path, StatusWriter status, string eventName)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
                status.Write(eventName + "_CLEARED", "Removed " + Path.GetFileName(path));
            }
        }
        catch (Exception ex)
        {
            status.Write(eventName + "_CLEAR_WARN", "Could not remove " + Path.GetFileName(path) + ": " + ex.Message);
        }
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? "";
    }

    private static string Shorten(string value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length <= maxLength)
        {
            return string.IsNullOrWhiteSpace(value) ? "-" : value;
        }

        return value[..maxLength] + "...";
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
