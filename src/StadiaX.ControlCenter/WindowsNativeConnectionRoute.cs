namespace StadiaX.ControlCenter;

internal static class WindowsNativeConnectionRoute
{
    internal const string PhysicalInputMethod = "HidSharp HID input reports";
    internal const string VirtualOutputMethod = "VIIPER -> usbip-win2 -> Xbox 360";

    internal static string TransportName(WindowsNativeHidDevice? device)
    {
        if (device is null)
        {
            return "Bluetooth LE automatic search";
        }

        var identity = string.Join(
            " ",
            device.DeviceInstancePath,
            device.HidHideSymbolicLink,
            device.FileSystemName);
        if (!string.IsNullOrWhiteSpace(device.BluetoothAddress) ||
            identity.Contains("BTHLE", StringComparison.OrdinalIgnoreCase) ||
            identity.Contains("BTHENUM", StringComparison.OrdinalIgnoreCase) ||
            identity.Contains("BLUETOOTH", StringComparison.OrdinalIgnoreCase))
        {
            return "Bluetooth LE through Windows";
        }

        if (identity.Contains("USB", StringComparison.OrdinalIgnoreCase))
        {
            return "USB HID";
        }

        return "Windows HID (transport not exposed)";
    }

    internal static string RumbleMethod(
        WindowsNativeHidOutputMode requestedMode,
        string? lastUsedMode = null)
    {
        var requested = WindowsNativeHidOutputModeStore.TechnicalName(requestedMode);
        return string.IsNullOrWhiteSpace(lastUsedMode)
            ? requestedMode == WindowsNativeHidOutputMode.Auto
                ? "Auto fallback (awaiting first command)"
                : requested
            : requestedMode == WindowsNativeHidOutputMode.Auto
                ? $"{lastUsedMode} (selected by Auto)"
                : lastUsedMode;
    }

    internal static string? ExtractLastUsedRumbleMethod(string logText)
    {
        foreach (var line in logText
                     .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                     .Reverse())
        {
            if (line.Contains("WINDOWS_NATIVE_HID_OUTPUT_MODE", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (!line.Contains("WINDOWS_NATIVE_RUMBLE_OK", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            const string marker = "used=";
            var start = line.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (start < 0)
            {
                return null;
            }

            start += marker.Length;
            var end = line.IndexOfAny([' ', '\t', ';', ','], start);
            var method = end < 0 ? line[start..] : line[start..end];
            return string.IsNullOrWhiteSpace(method) ? null : method.Trim();
        }

        return null;
    }

    internal static void RunSelfTest()
    {
        var bluetooth = new WindowsNativeHidDevice(
            0x18D1,
            0x9400,
            "Stadia Controller",
            "Google",
            "Stadia Controller",
            @"\\?\hid#vid_18d1&pid_9400",
            10,
            5,
            @"BTHLEDEVICE\DEV_001122334455",
            "",
            BluetoothAddress: "00:11:22:33:44:55");
        var log = "WINDOWS_NATIVE_RUMBLE_OK P1 requested=Auto used=Win32WriteFile queueMs=1";
        if (TransportName(bluetooth) != "Bluetooth LE through Windows" ||
            ExtractLastUsedRumbleMethod(log) != "Win32WriteFile" ||
            ExtractLastUsedRumbleMethod(log + Environment.NewLine + "WINDOWS_NATIVE_HID_OUTPUT_MODE requested=Auto") is not null ||
            RumbleMethod(WindowsNativeHidOutputMode.Auto, "Win32WriteFile") !=
            "Win32WriteFile (selected by Auto)")
        {
            throw new InvalidOperationException("Windows Native connection route self-test failed.");
        }
    }
}
