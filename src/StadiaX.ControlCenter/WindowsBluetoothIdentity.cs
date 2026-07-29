using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace StadiaX.ControlCenter;

internal static partial class WindowsBluetoothIdentity
{
    private const int CrSuccess = 0;
    private const uint LocateNormal = 0;
    private const uint LocatePhantom = 1;
    private static readonly Regex AddressPattern = BluetoothAddressRegex();
    private static readonly Regex ServiceAddressPattern = BluetoothServiceAddressRegex();

    public static string ResolveAddress(string? deviceInstancePath)
    {
        if (TryExtractAddress(deviceInstancePath, out var direct))
        {
            return direct;
        }

        if (string.IsNullOrWhiteSpace(deviceInstancePath) ||
            !TryLocateDevice(deviceInstancePath, out var deviceInstance))
        {
            return "";
        }

        for (var depth = 0; depth < 10; depth++)
        {
            var currentId = ReadDeviceId(deviceInstance);
            if (TryExtractAddress(currentId, out var address))
            {
                return address;
            }

            if (CM_Get_Parent(out var parent, deviceInstance, 0) != CrSuccess)
            {
                break;
            }

            deviceInstance = parent;
        }

        return "";
    }

    internal static bool TryExtractAddress(string? value, out string address)
    {
        address = "";
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var match = AddressPattern.Match(value);
        if (!match.Success &&
            value.StartsWith(@"BTHLEDEVICE\", StringComparison.OrdinalIgnoreCase))
        {
            match = ServiceAddressPattern.Match(value);
        }

        if (!match.Success)
        {
            return false;
        }

        var compact = match.Groups[1].Value.ToUpperInvariant();
        address = string.Join(
            ":",
            Enumerable.Range(0, 6).Select(index => compact.Substring(index * 2, 2)));
        return true;
    }

    internal static void RunSelfTest()
    {
        if (!TryExtractAddress(
                @"BTHENUM\DEV_E417D8427A01\7&123456&0&BLUETOOTHDEVICE_E417D8427A01",
                out var classic) ||
            classic != "E4:17:D8:42:7A:01" ||
            !TryExtractAddress(
                @"BTHLEDEVICE\{00001812-0000-1000-8000-00805f9b34fb}_DEV_001122334455",
                out var lowEnergy) ||
            lowEnergy != "00:11:22:33:44:55" ||
            !TryExtractAddress(
                @"BTHLEDEVICE\{0000180F-0000-1000-8000-00805F9B34FB}_DEV_VID&0101F1_PID&0173_REV&0120_F88A5E086C57\A&301C08B8&0&0010",
                out var service) ||
            service != "F8:8A:5E:08:6C:57" ||
            TryExtractAddress(@"HID\VID_18D1&PID_9400\NO_ADDRESS", out _))
        {
            throw new InvalidOperationException("Windows Bluetooth device identity self-test failed.");
        }
    }

    private static bool TryLocateDevice(string deviceInstancePath, out uint deviceInstance)
    {
        return CM_Locate_DevNode(out deviceInstance, deviceInstancePath, LocateNormal) == CrSuccess ||
               CM_Locate_DevNode(out deviceInstance, deviceInstancePath, LocatePhantom) == CrSuccess;
    }

    private static string ReadDeviceId(uint deviceInstance)
    {
        if (CM_Get_Device_ID_Size(out var size, deviceInstance, 0) != CrSuccess)
        {
            return "";
        }

        var buffer = new StringBuilder(checked((int)size + 1));
        return CM_Get_Device_ID(deviceInstance, buffer, (uint)buffer.Capacity, 0) == CrSuccess
            ? buffer.ToString()
            : "";
    }

    [GeneratedRegex(
        @"(?:DEV_|BLUETOOTHDEVICE_)([0-9A-Fa-f]{12})(?:\\|&|$)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BluetoothAddressRegex();

    [GeneratedRegex(
        @"_([0-9A-Fa-f]{12})\\",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BluetoothServiceAddressRegex();

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Locate_DevNode(
        out uint deviceInstance,
        string deviceId,
        uint flags);

    [DllImport("cfgmgr32.dll")]
    private static extern int CM_Get_Parent(
        out uint parentDeviceInstance,
        uint deviceInstance,
        uint flags);

    [DllImport("cfgmgr32.dll")]
    private static extern int CM_Get_Device_ID_Size(
        out uint length,
        uint deviceInstance,
        uint flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Get_Device_ID(
        uint deviceInstance,
        StringBuilder buffer,
        uint bufferLength,
        uint flags);
}
