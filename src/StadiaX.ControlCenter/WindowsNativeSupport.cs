using System.Diagnostics;
using System.Text;
using System.Text.Json;
using HidSharp;

namespace StadiaX.ControlCenter;

internal sealed record WindowsNativeHidDevice(
    int VendorId,
    int ProductId,
    string ProductName,
    string Manufacturer,
    string FriendlyName,
    string FileSystemName,
    int MaxInputReportLength,
    int MaxOutputReportLength,
    string DeviceInstancePath,
    string HidHideSymbolicLink);

internal sealed record WindowsNativeHidScanResult(
    IReadOnlyList<WindowsNativeHidDevice> Devices,
    int RawCandidateCount,
    int DuplicateCandidateCount);

internal sealed record HidHideDeviceEntry(
    string FriendlyName,
    string DeviceInstancePath,
    string SymbolicLink,
    bool Present,
    bool GamingDevice,
    string Vendor,
    string Product,
    string Usage,
    string Description);

internal sealed class HidHideManager
{
    private static readonly string DefaultCliPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        "Nefarius Software Solutions",
        "HidHide",
        "x64",
        "HidHideCLI.exe");

    private readonly AppPaths _paths;
    private readonly ProcessRunner _runner;

    public HidHideManager(AppPaths paths, ProcessRunner runner)
    {
        _paths = paths;
        _runner = runner;
    }

    public string? CliPath => File.Exists(DefaultCliPath) ? DefaultCliPath : null;

    public bool IsInstalled => CliPath is not null;

    public async Task<string> GetVersionAsync()
    {
        var cli = CliPath;
        if (cli is null)
        {
            return "";
        }

        var result = await _runner.RunAsync(cli, new[] { "--version" }, _paths.Root, 10000).ConfigureAwait(false);
        return result.Output.Trim();
    }

    public async Task<IReadOnlyList<HidHideDeviceEntry>> GetDevicesAsync()
    {
        var cli = CliPath;
        if (cli is null)
        {
            return Array.Empty<HidHideDeviceEntry>();
        }

        var result = await _runner.RunAsync(cli, new[] { "--dev-all", "--cancel" }, _paths.Root, 20000).ConfigureAwait(false);
        return ParseDeviceList(result.Output);
    }

    public async Task<CommandResult> ConfigureStadiaDevicesAsync(string appPath, IReadOnlyList<string> deviceInstancePaths, bool elevated)
    {
        var cli = CliPath;
        if (cli is null)
        {
            return new CommandResult(-1, "", "HidHideCLI.exe was not found.");
        }

        var args = new List<string> { "--app-reg", appPath };
        foreach (var devicePath in deviceInstancePaths.Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            args.Add("--dev-hide");
            args.Add(devicePath);
        }
        args.Add("--cloak-on");

        return elevated
            ? await RunElevatedAsync(cli, args).ConfigureAwait(false)
            : await _runner.RunAsync(cli, args, _paths.Root, 30000).ConfigureAwait(false);
    }

    public async Task<CommandResult> DisableCloakAsync(bool elevated)
    {
        var cli = CliPath;
        if (cli is null)
        {
            return new CommandResult(-1, "", "HidHideCLI.exe was not found.");
        }

        var args = new[] { "--cloak-off" };
        return elevated
            ? await RunElevatedAsync(cli, args).ConfigureAwait(false)
            : await _runner.RunAsync(cli, args, _paths.Root, 30000).ConfigureAwait(false);
    }

    private async Task<CommandResult> RunElevatedAsync(string cli, IEnumerable<string> args)
    {
        var commandLine = string.Join(" ", args.Select(WindowsCommandLineQuote));
        var command = $"Start-Process -FilePath {PowerShellSingleQuote(cli)} -ArgumentList {PowerShellSingleQuote(commandLine)} -Verb RunAs -Wait";
        return await _runner.RunAsync(
            "powershell.exe",
            new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", command },
            _paths.Root,
            120000,
            createNoWindow: false).ConfigureAwait(false);
    }

    private static IReadOnlyList<HidHideDeviceEntry> ParseDeviceList(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Array.Empty<HidHideDeviceEntry>();
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var entries = new List<HidHideDeviceEntry>();
            foreach (var group in document.RootElement.EnumerateArray())
            {
                var friendlyName = JsonString(group, "friendlyName");
                if (!group.TryGetProperty("devices", out var devices) || devices.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var item in devices.EnumerateArray())
                {
                    entries.Add(new HidHideDeviceEntry(
                        friendlyName,
                        JsonString(item, "deviceInstancePath"),
                        JsonString(item, "symbolicLink"),
                        JsonBool(item, "present"),
                        JsonBool(item, "gamingDevice"),
                        JsonString(item, "vendor"),
                        JsonString(item, "product"),
                        JsonString(item, "usage"),
                        JsonString(item, "description")));
                }
            }

            return entries;
        }
        catch
        {
            return Array.Empty<HidHideDeviceEntry>();
        }
    }

    private static string JsonString(JsonElement element, string property)
    {
        return element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";
    }

    private static bool JsonBool(JsonElement element, string property)
    {
        return element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.True;
    }

    private static string PowerShellSingleQuote(string value)
    {
        return "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
    }

    private static string WindowsCommandLineQuote(string value)
    {
        if (value.Length == 0)
        {
            return "\"\"";
        }

        var builder = new StringBuilder(value.Length + 2);
        builder.Append('"');
        var pendingBackslashes = 0;
        foreach (var character in value)
        {
            if (character == '\\')
            {
                pendingBackslashes++;
                continue;
            }

            if (character == '"')
            {
                builder.Append('\\', pendingBackslashes * 2 + 1);
                builder.Append('"');
                pendingBackslashes = 0;
                continue;
            }

            builder.Append('\\', pendingBackslashes);
            builder.Append(character);
            pendingBackslashes = 0;
        }

        builder.Append('\\', pendingBackslashes * 2);
        builder.Append('"');
        return builder.ToString();
    }
}

internal sealed class WindowsNativeHidScanner
{
    public const int StadiaVendorId = 0x18D1;
    public const int StadiaProductId = 0x9400;

    private readonly HidHideManager _hidHide;

    public WindowsNativeHidScanner(HidHideManager hidHide)
    {
        _hidHide = hidHide;
    }

    public async Task<IReadOnlyList<WindowsNativeHidDevice>> FindStadiaControllersAsync()
    {
        return (await ScanStadiaControllersAsync().ConfigureAwait(false)).Devices;
    }

    public async Task<WindowsNativeHidScanResult> ScanStadiaControllersAsync()
    {
        var hiddenDevices = await _hidHide.GetDevicesAsync().ConfigureAwait(false);
        var hidHideBySymbolicLink = hiddenDevices
            .Where(device => !string.IsNullOrWhiteSpace(device.SymbolicLink))
            .GroupBy(device => NormalizeDevicePath(device.SymbolicLink))
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var rawCandidates = DeviceList.Local.GetHidDevices()
            .Where(IsLikelyStadiaDevice)
            .Select(device => ToNativeDevice(device, hidHideBySymbolicLink))
            .ToArray();
        var devices = rawCandidates
            .GroupBy(DeviceIdentityKey, StringComparer.OrdinalIgnoreCase)
            .Select(ChooseBestCandidate)
            .OrderBy(device => device.FriendlyName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(device => device.FileSystemName, StringComparer.OrdinalIgnoreCase)
            .Take(4)
            .ToArray();
        return new WindowsNativeHidScanResult(
            devices,
            rawCandidates.Length,
            Math.Max(0, rawCandidates.Length - devices.Length));
    }

    public async Task<string> CreateProbeReportAsync(TimeSpan captureTime)
    {
        var scan = await ScanStadiaControllersAsync().ConfigureAwait(false);
        var devices = scan.Devices;
        var lines = new List<string>
        {
            "Stadia X Windows Native probe",
            $"Created: {DateTimeOffset.Now:o}",
            $"HidHide installed: {_hidHide.IsInstalled}",
            $"HidHide version: {await _hidHide.GetVersionAsync().ConfigureAwait(false)}",
            $"Detected Stadia HID devices: {devices.Count}",
            $"Raw HID candidates: {scan.RawCandidateCount}",
            $"Duplicate HID candidates ignored: {scan.DuplicateCandidateCount}",
            ""
        };

        if (devices.Count == 0)
        {
            lines.Add("No Stadia HID devices are visible to Stadia X right now.");
            lines.Add("Pair the controller with Windows, keep it on, then run the probe again.");
            return string.Join(Environment.NewLine, lines);
        }

        for (var i = 0; i < devices.Count; i++)
        {
            var device = devices[i];
            lines.Add($"== P{i + 1} candidate ==");
            lines.Add($"Name: {device.FriendlyName}");
            lines.Add($"Product: {device.ProductName}");
            lines.Add($"VID/PID: {device.VendorId:X4}:{device.ProductId:X4}");
            lines.Add($"HID path: {device.FileSystemName}");
            lines.Add($"HidHide instance: {EmptyAsNone(device.DeviceInstancePath)}");
            lines.Add($"Input report length: {device.MaxInputReportLength}");
            lines.Add($"Output report length: {device.MaxOutputReportLength}");
            lines.AddRange(DescribeReports(device.FileSystemName));
            lines.AddRange(CaptureReports(device.FileSystemName, captureTime));
            lines.Add("");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static bool IsLikelyStadiaDevice(HidDevice device)
    {
        if (device.VendorID == StadiaVendorId && device.ProductID == StadiaProductId)
        {
            return true;
        }

        var text = string.Join(" ", Safe(() => device.GetFriendlyName()), Safe(() => device.GetProductName()), device.GetFileSystemName());
        return text.Contains("stadia", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("18d1", StringComparison.OrdinalIgnoreCase) && text.Contains("9400", StringComparison.OrdinalIgnoreCase);
    }

    private static WindowsNativeHidDevice ToNativeDevice(HidDevice device, IReadOnlyDictionary<string, HidHideDeviceEntry> hidHideBySymbolicLink)
    {
        var fileSystemName = device.GetFileSystemName();
        hidHideBySymbolicLink.TryGetValue(NormalizeDevicePath(fileSystemName), out var hidHideEntry);
        return new WindowsNativeHidDevice(
            device.VendorID,
            device.ProductID,
            Safe(() => device.GetProductName()),
            Safe(() => device.GetManufacturer()),
            Safe(() => device.GetFriendlyName()),
            fileSystemName,
            SafeInt(device.GetMaxInputReportLength),
            SafeInt(device.GetMaxOutputReportLength),
            hidHideEntry?.DeviceInstancePath ?? "",
            hidHideEntry?.SymbolicLink ?? "");
    }

    private static string DeviceIdentityKey(WindowsNativeHidDevice device)
    {
        return !string.IsNullOrWhiteSpace(device.DeviceInstancePath)
            ? NormalizeDevicePath(device.DeviceInstancePath)
            : NormalizeDevicePath(device.FileSystemName);
    }

    private static WindowsNativeHidDevice ChooseBestCandidate(IGrouping<string, WindowsNativeHidDevice> candidates)
    {
        return candidates
            .OrderByDescending(device => device.MaxInputReportLength > 0)
            .ThenByDescending(device => device.MaxOutputReportLength > 0)
            .ThenByDescending(device => device.MaxInputReportLength)
            .ThenByDescending(device => device.MaxOutputReportLength)
            .ThenBy(device => device.FriendlyName, StringComparer.OrdinalIgnoreCase)
            .First();
    }

    private static IEnumerable<string> CaptureReports(string fileSystemName, TimeSpan captureTime)
    {
        var lines = new List<string>();
        var device = DeviceList.Local.GetHidDevices().FirstOrDefault(item =>
            item.GetFileSystemName().Equals(fileSystemName, StringComparison.OrdinalIgnoreCase));
        if (device is null)
        {
            lines.Add("Capture: device disappeared before open.");
            return lines;
        }

        HidStream? stream = null;
        try
        {
            stream = device.Open();
            stream.ReadTimeout = 350;
            var buffer = new byte[Math.Max(1, device.GetMaxInputReportLength())];
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var sw = Stopwatch.StartNew();
            var count = 0;

            lines.Add($"Capture: reading raw HID input for {captureTime.TotalSeconds:0.#}s");
            while (sw.Elapsed < captureTime && count < 24)
            {
                int read;
                try
                {
                    Array.Clear(buffer);
                    read = stream.Read(buffer);
                }
                catch (TimeoutException)
                {
                    continue;
                }

                if (read <= 0)
                {
                    continue;
                }

                var hex = Convert.ToHexString(buffer.AsSpan(0, read));
                if (!seen.Add(hex))
                {
                    continue;
                }

                lines.Add($"Report {++count}: {hex}");
            }

            if (count == 0)
            {
                lines.Add("Capture: no input reports received. Press buttons while probing.");
            }
        }
        catch (Exception ex)
        {
            lines.Add("Capture failed: " + ex.Message);
        }
        finally
        {
            stream?.Dispose();
        }

        return lines;
    }

    private static IEnumerable<string> DescribeReports(string fileSystemName)
    {
        var lines = new List<string>();
        try
        {
            var device = DeviceList.Local.GetHidDevices().FirstOrDefault(item =>
                item.GetFileSystemName().Equals(fileSystemName, StringComparison.OrdinalIgnoreCase));
            if (device is null)
            {
                return lines;
            }

            var descriptor = device.GetReportDescriptor();
            lines.Add("Input report IDs: " + ReportSummary(descriptor.InputReports));
            lines.Add("Output report IDs: " + ReportSummary(descriptor.OutputReports));
            lines.Add("Feature report IDs: " + ReportSummary(descriptor.FeatureReports));
        }
        catch (Exception ex)
        {
            lines.Add("Report descriptor read failed: " + ex.Message);
        }

        return lines;
    }

    private static string ReportSummary(IEnumerable<HidSharp.Reports.Report> reports)
    {
        var summary = reports
            .Select(report => report.ReportID == 0 ? $"none/len={report.Length}" : $"{report.ReportID}/len={report.Length}")
            .ToArray();
        return summary.Length == 0 ? "none" : string.Join(", ", summary);
    }

    private static string NormalizeDevicePath(string value)
    {
        return value.Trim()
            .TrimEnd('#')
            .ToUpperInvariant();
    }

    private static string Safe(Func<string> read)
    {
        try
        {
            var value = read();
            return string.IsNullOrWhiteSpace(value) ? "" : value.Trim();
        }
        catch
        {
            return "";
        }
    }

    private static int SafeInt(Func<int> read)
    {
        try
        {
            return read();
        }
        catch
        {
            return 0;
        }
    }

    private static string EmptyAsNone(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "none" : value;
    }
}
