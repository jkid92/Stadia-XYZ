using System.Diagnostics;

namespace StadiaX.ControlCenter;

internal sealed class BridgeOrchestrator
{
    private const int StartPhaseCount = 6;
    private const int StopPhaseCount = 3;

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
        ClearReceiverStopSignal();

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
        File.WriteAllText(Path.Combine(_paths.Root, "bt_busid.txt"), busId + Environment.NewLine);
        status.Write("BT_SELECTED", $"Using Bluetooth BUSID {busId}");
        status.WritePhase("Linux bridge", 3, StartPhaseCount, "Bluetooth adapter", "OK", $"Using Bluetooth BUSID {busId}");

        if (!await AttachBluetoothAsync(distro, busId, status).ConfigureAwait(false))
        {
            status.WritePhase("Linux bridge", 3, StartPhaseCount, "Bluetooth adapter", "FAIL", "Could not attach Bluetooth adapter to WSL");
            return 1;
        }

        status.WritePhase("Linux bridge", 4, StartPhaseCount, "Linux core", "START", "Deploying and starting BlueZ bridge");
        if (!await DeployLinuxBridgeAsync(distro, status).ConfigureAwait(false))
        {
            status.WritePhase("Linux bridge", 4, StartPhaseCount, "Linux core", "FAIL", "Could not deploy Linux bridge files");
            await StopAsync().ConfigureAwait(false);
            return 1;
        }

        var linuxStarted = await StartLinuxCoreAsync(distro, status).ConfigureAwait(false);
        if (!linuxStarted)
        {
            status.WritePhase("Linux bridge", 4, StartPhaseCount, "Linux core", "FAIL", "Could not start Linux core");
            await StopAsync().ConfigureAwait(false);
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
        status.WritePhase("Linux bridge", 1, StopPhaseCount, "Stop receiver", "START", "Stopping receiver and Linux session");
        status.Write("STOP_START", "Stopping Stadia X and restoring Bluetooth");

        SignalReceiverStop();
        KillProcess("stadia_receiver");
        KillProcess("stadia-vigem-x86");

        await _runner.RunAsync("wsl", new[] { "--shutdown" }, _paths.Root, 20000).ConfigureAwait(false);
        await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        status.WritePhase("Linux bridge", 1, StopPhaseCount, "Stop receiver", "OK", "Receiver stop signal sent and WSL shutdown requested");

        status.WritePhase("Linux bridge", 2, StopPhaseCount, "Restore Bluetooth", "START", "Resolving adapter to detach");
        var busId = await ResolveBusIdForDetachAsync().ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(busId))
        {
            status.Write("BT_RESTORE_UNKNOWN", "No Bluetooth BUSID found for detach");
            status.WritePhase("Linux bridge", 2, StopPhaseCount, "Restore Bluetooth", "WARN", "No Bluetooth BUSID found for detach");
        }
        else
        {
            status.Write("BT_RESTORE_START", $"Releasing Bluetooth BUSID {busId}");
            var detach = await _runner.RunAsync("usbipd", new[] { "detach", "--busid", busId }, _paths.Root, 15000).ConfigureAwait(false);
            status.Write("BT_RESTORE_DETACH_RESULT", $"exit={detach.ExitCode} detail={Shorten(FirstNonEmpty(detach.Error, detach.Output, "none"), 240)}");
            await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
            var unbind = await _runner.RunAsync("usbipd", new[] { "unbind", "--busid", busId }, _paths.Root, 15000).ConfigureAwait(false);
            var forceUnbind = await _runner.RunAsync("usbipd", new[] { "unbind", "--busid", busId, "--force" }, _paths.Root, 15000).ConfigureAwait(false);
            status.Write("BT_RESTORE_UNBIND_RESULT", $"exit={unbind.ExitCode} forceExit={forceUnbind.ExitCode} detail={Shorten(FirstNonEmpty(forceUnbind.Error, forceUnbind.Output, unbind.Error, unbind.Output, "none"), 240)}");
            var restoreLooksOk = detach.ExitCode == 0 || unbind.ExitCode == 0 || forceUnbind.ExitCode == 0;
            status.Write(restoreLooksOk ? "BT_RESTORE_OK" : "BT_RESTORE_WARN", "Bluetooth adapter restore commands completed");
            status.WritePhase("Linux bridge", 2, StopPhaseCount, "Restore Bluetooth", restoreLooksOk ? "OK" : "WARN", $"Bluetooth BUSID {busId} restore commands completed");
            var busFile = Path.Combine(_paths.Root, "bt_busid.txt");
            if (File.Exists(busFile))
            {
                File.Delete(busFile);
            }
        }

        await Task.Delay(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
        status.Write("STOP_DONE", "Teardown complete");
        status.WritePhase("Linux bridge", 3, StopPhaseCount, "Teardown", "OK", "Teardown complete");
        return 0;
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
        var targetKernel = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "wsl_kernel");
        if (!File.Exists(targetKernel) && File.Exists(sourceKernel))
        {
            File.Copy(sourceKernel, targetKernel, overwrite: true);
            var wslConfig = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".wslconfig");
            var backup = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".wslconfig.stadia-x.bak");
            if (File.Exists(wslConfig) && !File.Exists(backup))
            {
                File.Copy(wslConfig, backup);
                status.Write("WSL_CONFIG_BACKUP", "Backed up existing .wslconfig");
            }

            File.WriteAllText(wslConfig, "[wsl2]\n" +
                                        $"kernel={EscapeWslConfigPath(targetKernel)}\n" +
                                        "memory=800MB\nprocessors=2\nswap=800MB\n");
            status.Write("WSL_KERNEL_DEPLOY", "Custom WSL kernel deployed; restarting WSL");
            await _runner.RunAsync("wsl", new[] { "--shutdown" }, _paths.Root, 20000).ConfigureAwait(false);
            await Task.Delay(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
            return true;
        }
        else if (!File.Exists(targetKernel))
        {
            status.Write("WSL_KERNEL_MISSING", "Custom WSL kernel is required but build/wsl_kernel is missing");
            return false;
        }

        return true;
    }

    private async Task CleanupOldSessionAsync(string distro, StatusWriter status)
    {
        SignalReceiverStop();
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
            ClearReceiverStopSignal();
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
        ClearReceiverStopSignal();
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
                status.Write("BT_ATTACH_OK", "Bluetooth adapter attached to WSL");
                var verify = await _runner.RunAsync("usbipd", new[] { "list" }, _paths.Root, 15000).ConfigureAwait(false);
                var line = verify.Output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                    .FirstOrDefault(value => value.TrimStart().StartsWith(busId + " ", StringComparison.OrdinalIgnoreCase));
                status.Write(line is not null && line.Contains("Attached", StringComparison.OrdinalIgnoreCase) ? "BT_ATTACH_VERIFY_OK" : "BT_ATTACH_VERIFY_WARN",
                    line is not null && line.Contains("Attached", StringComparison.OrdinalIgnoreCase)
                        ? $"usbipd reports BUSID {busId} as attached"
                        : "Attach command succeeded, but usbipd list did not report the BUSID as Attached");
                await VerifyBluetoothInsideWslAsync(distro, status).ConfigureAwait(false);
                await Task.Delay(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                return true;
            }

            status.Write("BT_ATTACH_RETRY", $"Attach failed on attempt {attempt}; retrying after cleanup delay");
            await Task.Delay(TimeSpan.FromSeconds(4)).ConfigureAwait(false);
        }

        status.Write("BT_ATTACH_FAILED", "Could not attach Bluetooth adapter to WSL");
        return false;
    }

    private async Task VerifyBluetoothInsideWslAsync(string distro, StatusWriter status)
    {
        var probe = await _runner.RunAsync(
            "wsl",
            new[] { "-d", distro, "-u", "root", "bash", "-lc", "if command -v bluetoothctl >/dev/null 2>&1; then timeout 6s bluetoothctl list 2>/dev/null || timeout 6s bluetoothctl show 2>/dev/null || true; elif command -v hciconfig >/dev/null 2>&1; then hciconfig -a 2>/dev/null || true; fi" },
            _paths.Root,
            10000).ConfigureAwait(false);

        var detail = Shorten(FirstNonEmpty(probe.Output, probe.Error, "no Bluetooth controller reported yet"), 260);
        var looksReady = probe.Output.Contains("Controller", StringComparison.OrdinalIgnoreCase) ||
                         probe.Output.Contains("hci", StringComparison.OrdinalIgnoreCase);
        status.Write(looksReady ? "BT_WSL_PROBE_OK" : "BT_WSL_PROBE_WAIT", detail);
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
        Directory.CreateDirectory(_paths.LogDirectory);
        await File.AppendAllTextAsync(_paths.ReceiverLog, $"[{DateTime.Now}] Starting integrated receiver for {wslIp}{Environment.NewLine}").ConfigureAwait(false);

        using var cancellation = new CancellationTokenSource();
        using var stopWatcher = StartReceiverStopWatcher(cancellation);
        var receiver = new IntegratedReceiver(_paths, wslIp, status);
        int exitCode;
        try
        {
            exitCode = await receiver.RunAsync(cancellation.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            status.Write("RECEIVER_FAILED", ex.Message);
            exitCode = 1;
        }

        status.Write("RECEIVER_EXITED", $"Integrated receiver exited with code {exitCode}");
        await StopAsync().ConfigureAwait(false);
        return exitCode;
    }

    private System.Threading.Timer StartReceiverStopWatcher(CancellationTokenSource cancellation)
    {
        return new System.Threading.Timer(_ =>
        {
            try
            {
                if (File.Exists(ReceiverStopSignalPath()))
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

    private void SignalReceiverStop()
    {
        Directory.CreateDirectory(_paths.LogDirectory);
        File.WriteAllText(ReceiverStopSignalPath(), DateTime.Now.ToString("O") + Environment.NewLine);
    }

    private void ClearReceiverStopSignal()
    {
        var path = ReceiverStopSignalPath();
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private string ReceiverStopSignalPath()
    {
        return Path.Combine(_paths.LogDirectory, "receiver.stop");
    }

    private async Task<string> ResolveBusIdForDetachAsync()
    {
        var busFile = Path.Combine(_paths.Root, "bt_busid.txt");
        if (File.Exists(busFile))
        {
            var saved = File.ReadAllText(busFile).Trim();
            if (IsBusId(saved))
            {
                return saved;
            }
        }

        var list = await _runner.RunAsync("usbipd", new[] { "list" }, _paths.Root, 15000).ConfigureAwait(false);
        foreach (var line in list.Output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
        {
            var lower = line.ToLowerInvariant();
            if (lower.Contains("bluetooth") || lower.Contains("usbip shared") || lower.Contains("intel wireless"))
            {
                var first = line.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                if (IsBusId(first))
                {
                    return first!;
                }
            }
        }

        return "";
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
}
