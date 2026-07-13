using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace StadiaX.ControlCenter;

internal sealed record UsbipdDevice(string BusId, string VidPid, string Name, string State, bool IsBluetooth)
{
    public string Display => $"{BusId}  {VidPid}  {Name}  [{State}]";
}

internal sealed record WindowsBluetoothDevice(string Name, string Status, string InstanceId);

internal sealed record LinuxBluetoothDevice(string Mac, string Name, string Connected, string Paired, string Trusted, int? BatteryPercent, bool IsStadia, string Source = BluetoothDeviceSources.BlueZ);

internal static class BluetoothDeviceSources
{
    public const string BlueZ = "BlueZ";
    public const string Diagnostics = "Diagnostics";
    public const string BridgeLog = "Bridge log";
    public const string Receiver = "Receiver";
    public const string Demo = "Demo";
}

internal sealed record ControllerProfile(string Name, string Mac, int Slot, bool AutoConnect);

internal sealed record MacroMapping(string Code, string Shortcut);

internal sealed record ControllerTelemetryRow(
    int Index,
    bool Active,
    double PacketsPerSecond,
    ulong Packets,
    int TriggerLeft,
    int TriggerRight,
    int StickLeftX,
    int StickLeftY,
    int StickRightX,
    int StickRightY,
    IReadOnlyDictionary<string, bool> Buttons);

internal sealed record ControllerTelemetrySnapshot(DateTimeOffset ReadAt, IReadOnlyList<ControllerTelemetryRow> Controllers);

internal sealed class NativeControlServices
{
    private const int BridgeRumblePort = 45494;
    private const byte PacketMagic = 0x53;
    private const byte PacketVersion = 1;
    public static readonly TimeSpan ControllerTelemetryMaxAge = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ControllerActiveMaxAge = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan LiveFallbackMaxAge = TimeSpan.FromSeconds(90);
    private static readonly Regex BusIdPattern = new(@"^\d+-\d+$", RegexOptions.Compiled);
    private static readonly Regex MacPattern = new(@"^[0-9A-Fa-f]{2}(:[0-9A-Fa-f]{2}){5}$", RegexOptions.Compiled);
    private static bool IsBluetoothDemoMode =>
        string.Equals(Environment.GetEnvironmentVariable("STADIAX_DEMO_BLUETOOTH"), "1", StringComparison.OrdinalIgnoreCase);

    private readonly AppPaths _paths;
    private readonly ProcessRunner _runner;
    private readonly WslDistroResolver _wslResolver;
    private string _liveInputDistroCache = "";
    private ControllerTelemetrySnapshot? _lastLiveInputSnapshot;
    private DateTime _lastLiveInputProbeUtc = DateTime.MinValue;
    private DateTime _lastLiveInputTelemetryLogUtc = DateTime.MinValue;
    private DateTime _lastLiveInputTelemetryFailureLogUtc = DateTime.MinValue;

    public NativeControlServices(AppPaths paths, ProcessRunner runner)
    {
        _paths = paths;
        _runner = runner;
        _wslResolver = new WslDistroResolver(paths, runner);
    }

    public async Task<IReadOnlyList<UsbipdDevice>> GetUsbipdDevicesAsync()
    {
        if (IsBluetoothDemoMode)
        {
            return DemoUsbipdDevices();
        }

        var result = await _runner.RunAsync("usbipd", new[] { "list" }, _paths.Root, 15000).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            return Array.Empty<UsbipdDevice>();
        }

        var devices = new List<UsbipdDevice>();
        foreach (var rawLine in result.Output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (!Regex.IsMatch(line, @"^\d+-\d+\s+"))
            {
                continue;
            }

            var parts = Regex.Split(line, @"\s{2,}").Where(p => p.Length > 0).ToArray();
            if (parts.Length < 3)
            {
                continue;
            }

            var busId = parts[0].Trim();
            var vidPid = parts.Length > 1 ? parts[1].Trim() : "";
            var state = parts.Length > 3 ? parts[^1].Trim() : "";
            var name = parts.Length > 3 ? string.Join(" ", parts.Skip(2).Take(parts.Length - 3)).Trim() : parts[2].Trim();
            devices.Add(new UsbipdDevice(busId, vidPid, name, state, LooksLikeBluetooth(name)));
        }

        return devices
            .OrderByDescending(d => d.IsBluetooth)
            .ThenBy(d => d.BusId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public string GetSelectedBluetoothBusId()
    {
        if (!File.Exists(_paths.SelectedBluetoothBusId))
        {
            return "";
        }

        var busId = File.ReadAllText(_paths.SelectedBluetoothBusId).Trim();
        return IsBusId(busId) ? busId : "";
    }

    public void SaveSelectedBluetoothBusId(string busId)
    {
        busId = busId.Trim();
        if (!IsBusId(busId))
        {
            throw new InvalidOperationException("Bluetooth BUSID is not valid. Expected format: 1-13.");
        }

        File.WriteAllText(_paths.SelectedBluetoothBusId, busId + Environment.NewLine, Encoding.ASCII);
    }

    public async Task<IReadOnlyList<WslDistro>> GetWslDistrosAsync()
    {
        return await _wslResolver.GetDistrosAsync().ConfigureAwait(false);
    }

    public string GetSelectedWslDistro()
    {
        if (!File.Exists(_paths.SelectedWslDistro))
        {
            return "";
        }

        var distro = File.ReadAllText(_paths.SelectedWslDistro).Trim();
        return IsSafeDistroName(distro) ? distro : "";
    }

    public void SaveSelectedWslDistro(string distro)
    {
        if (string.IsNullOrWhiteSpace(distro) || distro.Equals("Automatic", StringComparison.OrdinalIgnoreCase))
        {
            if (File.Exists(_paths.SelectedWslDistro))
            {
                File.Delete(_paths.SelectedWslDistro);
            }
            return;
        }

        distro = distro.Trim();
        if (!IsSafeDistroName(distro))
        {
            throw new InvalidOperationException("WSL distro name contains unsupported characters.");
        }

        File.WriteAllText(_paths.SelectedWslDistro, distro + Environment.NewLine, Encoding.ASCII);
    }

    public async Task<IReadOnlyList<WindowsBluetoothDevice>> GetWindowsBluetoothDevicesAsync()
    {
        if (IsBluetoothDemoMode)
        {
            return DemoWindowsBluetoothDevices();
        }

        const string script = "Get-PnpDevice -Class Bluetooth -ErrorAction SilentlyContinue | ForEach-Object { " +
                              "$name=($_.FriendlyName -replace '\\|','/'); $status=($_.Status -replace '\\|','/'); " +
                              "$id=($_.InstanceId -replace '\\|','/'); \"$name|$status|$id\" }";
        var result = await _runner.RunAsync("powershell.exe", new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", script }, _paths.Root, 15000).ConfigureAwait(false);
        var devices = new List<WindowsBluetoothDevice>();
        foreach (var line in result.Output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split('|');
            if (parts.Length >= 3)
            {
                devices.Add(new WindowsBluetoothDevice(parts[0].Trim(), parts[1].Trim(), parts[2].Trim()));
            }
        }

        return devices.ToArray();
    }

    public async Task<IReadOnlyList<LinuxBluetoothDevice>> GetLinuxBluetoothDevicesAsync(int scanSeconds = 0)
    {
        if (IsBluetoothDemoMode)
        {
            if (scanSeconds > 0)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(1200, scanSeconds * 120))).ConfigureAwait(false);
            }

            return DemoLinuxBluetoothDevices();
        }

        var distro = await _wslResolver.ResolveAsync().ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(distro))
        {
            return Array.Empty<LinuxBluetoothDevice>();
        }

        await EnsureBluetoothAdapterAttachedAsync(distro).ConfigureAwait(false);

        scanSeconds = Math.Clamp(scanSeconds, 0, 20);
        var listTimeoutSeconds = 2;
        var commandTimeoutMs = scanSeconds > 0 ? Math.Max(30000, (scanSeconds + 22) * 1000) : 10000;
        var scanCommand = scanSeconds > 0
            ? $"scan_log=/tmp/stadia-x-ui-scan-$(id -u).log; rm -f \"$scan_log\"; timeout {scanSeconds + 2}s bluetoothctl --timeout {scanSeconds} scan on > \"$scan_log\" 2>&1 || true; timeout 2s bluetoothctl scan off >/dev/null 2>&1 || true; "
            : "scan_log=/tmp/stadia-x-ui-scan-$(id -u).log; ";
        var scanLogPipe = scanSeconds > 0
            ? "[ -s \"$scan_log\" ] && cat \"$scan_log\" | parse_lines; "
            : "";
        var knownMacs = string.Join(" ",
            GetSelectedControllerMacs()
                .Concat(GetProfiles().Select(p => p.Mac))
                .Where(IsMac)
                .Select(m => m.ToUpperInvariant())
                .Distinct(StringComparer.OrdinalIgnoreCase));

        var script =
            "if ! command -v bluetoothctl >/dev/null 2>&1; then echo \"__ERROR__|bluetoothctl missing\"; exit 0; fi; " +
            scanCommand +
            "parse_lines() { " +
            "grep -Eo '([0-9A-Fa-f]{2}:){5}[0-9A-Fa-f]{2}.*' | while IFS= read -r tail; do " +
            "mac=\"${tail%% *}\"; name=\"${tail#\"$mac\"}\"; name=\"${name# }\"; " +
            "case \"$tail\" in *Controller*) continue;; esac; " +
            "case \"$name\" in RSSI:*|TxPower:*|ManufacturerData:*|ServiceData:*|Flags:*|UUIDs:*|Connected:*|Paired:*|Trusted:*|Class:*|Icon:*|Alias:*|Name:*|Discovering:*) continue;; esac; " +
            "printf '%s|%s\\n' \"$mac\" \"$name\"; " +
            "done; " +
            "}; " +
            "info_value() { printf '%s\\n' \"$1\" | sed -n \"s/^[[:space:]]*$2:[[:space:]]*//Ip\" | head -n 1; }; " +
            "emit_device() { " +
            "mac=\"$1\"; fallback_name=\"$2\"; " +
            "info=\"$(timeout 3s bluetoothctl info \"$mac\" 2>/dev/null || true)\"; " +
            "name=\"$(info_value \"$info\" Name)\"; [ -z \"$name\" ] && name=\"$(info_value \"$info\" Alias)\"; [ -z \"$name\" ] && name=\"$fallback_name\"; [ -z \"$name\" ] && name=\"(unknown)\"; " +
            "paired=\"$(info_value \"$info\" Paired)\"; trusted=\"$(info_value \"$info\" Trusted)\"; connected=\"$(info_value \"$info\" Connected)\"; " +
            "battery=\"$(printf '%s\\n' \"$info\" | sed -nE 's/.*Battery Percentage:.*\\(([0-9]+)\\).*/\\1/p' | head -n 1)\"; " +
            "name=\"${name//|/}\"; printf '%s|%s|%s|%s|%s|%s\\n' \"$mac\" \"$name\" \"$paired\" \"$trusted\" \"$connected\" \"$battery\"; " +
            "}; " +
            "{ " +
            scanLogPipe +
            "for filter in _ Connected Paired Trusted Bonded; do " +
            $"if [ \"$filter\" = _ ]; then timeout {listTimeoutSeconds}s bluetoothctl devices 2>/dev/null; else timeout {listTimeoutSeconds}s bluetoothctl devices \"$filter\" 2>/dev/null; fi; " +
            "done | parse_lines; " +
            "} | while IFS='|' read -r mac name; do " +
            "mac=\"${mac^^}\"; case \" $seen \" in *\" $mac \"*) continue;; esac; seen=\"$seen $mac\"; " +
            "emit_device \"$mac\" \"$name\"; " +
            "done";

        var result = await _runner.RunAsync("wsl", new[] { "-d", distro, "--exec", "bash", "-lc", script }, _paths.Root, commandTimeoutMs).ConfigureAwait(false);
        AppDiagnosticsLogger.Record(
            "LINUX_BT_RAW_RESULT",
            ("scanSeconds", scanSeconds.ToString()),
            ("exitCode", result.ExitCode.ToString()),
            ("outputBytes", result.Output.Length.ToString()),
            ("errorBytes", result.Error.Length.ToString()),
            ("outputSample", TruncateForDiagnostics(result.Output, 500)),
            ("errorSample", TruncateForDiagnostics(result.Error, 300)));
        var devices = new List<LinuxBluetoothDevice>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in result.Output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.StartsWith("__ERROR__|", StringComparison.Ordinal))
            {
                continue;
            }

            var parts = line.Split('|');
            if (parts.Length < 6 || !IsMac(parts[0]))
            {
                continue;
            }

            int? battery = int.TryParse(parts[5].Trim(), out var percent) ? Math.Clamp(percent, 0, 100) : null;
            var name = string.IsNullOrWhiteSpace(parts[1]) ? "(unknown)" : parts[1].Trim();
            UpsertLinuxDevice(devices, seen, new LinuxBluetoothDevice(
                parts[0].Trim().ToUpperInvariant(),
                name,
                NormalizeYesNo(parts[4]),
                NormalizeYesNo(parts[2]),
                NormalizeYesNo(parts[3]),
                battery,
                name.Contains("stadia", StringComparison.OrdinalIgnoreCase)));
        }

        foreach (var mac in knownMacs.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (seen.Contains(mac))
            {
                continue;
            }

            var device = await GetLinuxBluetoothDeviceByInfoAsync(distro, mac).ConfigureAwait(false);
            if (device is not null)
            {
                UpsertLinuxDevice(devices, seen, device);
            }
        }

        AddBluetoothDiagnosticsDevices(devices, seen);
        AddBridgeLogDevices(devices, seen);

        return devices
            .OrderByDescending(d => d.IsStadia)
            .ThenByDescending(IsLiveBluetoothConnected)
            .ThenBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task SendRumbleTestAsync(int controllerIndex, byte largeMotor = 220, byte smallMotor = 180, int durationMs = 320)
    {
        if (controllerIndex < 1 || controllerIndex > 4)
        {
            throw new ArgumentOutOfRangeException(nameof(controllerIndex), "Controller index must be between 1 and 4.");
        }

        if (IsBluetoothDemoMode)
        {
            await Task.Delay(Math.Clamp(durationMs, 80, 1500)).ConfigureAwait(false);
            return;
        }

        var distro = await _wslResolver.ResolveAsync().ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(distro))
        {
            throw new InvalidOperationException("No usable WSL distro found. Start the bridge before testing rumble.");
        }

        const string ipScript = "hostname -I 2>/dev/null | tr ' ' '\\n' | grep -m1 -E '^[0-9]+(\\.[0-9]+){3}$'";
        var ipResult = await _runner.RunAsync("wsl", new[] { "-d", distro, "bash", "-lc", ipScript }, _paths.Root, 10000).ConfigureAwait(false);
        var ip = ipResult.Output.Trim().Split(new[] { "\r\n", "\n", " " }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (!IPAddress.TryParse(ip, out var bridgeAddress))
        {
            throw new InvalidOperationException("Could not resolve the WSL bridge IP. Start the bridge, then try the rumble test again.");
        }

        using var udp = new UdpClient(AddressFamily.InterNetwork);
        udp.Connect(new IPEndPoint(bridgeAddress, BridgeRumblePort));
        await udp.SendAsync(BuildRumblePacket(controllerIndex - 1, largeMotor, smallMotor), 6).ConfigureAwait(false);
        await Task.Delay(Math.Clamp(durationMs, 80, 1500)).ConfigureAwait(false);
        await udp.SendAsync(BuildRumblePacket(controllerIndex - 1, 0, 0), 6).ConfigureAwait(false);
    }

    public async Task<CommandResult> RunLinuxBluetoothCommandAsync(string mac, string command)
    {
        if (!IsMac(mac))
        {
            return new CommandResult(-1, "", "Invalid Bluetooth MAC.");
        }

        if (IsBluetoothDemoMode)
        {
            return new CommandResult(
                0,
                $"Demo Bluetooth command '{command}' completed for {mac}.{Environment.NewLine}No real Bluetooth device was changed.",
                "");
        }

        var distro = await _wslResolver.ResolveAsync().ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(distro))
        {
            return new CommandResult(-1, "", "No WSL distro resolved.");
        }

        await EnsureBluetoothAdapterAttachedAsync(distro).ConfigureAwait(false);

        command = command.ToLowerInvariant() switch
        {
            "pair" => BuildLinuxBluetoothPairScript(mac),
            "trust" => BuildLinuxBluetoothCommandScript(mac, ("Trust device", "trust", 8)),
            "pair-only" => BuildLinuxBluetoothCommandScript(mac, ("Pair device", "pair", 24)),
            "connect" => BuildLinuxBluetoothCommandScript(mac, ("Connect device", "connect", 24)),
            "disconnect" => BuildLinuxBluetoothCommandScript(mac, ("Disconnect device", "disconnect", 12)),
            "remove" => BuildLinuxBluetoothCommandScript(mac, ("Remove device", "remove", 12)),
            _ => BuildLinuxBluetoothInfoScript(mac)
        };

        return await _runner.RunAsync("wsl", new[] { "-d", distro, "-u", "root", "--exec", "bash", "-lc", command }, _paths.Root, 60000).ConfigureAwait(false);
    }

    private async Task EnsureBluetoothAdapterAttachedAsync(string distro)
    {
        if (IsBluetoothDemoMode)
        {
            return;
        }

        IReadOnlyList<UsbipdDevice> devices;
        try
        {
            devices = await GetUsbipdDevicesAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            AppDiagnosticsLogger.Record("BT_ATTACH_PREFLIGHT_FAILED", ("stage", "list"), ("error", ex.Message));
            return;
        }

        var selectedBusId = GetSelectedBluetoothBusId();
        var selected = !string.IsNullOrWhiteSpace(selectedBusId)
            ? devices.FirstOrDefault(device => device.BusId.Equals(selectedBusId, StringComparison.OrdinalIgnoreCase))
            : null;
        var adapter = selected ?? devices.FirstOrDefault(device => device.IsBluetooth);
        if (adapter is null || string.IsNullOrWhiteSpace(adapter.BusId))
        {
            AppDiagnosticsLogger.Record("BT_ATTACH_PREFLIGHT_SKIPPED", ("reason", "no_adapter"), ("selectedBusId", selectedBusId));
            return;
        }

        if (adapter.State.Contains("Attached", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        AppDiagnosticsLogger.Record(
            "BT_ATTACH_PREFLIGHT_START",
            ("busId", adapter.BusId),
            ("state", adapter.State),
            ("name", adapter.Name),
            ("distro", distro));

        var wake = await _runner.RunAsync("wsl", new[] { "-d", distro, "--exec", "bash", "-lc", "true" }, _paths.Root, 12000).ConfigureAwait(false);
        AppDiagnosticsLogger.Record(
            "BT_ATTACH_PREFLIGHT_WAKE_WSL",
            ("distro", distro),
            ("exitCode", wake.ExitCode.ToString()),
            ("detail", TruncateForDiagnostics(FirstNonEmpty(wake.Error, wake.Output, "none"), 180)));

        var bind = await _runner.RunAsync("usbipd", new[] { "bind", "--busid", adapter.BusId, "--force" }, _paths.Root, 20000).ConfigureAwait(false);
        AppDiagnosticsLogger.Record(
            "BT_ATTACH_PREFLIGHT_BIND",
            ("busId", adapter.BusId),
            ("exitCode", bind.ExitCode.ToString()),
            ("detail", TruncateForDiagnostics(FirstNonEmpty(bind.Error, bind.Output, "none"), 240)));

        var attach = await _runner.RunAsync("usbipd", new[] { "attach", "--wsl", "--busid", adapter.BusId }, _paths.Root, 30000, createNoWindow: false).ConfigureAwait(false);
        if (attach.ExitCode != 0 &&
            FirstNonEmpty(attach.Error, attach.Output).Contains("no WSL 2 distribution running", StringComparison.OrdinalIgnoreCase))
        {
            await _runner.RunAsync("wsl", new[] { "-d", distro, "--exec", "bash", "-lc", "sleep 1" }, _paths.Root, 12000).ConfigureAwait(false);
            attach = await _runner.RunAsync("usbipd", new[] { "attach", "--wsl", "--busid", adapter.BusId }, _paths.Root, 30000, createNoWindow: false).ConfigureAwait(false);
        }
        AppDiagnosticsLogger.Record(
            "BT_ATTACH_PREFLIGHT_ATTACH",
            ("busId", adapter.BusId),
            ("exitCode", attach.ExitCode.ToString()),
            ("detail", TruncateForDiagnostics(FirstNonEmpty(attach.Error, attach.Output, "none"), 240)));

        if (attach.ExitCode == 0)
        {
            await _runner.RunAsync(
                "wsl",
                new[] { "-d", distro, "-u", "root", "--exec", "bash", "-lc", "systemctl restart bluetooth >/dev/null 2>&1 || true; sleep 1; bluetoothctl power on >/dev/null 2>&1 || true" },
                _paths.Root,
                12000).ConfigureAwait(false);
        }
    }

    private static string BuildLinuxBluetoothInfoScript(string mac)
    {
        return string.Join("\n", new[]
        {
            "if ! command -v bluetoothctl >/dev/null 2>&1; then echo \"bluetoothctl missing\"; exit 0; fi",
            $"mac='{mac}'",
            "echo \"Final BlueZ info:\"",
            "timeout 5s bluetoothctl info \"$mac\" 2>&1 || true"
        });
    }

    private static string BuildLinuxBluetoothPairScript(string mac)
    {
        return string.Join("\n", new[]
        {
            "if ! command -v bluetoothctl >/dev/null 2>&1; then echo \"bluetoothctl missing\"; exit 127; fi",
            $"mac='{mac}'",
            "agent_pid=\"\"",
            "cleanup_agent() { [ -n \"$agent_pid\" ] && kill \"$agent_pid\" >/dev/null 2>&1 || true; }",
            "trap cleanup_agent EXIT",
            "print_state() {",
            "  info=\"$(timeout 5s bluetoothctl info \"$mac\" 2>&1 || true)\"",
            "  paired=\"$(printf \"%s\\n\" \"$info\" | awk -F': ' '/Paired:/ {print $2; exit}')\"",
            "  trusted=\"$(printf \"%s\\n\" \"$info\" | awk -F': ' '/Trusted:/ {print $2; exit}')\"",
            "  connected=\"$(printf \"%s\\n\" \"$info\" | awk -F': ' '/Connected:/ {print $2; exit}')\"",
            "  name=\"$(printf \"%s\\n\" \"$info\" | awk -F': ' '/Name:/ {print $2; exit}')\"",
            "  echo \"State: name=${name:-unknown} paired=${paired:-unknown} trusted=${trusted:-unknown} connected=${connected:-unknown}\"",
            "}",
            "state_value() { timeout 5s bluetoothctl info \"$mac\" 2>/dev/null | awk -F': ' -v key=\"$1\" '$1 ~ \"^[[:space:]]*\" key \"$\" {print $2; exit}'; }",
            "wait_state() {",
            "  key=\"$1\"",
            "  want=\"$2\"",
            "  seconds=\"$3\"",
            "  i=0",
            "  while [ \"$i\" -lt \"$seconds\" ]; do",
            "    value=\"$(state_value \"$key\")\"",
            "    [ \"${value,,}\" = \"${want,,}\" ] && return 0",
            "    sleep 1",
            "    i=$((i + 1))",
            "  done",
            "  return 1",
            "}",
            "start_agent() {",
            "  if ! python3 -c 'import dbus, dbus.service, dbus.mainloop.glib; from gi.repository import GLib' >/dev/null 2>&1; then",
            "    echo \"Python D-Bus agent dependencies are missing; continuing with bluetoothctl agent fallback.\"",
            "    return 1",
            "  fi",
            "  cat >/tmp/stadia-x-ui-agent.py <<'PY'",
            "import dbus",
            "import dbus.service",
            "import dbus.mainloop.glib",
            "from gi.repository import GLib",
            "BUS_NAME = 'org.bluez'",
            "AGENT_MANAGER_IFACE = 'org.bluez.AgentManager1'",
            "AGENT_IFACE = 'org.bluez.Agent1'",
            "AGENT_PATH = '/stadiax/ui_agent'",
            "class Agent(dbus.service.Object):",
            "    @dbus.service.method(AGENT_IFACE, in_signature='', out_signature='')",
            "    def Release(self): pass",
            "    @dbus.service.method(AGENT_IFACE, in_signature='o', out_signature='s')",
            "    def RequestPinCode(self, device): return '0000'",
            "    @dbus.service.method(AGENT_IFACE, in_signature='os', out_signature='')",
            "    def DisplayPinCode(self, device, pincode): pass",
            "    @dbus.service.method(AGENT_IFACE, in_signature='o', out_signature='u')",
            "    def RequestPasskey(self, device): return dbus.UInt32(0)",
            "    @dbus.service.method(AGENT_IFACE, in_signature='ouq', out_signature='')",
            "    def DisplayPasskey(self, device, passkey, entered): pass",
            "    @dbus.service.method(AGENT_IFACE, in_signature='ou', out_signature='')",
            "    def RequestConfirmation(self, device, passkey): pass",
            "    @dbus.service.method(AGENT_IFACE, in_signature='o', out_signature='')",
            "    def RequestAuthorization(self, device): pass",
            "    @dbus.service.method(AGENT_IFACE, in_signature='os', out_signature='')",
            "    def AuthorizeService(self, device, uuid): pass",
            "    @dbus.service.method(AGENT_IFACE, in_signature='', out_signature='')",
            "    def Cancel(self): pass",
            "dbus.mainloop.glib.DBusGMainLoop(set_as_default=True)",
            "bus = dbus.SystemBus()",
            "agent = Agent(bus, AGENT_PATH)",
            "manager = dbus.Interface(bus.get_object(BUS_NAME, '/org/bluez'), AGENT_MANAGER_IFACE)",
            "try:",
            "    manager.RegisterAgent(AGENT_PATH, 'NoInputNoOutput')",
            "except dbus.exceptions.DBusException:",
            "    pass",
            "manager.RequestDefaultAgent(AGENT_PATH)",
            "print('Stadia X UI BlueZ auto-agent ready', flush=True)",
            "GLib.MainLoop().run()",
            "PY",
            "  python3 /tmp/stadia-x-ui-agent.py >/tmp/stadia-x-ui-agent.log 2>&1 &",
            "  agent_pid=$!",
            "  sleep 1",
            "  if ! kill -0 \"$agent_pid\" >/dev/null 2>&1; then",
            "    echo \"BlueZ auto-agent failed to start:\"",
            "    cat /tmp/stadia-x-ui-agent.log 2>/dev/null || true",
            "    agent_pid=\"\"",
            "    return 1",
            "  fi",
            "  echo \"BlueZ auto-agent started (pid $agent_pid).\"",
            "  return 0",
            "}",
            "echo \"Step: prepare adapter\"",
            "timeout 5s bluetoothctl power on 2>&1 || true",
            "timeout 5s bluetoothctl pairable on 2>&1 || true",
            "print_state",
            "paired_now=\"$(state_value Paired)\"",
            "connected_now=\"$(state_value Connected)\"",
            "if [ \"${paired_now,,}\" != \"yes\" ] && [ \"${connected_now,,}\" != \"yes\" ] && timeout 3s bluetoothctl info \"$mac\" >/dev/null 2>&1; then",
            "  echo \"Removing stale non-paired BlueZ cache entry for $mac before explicit Pair.\"",
            "  timeout 8s bluetoothctl remove \"$mac\" 2>&1 || true",
            "  sleep 1",
            "fi",
            "if ! timeout 3s bluetoothctl info \"$mac\" >/dev/null 2>&1; then",
            "  echo \"Device is not in BlueZ cache; scanning for 12 seconds.\"",
            "  timeout 14s bluetoothctl --timeout 12 scan on 2>&1 || true",
            "  timeout 2s bluetoothctl scan off >/dev/null 2>&1 || true",
            "fi",
            "echo",
            "echo \"Step: start pairing agent\"",
            "start_agent || timeout 5s bluetoothctl agent NoInputNoOutput 2>&1 || true",
            "timeout 5s bluetoothctl pairable on 2>&1 || true",
            "echo",
            "echo \"Step: pair device\"",
            "timeout 30s bluetoothctl pair \"$mac\" 2>&1 || true",
            "if wait_state Paired yes 18; then",
            "  echo \"Pair confirmed by BlueZ.\"",
            "else",
            "  echo \"Pair was not confirmed by BlueZ within 18 seconds.\"",
            "fi",
            "timeout 8s bluetoothctl trust \"$mac\" 2>&1 || true",
            "echo",
            "echo \"Step: connect device\"",
            "timeout 24s bluetoothctl connect \"$mac\" 2>&1 || true",
            "if wait_state Connected yes 12; then",
            "  echo \"Connect confirmed by BlueZ.\"",
            "else",
            "  echo \"Connect was not confirmed by BlueZ within 12 seconds.\"",
            "fi",
            "print_state",
            "final=\"$(timeout 5s bluetoothctl info \"$mac\" 2>&1 || true)\"",
            "if printf \"%s\\n\" \"$final\" | grep -qi 'Connected:[[:space:]]*yes'; then exit 0; fi",
            "if printf \"%s\\n\" \"$final\" | grep -qi 'Paired:[[:space:]]*yes'; then exit 1; fi",
            "exit 2"
        });
    }

    private static string BuildLinuxBluetoothCommandScript(string mac, params (string Label, string Command, int TimeoutSeconds)[] stages)
    {
        var lines = new List<string>
        {
            "if ! command -v bluetoothctl >/dev/null 2>&1; then echo \"bluetoothctl missing\"; exit 0; fi",
            $"mac='{mac}'",
            "print_state() {",
            "  info=\"$(timeout 5s bluetoothctl info \"$mac\" 2>&1 || true)\"",
            "  paired=\"$(printf \"%s\\n\" \"$info\" | awk -F': ' '/Paired:/ {print $2; exit}')\"",
            "  trusted=\"$(printf \"%s\\n\" \"$info\" | awk -F': ' '/Trusted:/ {print $2; exit}')\"",
            "  connected=\"$(printf \"%s\\n\" \"$info\" | awk -F': ' '/Connected:/ {print $2; exit}')\"",
            "  name=\"$(printf \"%s\\n\" \"$info\" | awk -F': ' '/Name:/ {print $2; exit}')\"",
            "  echo \"State: name=${name:-unknown} paired=${paired:-unknown} trusted=${trusted:-unknown} connected=${connected:-unknown}\"",
            "}",
            "run_step() {",
            "  label=\"$1\"",
            "  timeout_s=\"$2\"",
            "  bt_command=\"$3\"",
            "  echo",
            "  echo \"Step: $label\"",
            "  print_state",
            "  timeout \"${timeout_s}s\" bluetoothctl \"$bt_command\" \"$mac\" 2>&1",
            "  rc=$?",
            "  echo \"Result: $label exit code $rc\"",
            "  print_state",
            "  return 0",
            "}"
        };

        foreach (var stage in stages)
        {
            lines.Add($"run_step \"{stage.Label}\" {Math.Clamp(stage.TimeoutSeconds, 1, 60)} {stage.Command}");
        }

        lines.Add("");
        lines.Add("echo \"Final BlueZ info:\"");
        lines.Add("timeout 5s bluetoothctl info \"$mac\" 2>&1 || true");
        return string.Join("\n", lines);
    }

    private static IReadOnlyList<UsbipdDevice> DemoUsbipdDevices()
    {
        return new[]
        {
            new UsbipdDevice("2-4", "8087:0032", "Intel Wireless Bluetooth", "Shared", true),
            new UsbipdDevice("3-1", "045E:02EA", "Xbox Wireless Adapter", "Not shared", false)
        };
    }

    private static IReadOnlyList<WindowsBluetoothDevice> DemoWindowsBluetoothDevices()
    {
        return new[]
        {
            new WindowsBluetoothDevice("Intel Wireless Bluetooth", "OK", @"USB\VID_8087&PID_0032\DEMO-BT-ADAPTER"),
            new WindowsBluetoothDevice("Bluetooth Device (RFCOMM Protocol TDI)", "OK", @"BTH\MS_RFCOMM\DEMO-RFCOMM"),
            new WindowsBluetoothDevice("Microsoft Bluetooth Enumerator", "OK", @"BTH\MS_BTHBRB\DEMO-ENUM")
        };
    }

    private static IReadOnlyList<LinuxBluetoothDevice> DemoLinuxBluetoothDevices()
    {
        return new[]
        {
            new LinuxBluetoothDevice("E4:17:D8:42:7A:01", "Stadia Controller P1", "yes", "yes", "yes", 84, true, BluetoothDeviceSources.Demo),
            new LinuxBluetoothDevice("E4:17:D8:42:7A:02", "Stadia Controller P2", "no", "yes", "yes", 9, true, BluetoothDeviceSources.Demo),
            new LinuxBluetoothDevice("A8:6B:AD:12:44:90", "8BitDo SN30 Pro", "no", "no", "no", 56, false, BluetoothDeviceSources.Demo)
        };
    }

    private void AddBluetoothDiagnosticsDevices(List<LinuxBluetoothDevice> devices, HashSet<string> seen)
    {
        if (!File.Exists(_paths.BluetoothDiagnostics))
        {
            return;
        }

        if (!IsFreshLiveFallbackFile(_paths.BluetoothDiagnostics))
        {
            AppDiagnosticsLogger.Record("LIVE_FALLBACK_SKIPPED", ("source", "bluetooth-diagnostics"), ("age", FileAgeText(_paths.BluetoothDiagnostics)));
            return;
        }

        var text = ReadFileBestEffort(_paths.BluetoothDiagnostics);
        foreach (Match match in Regex.Matches(text, @"(?ms)^--\s*(?<mac>(?:[0-9A-Fa-f]{2}:){5}[0-9A-Fa-f]{2})\s*--\r?\n(?<info>.*?)(?=^--\s*(?:[0-9A-Fa-f]{2}:){5}[0-9A-Fa-f]{2}\s*--|^==|\z)"))
        {
            var device = ParseLinuxBluetoothInfo(match.Groups["mac"].Value, match.Groups["info"].Value);
            if (device is not null)
            {
                UpsertLinuxDevice(devices, seen, device with
                {
                    Connected = "",
                    Source = BluetoothDeviceSources.Diagnostics
                });
            }
        }
    }

    private void AddBridgeLogDevices(List<LinuxBluetoothDevice> devices, HashSet<string> seen)
    {
        if (!File.Exists(_paths.LinuxLog))
        {
            return;
        }

        if (!IsFreshLiveFallbackFile(_paths.LinuxLog))
        {
            AppDiagnosticsLogger.Record("LIVE_FALLBACK_SKIPPED", ("source", "linux-log"), ("age", FileAgeText(_paths.LinuxLog)));
            return;
        }

        var text = ReadFileBestEffort(_paths.LinuxLog);
        foreach (Match match in Regex.Matches(text, @"Controller\s+(?<mac>(?:[0-9A-Fa-f]{2}:){5}[0-9A-Fa-f]{2})\s+(?:already\s+)?connected", RegexOptions.IgnoreCase))
        {
            var mac = match.Groups["mac"].Value.ToUpperInvariant();
            UpsertLinuxDevice(devices, seen, new LinuxBluetoothDevice(
                mac,
                "Stadia Controller (bridge log)",
                "",
                "",
                "",
                null,
                true,
                BluetoothDeviceSources.BridgeLog));
        }
    }

    private static bool IsFreshLiveFallbackFile(string path)
    {
        try
        {
            var age = DateTime.UtcNow - File.GetLastWriteTimeUtc(path);
            return age >= TimeSpan.Zero && age <= LiveFallbackMaxAge;
        }
        catch
        {
            return false;
        }
    }

    private static string FileAgeText(string path)
    {
        try
        {
            var age = DateTime.UtcNow - File.GetLastWriteTimeUtc(path);
            return age.TotalSeconds < 120
                ? $"{Math.Max(0, (int)age.TotalSeconds)}s"
                : $"{Math.Max(0, (int)age.TotalMinutes)}m";
        }
        catch
        {
            return "unknown";
        }
    }

    private static void UpsertLinuxDevice(List<LinuxBluetoothDevice> devices, HashSet<string> seen, LinuxBluetoothDevice candidate)
    {
        if (!IsMac(candidate.Mac))
        {
            return;
        }

        var mac = candidate.Mac.Trim().ToUpperInvariant();
        var normalized = candidate with { Mac = mac };
        var index = devices.FindIndex(device => device.Mac.Equals(mac, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            seen.Add(mac);
            devices.Add(normalized);
            return;
        }

        devices[index] = MergeLinuxDevice(devices[index], normalized);
        seen.Add(mac);
    }

    private static LinuxBluetoothDevice MergeLinuxDevice(LinuxBluetoothDevice existing, LinuxBluetoothDevice candidate)
    {
        var existingLive = IsLiveBluetoothSource(existing.Source);
        var candidateLive = IsLiveBluetoothSource(candidate.Source);
        return existing with
        {
            Name = BetterValue(existing.Name, candidate.Name, "(unknown)"),
            Connected = BetterStateValue(existing.Connected, candidate.Connected, existingLive, candidateLive),
            Paired = BetterStateValue(existing.Paired, candidate.Paired, existingLive, candidateLive),
            Trusted = BetterStateValue(existing.Trusted, candidate.Trusted, existingLive, candidateLive),
            BatteryPercent = candidate.BatteryPercent ?? existing.BatteryPercent,
            IsStadia = existing.IsStadia || candidate.IsStadia,
            Source = BetterSource(existing.Source, candidate.Source)
        };
    }

    private static string BetterSource(string existing, string candidate)
    {
        if (IsLiveBluetoothSource(candidate))
        {
            return candidate;
        }

        if (IsLiveBluetoothSource(existing))
        {
            return existing;
        }

        return string.IsNullOrWhiteSpace(existing) ? candidate : existing;
    }

    private static bool IsLiveBluetoothSource(string source)
    {
        return string.IsNullOrWhiteSpace(source) ||
               source.Equals(BluetoothDeviceSources.BlueZ, StringComparison.OrdinalIgnoreCase) ||
               source.Equals(BluetoothDeviceSources.Demo, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLiveBluetoothConnected(LinuxBluetoothDevice device)
    {
        return IsLiveBluetoothSource(device.Source) &&
               device.Connected.Equals("yes", StringComparison.OrdinalIgnoreCase);
    }

    private static string BetterStateValue(string existing, string candidate, bool existingLive, bool candidateLive)
    {
        existing = NormalizeYesNo(existing);
        candidate = NormalizeYesNo(candidate);

        if (candidateLive && !existingLive)
        {
            return candidate;
        }

        if (existingLive && !candidateLive)
        {
            return existing;
        }

        if (candidate.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
            existing.Equals("yes", StringComparison.OrdinalIgnoreCase))
        {
            return "yes";
        }

        if (candidate.Equals("no", StringComparison.OrdinalIgnoreCase) ||
            existing.Equals("no", StringComparison.OrdinalIgnoreCase))
        {
            return "no";
        }

        return string.IsNullOrWhiteSpace(existing) ? candidate : existing;
    }

    private static string NormalizeYesNo(string value)
    {
        value = value.Trim();
        return value.Equals("yes", StringComparison.OrdinalIgnoreCase) ? "yes" :
            value.Equals("no", StringComparison.OrdinalIgnoreCase) ? "no" :
            value;
    }

    private static string BetterValue(string existing, string candidate, string emptyValue)
    {
        if (string.IsNullOrWhiteSpace(candidate) || candidate == "-" || candidate.Equals(emptyValue, StringComparison.OrdinalIgnoreCase))
        {
            return existing;
        }

        if (string.IsNullOrWhiteSpace(existing) || existing == "-" || existing.Equals(emptyValue, StringComparison.OrdinalIgnoreCase))
        {
            return candidate;
        }

        return existing;
    }

    private static string TruncateForDiagnostics(string value, int maxLength)
    {
        value = value.Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();
        return value.Length <= maxLength ? value : value[..maxLength] + "...";
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return "";
    }

    private static string ReadFileBestEffort(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return reader.ReadToEnd();
        }
        catch
        {
            return "";
        }
    }

    public async Task<CommandResult> RunLinuxBluetoothRepairAsync()
    {
        var distro = await _wslResolver.ResolveAsync().ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(distro))
        {
            return new CommandResult(-1, "", "No WSL distro resolved.");
        }

        const string script = """
if ! command -v bluetoothctl >/dev/null 2>&1; then
  echo "bluetoothctl missing"
  exit 0
fi
bluetoothctl scan off 2>&1 || true
bluetoothctl power off 2>&1 || true
sleep 2
bluetoothctl power on 2>&1 || true
bluetoothctl agent NoInputNoOutput 2>&1 || true
bluetoothctl default-agent 2>&1 || true
bluetoothctl show 2>&1 || true
bluetoothctl devices 2>&1 || true
""";
        return await _runner.RunAsync("wsl", new[] { "-d", distro, "--exec", "bash", "-lc", script }, _paths.Root, 30000).ConfigureAwait(false);
    }

    public async Task<string> CreateCapacityReportAsync()
    {
        Directory.CreateDirectory(_paths.LogDirectory);
        var usb = await GetUsbipdDevicesAsync().ConfigureAwait(false);
        var windowsBt = await GetWindowsBluetoothDevicesAsync().ConfigureAwait(false);
        var linuxBt = await GetLinuxBluetoothDevicesAsync(0).ConfigureAwait(false);
        var selectedBus = GetSelectedBluetoothBusId();
        var selectedDevice = usb.FirstOrDefault(d => d.BusId.Equals(selectedBus, StringComparison.OrdinalIgnoreCase)) ??
                             usb.FirstOrDefault(d => d.IsBluetooth);
        var profiles = GetProfiles().Where(p => p.AutoConnect).OrderBy(p => p.Slot).ToArray();
        var path = Path.Combine(_paths.LogDirectory, "capacity-wizard.txt");
        var lines = new List<string>
        {
            "Stadia X controller capacity report",
            $"Created: {DateTimeOffset.Now:o}",
            $"Selected BUSID: {EmptyAsNone(selectedBus)}",
            $"Selected adapter: {(selectedDevice is null ? "none" : selectedDevice.Display)}",
            EstimateCapacity(selectedDevice, windowsBt),
            $"Windows Bluetooth active/OK devices: {windowsBt.Count(d => d.Status.Equals("OK", StringComparison.OrdinalIgnoreCase))}",
            $"Linux visible Stadia devices: {linuxBt.Count(d => d.IsStadia)}",
            $"Auto-connect profiles: {profiles.Length}",
            "",
            "Linux devices:",
        };
        lines.AddRange(linuxBt.Select(d => $"- {d.Mac} {d.Name} source={d.Source} connected={d.Connected} paired={d.Paired} battery={(d.BatteryPercent is null ? "unknown" : d.BatteryPercent + "%")}"));
        await File.WriteAllTextAsync(path, string.Join(Environment.NewLine, lines) + Environment.NewLine).ConfigureAwait(false);
        return path;
    }

    public IReadOnlyList<string> GetSelectedControllerMacs()
    {
        if (!File.Exists(_paths.SelectedControllerMacs))
        {
            return Array.Empty<string>();
        }

        return File.ReadAllText(_paths.SelectedControllerMacs)
            .Split(new[] { ',', ';', ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(IsMac)
            .Take(4)
            .Select(m => m.ToUpperInvariant())
            .ToArray();
    }

    public void SaveSelectedControllerMacs(IEnumerable<string> macs)
    {
        var clean = macs.Where(IsMac).Select(m => m.ToUpperInvariant()).Distinct(StringComparer.OrdinalIgnoreCase).Take(4).ToArray();
        if (clean.Length == 0)
        {
            if (File.Exists(_paths.SelectedControllerMacs))
            {
                File.Delete(_paths.SelectedControllerMacs);
            }
            return;
        }

        File.WriteAllText(_paths.SelectedControllerMacs, string.Join(",", clean) + Environment.NewLine, Encoding.ASCII);
    }

    public IReadOnlyList<ControllerProfile> GetProfiles()
    {
        if (!File.Exists(_paths.ControllerProfiles))
        {
            return Array.Empty<ControllerProfile>();
        }

        try
        {
            return JsonSerializer.Deserialize<List<ControllerProfile>>(File.ReadAllText(_paths.ControllerProfiles)) ?? new List<ControllerProfile>();
        }
        catch
        {
            var backup = _paths.ControllerProfiles + "." + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".bak";
            File.Copy(_paths.ControllerProfiles, backup, overwrite: true);
            return Array.Empty<ControllerProfile>();
        }
    }

    public void SaveProfiles(IEnumerable<ControllerProfile> profiles)
    {
        var clean = profiles
            .Where(p => !string.IsNullOrWhiteSpace(p.Name) && IsMac(p.Mac))
            .Select(p => new ControllerProfile(p.Name.Trim(), p.Mac.Trim().ToUpperInvariant(), Math.Clamp(p.Slot, 1, 4), p.AutoConnect))
            .GroupBy(p => p.Slot)
            .Select(g => g.Last())
            .OrderBy(p => p.Slot)
            .ToArray();
        File.WriteAllText(_paths.ControllerProfiles, JsonSerializer.Serialize(clean, new JsonSerializerOptions { WriteIndented = true }));
    }

    public void ApplyAutoConnectProfiles()
    {
        SaveSelectedControllerMacs(GetProfiles().Where(p => p.AutoConnect).OrderBy(p => p.Slot).Select(p => p.Mac));
    }

    public IReadOnlyList<MacroMapping> LoadMacroMappings()
    {
        if (!File.Exists(_paths.MacroConfig))
        {
            return Array.Empty<MacroMapping>();
        }

        var mappings = new List<MacroMapping>();
        var inButtons = false;
        foreach (var rawLine in File.ReadLines(_paths.MacroConfig))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#'))
            {
                continue;
            }
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                inButtons = line.Equals("[Buttons]", StringComparison.OrdinalIgnoreCase);
                continue;
            }
            if (!inButtons)
            {
                continue;
            }

            var equals = line.IndexOf('=');
            if (equals <= 0)
            {
                continue;
            }

            mappings.Add(new MacroMapping(line[..equals].Trim(), line[(equals + 1)..].Trim()));
        }

        return mappings;
    }

    public string LoadMacroText()
    {
        return File.Exists(_paths.MacroConfig) ? File.ReadAllText(_paths.MacroConfig) : "[Buttons]" + Environment.NewLine;
    }

    public void SaveMacroText(string text)
    {
        if (File.Exists(_paths.MacroConfig))
        {
            var backup = _paths.MacroConfig + "." + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".bak";
            File.Copy(_paths.MacroConfig, backup, overwrite: true);
        }

        File.WriteAllText(_paths.MacroConfig, text);
    }

    public ControllerTelemetrySnapshot ReadControllerTelemetry()
    {
        if (!File.Exists(_paths.ControllerState))
        {
            return ReadLiveLinuxInputTelemetryOrEmpty(DateTimeOffset.Now);
        }

        var dataTimestamp = ControllerTelemetryTimestamp(_paths.ControllerState);
        if (!IsControllerTelemetryFileFresh(_paths.ControllerState))
        {
            return ReadLiveLinuxInputTelemetryOrEmpty(dataTimestamp);
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(File.ReadAllText(_paths.ControllerState));
        }
        catch (IOException)
        {
            return ReadLiveLinuxInputTelemetryOrEmpty(dataTimestamp);
        }
        catch (JsonException)
        {
            return ReadLiveLinuxInputTelemetryOrEmpty(dataTimestamp);
        }

        using (document)
        {
            if (!document.RootElement.TryGetProperty("controllers", out var controllers) || controllers.ValueKind != JsonValueKind.Array)
            {
                return ReadLiveLinuxInputTelemetryOrEmpty(dataTimestamp);
            }

            var snapshot = ReadControllerRows(controllers, dataTimestamp);
            return snapshot.Controllers.Count > 0 ? snapshot : ReadLiveLinuxInputTelemetryOrEmpty(dataTimestamp);
        }
    }

    private ControllerTelemetrySnapshot ReadLiveLinuxInputTelemetryOrEmpty(DateTimeOffset fallbackTimestamp)
    {
        var live = TryReadLiveLinuxInputTelemetry();
        return live ?? new ControllerTelemetrySnapshot(fallbackTimestamp, Array.Empty<ControllerTelemetryRow>());
    }

    private ControllerTelemetrySnapshot? TryReadLiveLinuxInputTelemetry()
    {
        var now = DateTime.UtcNow;
        var probeInterval = _lastLiveInputSnapshot?.Controllers.Count > 0
            ? TimeSpan.FromSeconds(1.5)
            : TimeSpan.FromSeconds(5);
        if (_lastLiveInputProbeUtc > now - probeInterval)
        {
            return _lastLiveInputSnapshot?.Controllers.Count > 0 ? _lastLiveInputSnapshot : null;
        }

        _lastLiveInputProbeUtc = now;
        var distro = ResolveLiveInputDistroQuick();
        if (string.IsNullOrWhiteSpace(distro))
        {
            return null;
        }

        const string script = """
import fcntl
import glob
import json
import os
import re
import struct

EVIOCGABS_BASE=(2<<30)|(24<<16)|(0x45<<8)|0x40
EVIOCGKEY=(2<<30)|(96<<16)|(0x45<<8)|0x18
ABS_X=0
ABS_Y=1
ABS_Z=2
ABS_RZ=5
ABS_GAS=9
ABS_BRAKE=10
ABS_HAT0X=16
ABS_HAT0Y=17
KEYS={
    'a':0x130,
    'b':0x131,
    'x':0x133,
    'y':0x134,
    'lb':0x136,
    'rb':0x137,
    'select':0x13a,
    'start':0x13b,
    'stadia':0x13c,
    'l3':0x13d,
    'r3':0x13e,
    'assistant':0x2c0,
}

def stadia_events():
    events=[]
    try:
        text=open('/proc/bus/input/devices','r',encoding='utf-8',errors='ignore').read()
    except Exception:
        text=''
    for block in text.split('\n\n'):
        if 'Stadia' not in block and '18D1:9400' not in block and '18d1:9400' not in block:
            continue
        handlers=re.search(r'H: Handlers=(.*)', block)
        if not handlers:
            continue
        for handler in handlers.group(1).split():
            if handler.startswith('event'):
                path='/dev/input/'+handler
                if os.path.exists(path):
                    events.append(path)
    if events:
        return sorted(dict.fromkeys(events))
    return sorted(glob.glob('/dev/input/event*'))

def abs_info(fd, code):
    try:
        data=fcntl.ioctl(fd, EVIOCGABS_BASE+code, b'\0'*24)
        value,minv,maxv,fuzz,flat,res=struct.unpack('iiiiii', data)
        return value,minv,maxv
    except OSError:
        return None

def stick_value(info):
    if not info:
        return 0
    value,minv,maxv=info
    if value == 0 and minv == 1 and maxv == 255:
        value = 128
    centered = value - 128
    scaled = int((centered * 32767) / 127)
    return max(-32767, min(32767, scaled))

def trigger_value(info):
    if not info:
        return 0
    value,_,_=info
    return max(0, min(255, int(value)))

def key_state(fd):
    buttons={name:False for name in KEYS}
    try:
        bits=fcntl.ioctl(fd, EVIOCGKEY, b'\0'*96)
    except OSError:
        return buttons
    for name,code in KEYS.items():
        buttons[name]=bool(bits[code//8] & (1 << (code % 8)))
    return buttons

controllers=[]
for index,path in enumerate(stadia_events()[:4]):
    try:
        fd=os.open(path, os.O_RDONLY|os.O_NONBLOCK)
    except OSError:
        continue
    buttons=key_state(fd)
    hatx=abs_info(fd, ABS_HAT0X)
    haty=abs_info(fd, ABS_HAT0Y)
    if hatx:
        buttons['dpad_left']=hatx[0] < 0
        buttons['dpad_right']=hatx[0] > 0
    else:
        buttons['dpad_left']=False
        buttons['dpad_right']=False
    if haty:
        buttons['dpad_up']=haty[0] < 0
        buttons['dpad_down']=haty[0] > 0
    else:
        buttons['dpad_up']=False
        buttons['dpad_down']=False
    axes={
        'trigger_left':trigger_value(abs_info(fd, ABS_BRAKE)),
        'trigger_right':trigger_value(abs_info(fd, ABS_GAS)),
        'stick_lx':stick_value(abs_info(fd, ABS_X)),
        'stick_ly':stick_value(abs_info(fd, ABS_Y)),
        'stick_rx':stick_value(abs_info(fd, ABS_Z)),
        'stick_ry':stick_value(abs_info(fd, ABS_RZ)),
    }
    os.close(fd)
    controllers.append({
        'index':index,
        'active':True,
        'last_seen_age_ms':0,
        'packets':1,
        'pps':0,
        'buttons':buttons,
        'axes':axes,
    })
print(json.dumps({'controllers':controllers}, separators=(',',':')))
""";

        try
        {
            var result = RunLiveInputProbe(distro, script, 1200);

            if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.Output))
            {
                RecordLiveInputTelemetryFailure("command", distro, FirstNonEmpty(result.Error, result.Output, "no output"));
                return null;
            }

            using var document = JsonDocument.Parse(result.Output);
            if (!document.RootElement.TryGetProperty("controllers", out var controllers) || controllers.ValueKind != JsonValueKind.Array)
            {
                RecordLiveInputTelemetryFailure("json", distro, "missing controllers array");
                return null;
            }

            var snapshot = ReadControllerRows(controllers, DateTimeOffset.Now);
            if (snapshot.Controllers.Count > 0)
            {
                _lastLiveInputSnapshot = snapshot;
                RecordLiveInputTelemetrySuccess(distro, snapshot.Controllers.Count);
                return snapshot;
            }

            _lastLiveInputSnapshot = null;
            return null;
        }
        catch (Exception ex)
        {
            RecordLiveInputTelemetryFailure(ex.GetType().Name, distro, ex.Message);
            return null;
        }
    }

    private CommandResult RunLiveInputProbe(string distro, string script, int timeoutMilliseconds)
    {
        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "wsl",
            WorkingDirectory = _paths.Root,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var argument in new[] { "-d", distro, "-u", "root", "--exec", "python3", "-c", script })
        {
            startInfo.ArgumentList.Add(argument);
        }

        try
        {
            using var process = new System.Diagnostics.Process { StartInfo = startInfo };
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
            if (!process.WaitForExit(timeoutMilliseconds))
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                return new CommandResult(-1, output.ToString(), $"Timed out after {timeoutMilliseconds} ms.");
            }

            process.WaitForExit();
            return new CommandResult(process.ExitCode, output.ToString(), error.ToString());
        }
        catch (Exception ex)
        {
            return new CommandResult(-1, "", ex.Message);
        }
    }

    private string ResolveLiveInputDistroQuick()
    {
        var selected = GetSelectedWslDistro();
        if (!string.IsNullOrWhiteSpace(selected))
        {
            _liveInputDistroCache = selected;
            return selected;
        }

        if (!string.IsNullOrWhiteSpace(_liveInputDistroCache))
        {
            return _liveInputDistroCache;
        }

        try
        {
            var result = _runner.RunAsync("wsl.exe", new[] { "-l", "-q" }, _paths.Root, 1200).GetAwaiter().GetResult();
            if (result.ExitCode == 0)
            {
                var distros = result.Output
                    .Replace("\0", "", StringComparison.Ordinal)
                    .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(line => line.Trim().TrimStart('*').Trim())
                    .Where(IsSafeDistroName)
                    .ToArray();
                var ubuntu = distros.FirstOrDefault(d => d.StartsWith("Ubuntu", StringComparison.OrdinalIgnoreCase));
                _liveInputDistroCache = ubuntu ?? distros.FirstOrDefault() ?? "";
            }
        }
        catch (Exception ex)
        {
            RecordLiveInputTelemetryFailure("distro", "", ex.Message);
        }

        if (!string.IsNullOrWhiteSpace(_liveInputDistroCache))
        {
            return _liveInputDistroCache;
        }

        _liveInputDistroCache = "Ubuntu";
        return _liveInputDistroCache;
    }

    private void RecordLiveInputTelemetrySuccess(string distro, int count)
    {
        if (_lastLiveInputTelemetryLogUtc > DateTime.UtcNow - TimeSpan.FromSeconds(15))
        {
            return;
        }

        _lastLiveInputTelemetryLogUtc = DateTime.UtcNow;
        AppDiagnosticsLogger.Record(
            "CONTROLLER_TELEMETRY_LIVE_INPUT_FALLBACK",
            ("distro", distro),
            ("controllers", count.ToString()));
    }

    private void RecordLiveInputTelemetryFailure(string stage, string distro, string detail)
    {
        if (_lastLiveInputTelemetryFailureLogUtc > DateTime.UtcNow - TimeSpan.FromSeconds(15))
        {
            return;
        }

        _lastLiveInputTelemetryFailureLogUtc = DateTime.UtcNow;
        AppDiagnosticsLogger.Record(
            "CONTROLLER_TELEMETRY_LIVE_INPUT_FAILED",
            ("stage", stage),
            ("distro", distro),
            ("detail", TruncateForDiagnostics(detail, 260)));
    }

    public static bool IsControllerTelemetryFileFresh(string path)
    {
        return File.Exists(path) &&
               File.GetLastWriteTimeUtc(path) >= DateTime.UtcNow - ControllerTelemetryMaxAge;
    }

    private static DateTimeOffset ControllerTelemetryTimestamp(string path)
    {
        try
        {
            var timestamp = File.GetLastWriteTimeUtc(path);
            return timestamp.Year <= 1900 ? DateTimeOffset.Now : new DateTimeOffset(timestamp, TimeSpan.Zero);
        }
        catch
        {
            return DateTimeOffset.Now;
        }
    }

    private static ControllerTelemetrySnapshot ReadControllerRows(JsonElement controllers, DateTimeOffset dataTimestamp)
    {
        var rows = new List<ControllerTelemetryRow>();
        var fileAge = DateTimeOffset.UtcNow - dataTimestamp.ToUniversalTime();
        var fileAgeMs = Math.Max(0d, fileAge.TotalMilliseconds);
        foreach (var item in controllers.EnumerateArray())
        {
            var buttons = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            if (item.TryGetProperty("buttons", out var buttonNode))
            {
                foreach (var prop in buttonNode.EnumerateObject())
                {
                    buttons[prop.Name] = prop.Value.ValueKind == JsonValueKind.True;
                }
            }

            var axes = item.TryGetProperty("axes", out var axisNode) ? axisNode : default;
            var effectiveLastSeenAgeMs = Math.Max(0L, ReadLong(item, "last_seen_age_ms")) + fileAgeMs;
            var active = ReadBool(item, "active") && effectiveLastSeenAgeMs < ControllerActiveMaxAge.TotalMilliseconds;
            rows.Add(new ControllerTelemetryRow(
                ReadInt(item, "index") + 1,
                active,
                active ? ReadDouble(item, "pps") : 0d,
                (ulong)Math.Max(0, ReadLong(item, "packets")),
                ReadInt(axes, "trigger_left"),
                ReadInt(axes, "trigger_right"),
                ReadInt(axes, "stick_lx"),
                ReadInt(axes, "stick_ly"),
                ReadInt(axes, "stick_rx"),
                ReadInt(axes, "stick_ry"),
                buttons));
        }

        return new ControllerTelemetrySnapshot(dataTimestamp, rows);
    }

    public async Task<string> CreateSessionReportAsync()
    {
        Directory.CreateDirectory(_paths.SupportBundleDirectory);
        var reportPath = Path.Combine(_paths.SupportBundleDirectory, "StadiaX-session-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".md");
        var devices = await GetUsbipdDevicesAsync().ConfigureAwait(false);
        var winBt = await GetWindowsBluetoothDevicesAsync().ConfigureAwait(false);
        var selectedMacs = GetSelectedControllerMacs();

        var lines = new List<string>
        {
            "# Stadia X Session Report",
            "",
            $"- Created: {DateTimeOffset.Now:o}",
            $"- Root: {_paths.Root}",
            $"- Version: {_paths.Version}",
            $"- Selected Bluetooth BUSID: {EmptyAsNone(GetSelectedBluetoothBusId())}",
            $"- Selected WSL distro: {EmptyAsNone(GetSelectedWslDistro())}",
            $"- Selected controller MACs: {(selectedMacs.Count > 0 ? string.Join(", ", selectedMacs) : "automatic")}",
            $"- usbipd devices: {devices.Count}",
            $"- Windows Bluetooth devices: {winBt.Count}",
            "",
            "## Recent Status",
            "```",
            LogReader.Tail(_paths.StatusLog, 80),
            "```",
            "",
            "## Recent User Actions",
            "```",
            LogReader.Tail(_paths.UserActionLog, 80),
            "```",
            "",
            "## Recent App Diagnostics",
            "```",
            LogReader.Tail(_paths.AppDiagnosticsLog, 80),
            "```",
            "",
            "## Recent Linux Log",
            "```",
            LogReader.Tail(_paths.LinuxLog, 80),
            "```"
        };

        await File.WriteAllTextAsync(reportPath, string.Join(Environment.NewLine, lines)).ConfigureAwait(false);
        return reportPath;
    }

    public async Task<string> CreateSupportBundleAsync()
    {
        Directory.CreateDirectory(_paths.SupportBundleDirectory);
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + AppDiagnosticsLogger.CurrentSessionId;
        var workDir = Path.Combine(_paths.SupportBundleDirectory, "StadiaX-support-" + stamp);
        Directory.CreateDirectory(workDir);
        var manifest = new List<string>
        {
            "Stadia X support bundle manifest",
            $"created={DateTimeOffset.Now:O}",
            $"version={_paths.Version}",
            $"session={AppDiagnosticsLogger.CurrentSessionId}",
            ""
        };

        foreach (var path in new[]
        {
            _paths.StatusLog,
            _paths.LinuxStatusLog,
            _paths.LinuxLog,
            _paths.UserActionLog,
            _paths.AppDiagnosticsLog,
            _paths.BluetoothDiagnostics,
            _paths.ReceiverLog,
            _paths.ControllerState,
            _paths.SelectedBluetoothBusId,
            _paths.SelectedControllerMacs,
            _paths.SelectedWslDistro,
            _paths.ControllerProfiles,
            _paths.MacroConfig,
            _paths.VersionFile
        })
        {
            CopySupportFile(path, workDir, manifest);
        }

        var report = await CreateSessionReportAsync().ConfigureAwait(false);
        CopySupportFile(report, workDir, manifest, "session-report.md");
        var commandReport = new StringBuilder();
        foreach (var command in new[]
        {
            ("usbipd", new[] { "list" }),
            ("wsl", new[] { "-l", "-v" })
        })
        {
            commandReport.AppendLine("== " + command.Item1 + " " + string.Join(" ", command.Item2) + " ==");
            var result = await _runner.RunAsync(command.Item1, command.Item2, _paths.Root, 15000).ConfigureAwait(false);
            commandReport.AppendLine(result.Output.Trim());
            commandReport.AppendLine(result.Error.Trim());
            commandReport.AppendLine();
        }
        var commandsPath = Path.Combine(workDir, "commands.txt");
        await File.WriteAllTextAsync(commandsPath, commandReport.ToString()).ConfigureAwait(false);
        manifest.Add($"GENERATED | {Path.GetFileName(commandsPath)} | bytes={new FileInfo(commandsPath).Length}");
        var manifestPath = Path.Combine(workDir, "bundle-manifest.txt");
        await File.WriteAllLinesAsync(manifestPath, manifest).ConfigureAwait(false);

        var zipPath = workDir + ".zip";
        if (File.Exists(zipPath))
        {
            File.Delete(zipPath);
        }
        ZipFile.CreateFromDirectory(workDir, zipPath, CompressionLevel.Optimal, includeBaseDirectory: false);
        try
        {
            Directory.Delete(workDir, recursive: true);
        }
        catch (Exception ex)
        {
            AppDiagnosticsLogger.Record(
                "SUPPORT_BUNDLE_STAGING_CLEANUP_WARN",
                ("directory", workDir),
                ("error", ex.Message));
        }

        AppDiagnosticsLogger.Record(
            "SUPPORT_BUNDLE_CREATED",
            ("path", zipPath),
            ("bytes", new FileInfo(zipPath).Length.ToString()),
            ("copied", manifest.Count(line => line.StartsWith("COPIED", StringComparison.Ordinal)).ToString()),
            ("skipped", manifest.Count(line => line.StartsWith("SKIPPED", StringComparison.Ordinal)).ToString()));
        return zipPath;
    }

    private static void CopySupportFile(string sourcePath, string destinationDirectory, ICollection<string> manifest, string? destinationName = null)
    {
        destinationName ??= Path.GetFileName(sourcePath);
        if (!File.Exists(sourcePath))
        {
            manifest.Add($"MISSING | {destinationName}");
            return;
        }

        try
        {
            var destinationPath = Path.Combine(destinationDirectory, Path.GetFileName(destinationName));
            using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var destination = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.Read);
            source.CopyTo(destination);
            manifest.Add($"COPIED | {Path.GetFileName(destinationPath)} | bytes={destination.Length} | modified={File.GetLastWriteTimeUtc(sourcePath):O}");
        }
        catch (Exception ex)
        {
            manifest.Add($"SKIPPED | {destinationName} | reason={SanitizeManifestValue(ex.Message)}");
            AppDiagnosticsLogger.Record(
                "SUPPORT_BUNDLE_FILE_SKIPPED",
                ("file", destinationName),
                ("error", ex.Message));
        }
    }

    private static string SanitizeManifestValue(string value)
    {
        return value.Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Replace("|", "/", StringComparison.Ordinal)
            .Trim();
    }

    public static string EstimateCapacity(UsbipdDevice? device, IReadOnlyList<WindowsBluetoothDevice> windowsDevices)
    {
        if (device is null)
        {
            return "No Bluetooth adapter selected.";
        }

        var name = device.Name.ToLowerInvariant();
        var activeCount = windowsDevices.Count(d => d.Status.Equals("OK", StringComparison.OrdinalIgnoreCase));
        var estimate = 2;
        var confidence = "medium";
        if (name.Contains("intel") || name.Contains("qualcomm") || name.Contains("mediatek") || name.Contains("bluetooth 5"))
        {
            estimate = 4;
            confidence = "good";
        }
        else if (name.Contains("realtek") || name.Contains("broadcom"))
        {
            estimate = 3;
        }
        else if (name.Contains("generic") || name.Contains("csr") || name.Contains("4.0"))
        {
            estimate = 2;
            confidence = "low";
        }

        if (activeCount > 2)
        {
            estimate = Math.Max(1, estimate - 1);
        }

        return $"Estimated Stadia capacity: {estimate}/4 controller(s), confidence {confidence}. Active Windows Bluetooth devices: {activeCount}.";
    }

    private static bool LooksLikeBluetooth(string value)
    {
        return value.Contains("bluetooth", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("intel wireless", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("realtek", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("mediatek", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("qualcomm", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("broadcom", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<LinuxBluetoothDevice?> GetLinuxBluetoothDeviceByInfoAsync(string distro, string mac)
    {
        if (!IsMac(mac))
        {
            return null;
        }

        var result = await _runner.RunAsync(
            "wsl",
            new[] { "-d", distro, "--exec", "bash", "-lc", $"timeout 3s bluetoothctl info '{mac}' 2>/dev/null || true" },
            _paths.Root,
            5000).ConfigureAwait(false);

        return ParseLinuxBluetoothInfo(mac, result.Output);
    }

    private static LinuxBluetoothDevice? ParseLinuxBluetoothInfo(string mac, string info)
    {
        if (!IsMac(mac) ||
            string.IsNullOrWhiteSpace(info) ||
            info.Contains("not available", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var name = InfoValue(info, "Name");
        if (string.IsNullOrWhiteSpace(name))
        {
            name = InfoValue(info, "Alias");
        }

        var connected = InfoValue(info, "Connected");
        var paired = InfoValue(info, "Paired");
        var trusted = InfoValue(info, "Trusted");
        var battery = ParseBatteryPercent(info);
        var isStadia = name.Contains("stadia", StringComparison.OrdinalIgnoreCase) ||
                       info.Contains("Modalias: usb:v18D1p9400", StringComparison.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(name) &&
            string.IsNullOrWhiteSpace(connected) &&
            string.IsNullOrWhiteSpace(paired) &&
            string.IsNullOrWhiteSpace(trusted))
        {
            return null;
        }

        return new LinuxBluetoothDevice(
            mac.Trim().ToUpperInvariant(),
            string.IsNullOrWhiteSpace(name) ? "(unknown)" : name.Trim(),
            connected.Trim(),
            paired.Trim(),
            trusted.Trim(),
            battery,
            isStadia);
    }

    private static string InfoValue(string info, string key)
    {
        foreach (var rawLine in info.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (line.StartsWith(key + ":", StringComparison.OrdinalIgnoreCase))
            {
                return line[(key.Length + 1)..].Trim();
            }
        }

        return "";
    }

    private static int? ParseBatteryPercent(string info)
    {
        var match = Regex.Match(info, @"Battery Percentage:\s+0x[0-9A-Fa-f]+\s+\((\d+)\)");
        return match.Success && int.TryParse(match.Groups[1].Value, out var percent)
            ? Math.Clamp(percent, 0, 100)
            : null;
    }

    private static bool IsBusId(string? value)
    {
        return !string.IsNullOrWhiteSpace(value) && BusIdPattern.IsMatch(value.Trim());
    }

    public static bool IsBluetoothMac(string? value) => IsMac(value);

    private static bool IsMac(string? value)
    {
        return !string.IsNullOrWhiteSpace(value) && MacPattern.IsMatch(value.Trim());
    }

    private static bool IsSafeDistroName(string? name)
    {
        return !string.IsNullOrWhiteSpace(name) && name.Trim().All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '.' or '-');
    }

    private static string EmptyAsNone(string value) => string.IsNullOrWhiteSpace(value) ? "none" : value;

    private static byte[] BuildRumblePacket(int controllerIndex, byte largeMotor, byte smallMotor)
    {
        return new[]
        {
            PacketMagic,
            PacketVersion,
            (byte)controllerIndex,
            (byte)0,
            largeMotor,
            smallMotor
        };
    }

    private static int ReadInt(JsonElement element, string property)
    {
        return element.ValueKind != JsonValueKind.Undefined &&
               element.TryGetProperty(property, out var value) &&
               value.TryGetInt32(out var number)
            ? number
            : 0;
    }

    private static long ReadLong(JsonElement element, string property)
    {
        return element.ValueKind != JsonValueKind.Undefined &&
               element.TryGetProperty(property, out var value) &&
               value.TryGetInt64(out var number)
            ? number
            : 0;
    }

    private static double ReadDouble(JsonElement element, string property)
    {
        return element.ValueKind != JsonValueKind.Undefined &&
               element.TryGetProperty(property, out var value) &&
               value.TryGetDouble(out var number)
            ? number
            : 0;
    }

    private static bool ReadBool(JsonElement element, string property)
    {
        return element.ValueKind != JsonValueKind.Undefined &&
               element.TryGetProperty(property, out var value) &&
               value.ValueKind == JsonValueKind.True;
    }
}
