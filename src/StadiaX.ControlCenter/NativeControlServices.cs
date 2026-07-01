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

internal sealed record LinuxBluetoothDevice(string Mac, string Name, string Connected, string Paired, string Trusted, int? BatteryPercent, bool IsStadia);

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
    private static readonly Regex BusIdPattern = new(@"^\d+-\d+$", RegexOptions.Compiled);
    private static readonly Regex MacPattern = new(@"^[0-9A-Fa-f]{2}(:[0-9A-Fa-f]{2}){5}$", RegexOptions.Compiled);
    private static bool IsBluetoothDemoMode =>
        string.Equals(Environment.GetEnvironmentVariable("STADIAX_DEMO_BLUETOOTH"), "1", StringComparison.OrdinalIgnoreCase);

    private readonly AppPaths _paths;
    private readonly ProcessRunner _runner;
    private readonly WslDistroResolver _wslResolver;

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

        scanSeconds = Math.Clamp(scanSeconds, 0, 20);
        var infoTimeoutSeconds = scanSeconds > 0 ? 3 : 1;
        var listTimeoutSeconds = scanSeconds > 0 ? 4 : 2;
        var commandTimeoutMs = scanSeconds > 0 ? Math.Max(15000, (scanSeconds + 8) * 1000) : 7000;
        var scanCommand = scanSeconds > 0
            ? $"timeout {scanSeconds + 2} bluetoothctl --timeout {scanSeconds} scan on >/dev/null 2>&1 || true; "
            : "";
        var knownMacs = string.Join(" ",
            GetSelectedControllerMacs()
                .Concat(GetProfiles().Select(p => p.Mac))
                .Where(IsMac)
                .Select(m => m.ToUpperInvariant())
                .Distinct(StringComparer.OrdinalIgnoreCase));
        var knownMacLoop = string.IsNullOrWhiteSpace(knownMacs)
            ? ""
            : $"for mac in {knownMacs}; do emit_device \"$mac\" \"\"; done; ";

        var script =
            "if ! command -v bluetoothctl >/dev/null 2>&1; then echo \"__ERROR__|bluetoothctl missing\"; exit 0; fi; " +
            scanCommand +
            "emit_device() { " +
            "mac=\"$1\"; name=\"$2\"; [ -z \"$mac\" ] && return; " +
            $"info=\"$(timeout {infoTimeoutSeconds}s bluetoothctl info \"$mac\" 2>/dev/null || true)\"; " +
            "printf \"%s\\n\" \"$info\" | grep -Eiq \"Name:|Alias:|Connected:|UUID:\" || [ -n \"$name\" ] || return; " +
            "[ -z \"$name\" ] && name=\"$(printf \"%s\\n\" \"$info\" | awk -F': ' '/Name:/ {print $2; exit}')\"; " +
            "paired=\"$(printf \"%s\\n\" \"$info\" | awk -F': ' '/Paired:/ {print $2; exit}')\"; " +
            "trusted=\"$(printf \"%s\\n\" \"$info\" | awk -F': ' '/Trusted:/ {print $2; exit}')\"; " +
            "connected=\"$(printf \"%s\\n\" \"$info\" | awk -F': ' '/Connected:/ {print $2; exit}')\"; " +
            "battery=\"$(printf \"%s\\n\" \"$info\" | awk -F'[()]' '/Battery Percentage:/ {print $2; exit}')\"; " +
            "printf '%s|%s|%s|%s|%s|%s\\n' \"$mac\" \"$name\" \"$paired\" \"$trusted\" \"$connected\" \"$battery\"; " +
            "}; " +
            "emit_list() { " +
            "filter=\"$1\"; " +
            $"if [ -z \"$filter\" ]; then timeout {listTimeoutSeconds}s bluetoothctl devices 2>/dev/null; else timeout {listTimeoutSeconds}s bluetoothctl devices \"$filter\" 2>/dev/null; fi | while IFS= read -r line; do " +
            "mac=\"$(printf '%s\\n' \"$line\" | grep -Eo '([0-9A-Fa-f]{2}:){5}[0-9A-Fa-f]{2}' | head -n 1)\"; " +
            "[ -z \"$mac\" ] && continue; " +
            "name=\"${line#*$mac}\"; name=\"${name# }\"; " +
            "emit_device \"$mac\" \"$name\"; " +
            "done; " +
            "}; " +
            "emit_list \"\"; emit_list Connected; emit_list Paired; emit_list Trusted; emit_list Bonded; " +
            knownMacLoop;

        var result = await _runner.RunAsync("wsl", new[] { "-d", distro, "--", "bash", "-lc", script }, _paths.Root, commandTimeoutMs).ConfigureAwait(false);
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
                parts[4].Trim(),
                parts[2].Trim(),
                parts[3].Trim(),
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
            .ThenByDescending(d => d.Connected.Equals("yes", StringComparison.OrdinalIgnoreCase))
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

        command = command.ToLowerInvariant() switch
        {
            "pair" => $"bluetoothctl trust '{mac}'; timeout 20 bluetoothctl pair '{mac}' || true; timeout 20 bluetoothctl connect '{mac}' || true; bluetoothctl info '{mac}'",
            "trust" => $"bluetoothctl trust '{mac}' || true; bluetoothctl info '{mac}'",
            "pair-only" => $"timeout 20 bluetoothctl pair '{mac}' || true; bluetoothctl info '{mac}'",
            "connect" => $"timeout 20 bluetoothctl connect '{mac}' || true; bluetoothctl info '{mac}'",
            "disconnect" => $"bluetoothctl disconnect '{mac}' || true; bluetoothctl info '{mac}'",
            "remove" => $"bluetoothctl remove '{mac}' || true",
            _ => $"bluetoothctl info '{mac}' || true"
        };

        return await _runner.RunAsync("wsl", new[] { "-d", distro, "--", "bash", "-lc", command }, _paths.Root, 45000).ConfigureAwait(false);
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
            new LinuxBluetoothDevice("E4:17:D8:42:7A:01", "Stadia Controller P1", "yes", "yes", "yes", 84, true),
            new LinuxBluetoothDevice("E4:17:D8:42:7A:02", "Stadia Controller P2", "no", "yes", "yes", 9, true),
            new LinuxBluetoothDevice("A8:6B:AD:12:44:90", "8BitDo SN30 Pro", "no", "no", "no", 56, false)
        };
    }

    private void AddBluetoothDiagnosticsDevices(List<LinuxBluetoothDevice> devices, HashSet<string> seen)
    {
        if (!File.Exists(_paths.BluetoothDiagnostics))
        {
            return;
        }

        var text = ReadFileBestEffort(_paths.BluetoothDiagnostics);
        foreach (Match match in Regex.Matches(text, @"(?ms)^--\s*(?<mac>(?:[0-9A-Fa-f]{2}:){5}[0-9A-Fa-f]{2})\s*--\r?\n(?<info>.*?)(?=^--\s*(?:[0-9A-Fa-f]{2}:){5}[0-9A-Fa-f]{2}\s*--|^==|\z)"))
        {
            var device = ParseLinuxBluetoothInfo(match.Groups["mac"].Value, match.Groups["info"].Value);
            if (device is not null)
            {
                UpsertLinuxDevice(devices, seen, device);
            }
        }
    }

    private void AddBridgeLogDevices(List<LinuxBluetoothDevice> devices, HashSet<string> seen)
    {
        if (!File.Exists(_paths.LinuxLog))
        {
            return;
        }

        var text = ReadFileBestEffort(_paths.LinuxLog);
        foreach (Match match in Regex.Matches(text, @"Controller\s+(?<mac>(?:[0-9A-Fa-f]{2}:){5}[0-9A-Fa-f]{2})\s+(?:already\s+)?connected", RegexOptions.IgnoreCase))
        {
            var mac = match.Groups["mac"].Value.ToUpperInvariant();
            UpsertLinuxDevice(devices, seen, new LinuxBluetoothDevice(
                mac,
                "Stadia Controller (bridge log)",
                "yes",
                "",
                "",
                null,
                true));
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
        return existing with
        {
            Name = BetterValue(existing.Name, candidate.Name, "(unknown)"),
            Connected = BetterValue(existing.Connected, candidate.Connected, ""),
            Paired = BetterValue(existing.Paired, candidate.Paired, ""),
            Trusted = BetterValue(existing.Trusted, candidate.Trusted, ""),
            BatteryPercent = candidate.BatteryPercent ?? existing.BatteryPercent,
            IsStadia = existing.IsStadia || candidate.IsStadia
        };
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
        return await _runner.RunAsync("wsl", new[] { "-d", distro, "--", "bash", "-lc", script }, _paths.Root, 30000).ConfigureAwait(false);
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
        lines.AddRange(linuxBt.Select(d => $"- {d.Mac} {d.Name} connected={d.Connected} paired={d.Paired} battery={(d.BatteryPercent is null ? "unknown" : d.BatteryPercent + "%")}"));
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
            return new ControllerTelemetrySnapshot(DateTimeOffset.Now, Array.Empty<ControllerTelemetryRow>());
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(File.ReadAllText(_paths.ControllerState));
        }
        catch (IOException)
        {
            return new ControllerTelemetrySnapshot(DateTimeOffset.Now, Array.Empty<ControllerTelemetryRow>());
        }
        catch (JsonException)
        {
            return new ControllerTelemetrySnapshot(DateTimeOffset.Now, Array.Empty<ControllerTelemetryRow>());
        }

        using (document)
        {
            if (!document.RootElement.TryGetProperty("controllers", out var controllers) || controllers.ValueKind != JsonValueKind.Array)
            {
                return new ControllerTelemetrySnapshot(DateTimeOffset.Now, Array.Empty<ControllerTelemetryRow>());
            }

            return ReadControllerRows(controllers);
        }
    }

    private static ControllerTelemetrySnapshot ReadControllerRows(JsonElement controllers)
    {
        var rows = new List<ControllerTelemetryRow>();
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
            rows.Add(new ControllerTelemetryRow(
                ReadInt(item, "index") + 1,
                ReadBool(item, "active"),
                ReadDouble(item, "pps"),
                (ulong)Math.Max(0, ReadLong(item, "packets")),
                ReadInt(axes, "trigger_left"),
                ReadInt(axes, "trigger_right"),
                ReadInt(axes, "stick_lx"),
                ReadInt(axes, "stick_ly"),
                ReadInt(axes, "stick_rx"),
                ReadInt(axes, "stick_ry"),
                buttons));
        }

        return new ControllerTelemetrySnapshot(DateTimeOffset.Now, rows);
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
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var workDir = Path.Combine(_paths.SupportBundleDirectory, "StadiaX-support-" + stamp);
        Directory.CreateDirectory(workDir);

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
            if (File.Exists(path))
            {
                File.Copy(path, Path.Combine(workDir, Path.GetFileName(path)), overwrite: true);
            }
        }

        var report = await CreateSessionReportAsync().ConfigureAwait(false);
        File.Copy(report, Path.Combine(workDir, "session-report.md"), overwrite: true);

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
        await File.WriteAllTextAsync(Path.Combine(workDir, "commands.txt"), commandReport.ToString()).ConfigureAwait(false);

        var zipPath = workDir + ".zip";
        if (File.Exists(zipPath))
        {
            File.Delete(zipPath);
        }
        ZipFile.CreateFromDirectory(workDir, zipPath, CompressionLevel.Optimal, includeBaseDirectory: false);
        return zipPath;
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
            new[] { "-d", distro, "--", "bash", "-lc", $"timeout 3s bluetoothctl info '{mac}' 2>/dev/null || true" },
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
