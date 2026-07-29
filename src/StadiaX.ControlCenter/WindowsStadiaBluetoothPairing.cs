using System.ComponentModel;
using System.Runtime.InteropServices;

namespace StadiaX.ControlCenter;

internal enum StadiaBluetoothPairingOutcome
{
    AlreadyPaired,
    Paired,
    Cancelled,
    Failed
}

internal sealed record StadiaBluetoothDevice(
    string Name,
    string Address,
    bool Connected,
    bool Remembered,
    bool Authenticated);

internal sealed record StadiaBluetoothPairingAttempt(
    StadiaBluetoothDevice Device,
    StadiaBluetoothPairingOutcome Outcome,
    int ErrorCode,
    string Detail);

internal sealed record StadiaBluetoothPairingProgress(
    string Stage,
    int Percent,
    string Detail);

internal sealed record StadiaBluetoothPairingResult(
    bool BluetoothAvailable,
    int DiscoveryError,
    IReadOnlyList<StadiaBluetoothDevice> Devices,
    IReadOnlyList<StadiaBluetoothPairingAttempt> Attempts)
{
    public bool HasUsableDevice => Attempts.Any(attempt =>
        attempt.Outcome is StadiaBluetoothPairingOutcome.AlreadyPaired or StadiaBluetoothPairingOutcome.Paired);

    public int PairedNow => Attempts.Count(attempt => attempt.Outcome == StadiaBluetoothPairingOutcome.Paired);
}

internal sealed class WindowsStadiaBluetoothPairingService
{
    private const int ErrorSuccess = 0;
    private const int ErrorCancelled = 1223;
    private const int ErrorNoMoreItems = 259;
    private const byte InquiryTimeoutMultiplier = 8;
    private const int BluetoothMaxNameSize = 248;

    public Task<StadiaBluetoothPairingResult> DiscoverAndPairAsync(
        int maxControllers = 4,
        Action<StadiaBluetoothPairingProgress>? progress = null,
        IntPtr parentWindow = default,
        CancellationToken cancellationToken = default)
    {
        maxControllers = Math.Clamp(maxControllers, 1, 4);
        if (IsDemoMode())
        {
            return RunDemoAsync(maxControllers, progress, cancellationToken);
        }

        return Task.Run(
            () => DiscoverAndPair(maxControllers, progress, parentWindow, cancellationToken),
            cancellationToken);
    }

    internal static bool IsStadiaName(string? name)
    {
        return !string.IsNullOrWhiteSpace(name) &&
               name.Trim().StartsWith("Stadia", StringComparison.OrdinalIgnoreCase);
    }

    internal static string FormatAddress(ulong address)
    {
        var octets = new string[6];
        for (var index = 0; index < octets.Length; index++)
        {
            var shift = (octets.Length - 1 - index) * 8;
            octets[index] = ((address >> shift) & 0xFF).ToString("X2");
        }

        return string.Join(":", octets);
    }

    internal static void RunSelfTest()
    {
        if (!IsStadiaName("Stadia Controller") ||
            !IsStadiaName("  stadia-test  ") ||
            IsStadiaName("Google Stadia") ||
            IsStadiaName("Xbox Wireless Controller") ||
            FormatAddress(0x001122334455) != "00:11:22:33:44:55" ||
            Marshal.SizeOf<BluetoothFindRadioParams>() != sizeof(int) ||
            Marshal.SizeOf<BluetoothDeviceSearchParams>() != (IntPtr.Size == 8 ? 40 : 32) ||
            Marshal.SizeOf<BluetoothDeviceInfo>() != 560)
        {
            throw new InvalidOperationException("Windows Stadia Bluetooth pairing filter self-test failed.");
        }

        var demo = RunDemoAsync(4, null, CancellationToken.None).GetAwaiter().GetResult();
        if (!demo.BluetoothAvailable ||
            demo.Devices.Count != 2 ||
            demo.PairedNow != 1 ||
            !demo.HasUsableDevice)
        {
            throw new InvalidOperationException("Windows Stadia Bluetooth pairing simulation self-test failed.");
        }
    }

    private static StadiaBluetoothPairingResult DiscoverAndPair(
        int maxControllers,
        Action<StadiaBluetoothPairingProgress>? progress,
        IntPtr parentWindow,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        progress?.Invoke(new StadiaBluetoothPairingProgress(
            "Radio",
            8,
            "Checking the Windows Bluetooth radio"));

        if (!TryFindBluetoothRadio(out var radioError))
        {
            return new StadiaBluetoothPairingResult(
                false,
                radioError,
                Array.Empty<StadiaBluetoothDevice>(),
                Array.Empty<StadiaBluetoothPairingAttempt>());
        }

        progress?.Invoke(new StadiaBluetoothPairingProgress(
            "Discovery",
            18,
            "Searching for Bluetooth devices named Stadia"));

        var candidates = FindStadiaDevices(out var discoveryError)
            .OrderByDescending(candidate => candidate.Device.Authenticated)
            .ThenByDescending(candidate => candidate.Device.Connected)
            .ThenBy(candidate => candidate.Device.Name, StringComparer.OrdinalIgnoreCase)
            .Take(maxControllers)
            .ToArray();

        cancellationToken.ThrowIfCancellationRequested();
        if (candidates.Length == 0)
        {
            return new StadiaBluetoothPairingResult(
                true,
                discoveryError,
                Array.Empty<StadiaBluetoothDevice>(),
                Array.Empty<StadiaBluetoothPairingAttempt>());
        }

        progress?.Invoke(new StadiaBluetoothPairingProgress(
            "Found",
            48,
            $"Found {candidates.Length} Stadia Bluetooth device(s)"));

        var attempts = new List<StadiaBluetoothPairingAttempt>(candidates.Length);
        for (var index = 0; index < candidates.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidate = candidates[index];
            if (candidate.Device.Authenticated)
            {
                attempts.Add(new StadiaBluetoothPairingAttempt(
                    candidate.Device,
                    StadiaBluetoothPairingOutcome.AlreadyPaired,
                    ErrorSuccess,
                    candidate.Device.Connected
                        ? "Already paired and connected"
                        : "Already paired; waiting for the HID connection"));
                continue;
            }

            var percent = 52 + (int)Math.Round((index + 1d) / candidates.Length * 34d);
            progress?.Invoke(new StadiaBluetoothPairingProgress(
                "Pairing",
                percent,
                $"Pairing {candidate.Device.Name}; confirm the Windows prompt if it appears"));

            var nativeInfo = candidate.NativeInfo;
            var error = BluetoothAuthenticateDeviceEx(
                parentWindow,
                IntPtr.Zero,
                ref nativeInfo,
                IntPtr.Zero,
                BluetoothAuthenticationRequirements.ProtectionNotRequiredBonding);

            var outcome = error switch
            {
                ErrorSuccess => StadiaBluetoothPairingOutcome.Paired,
                ErrorNoMoreItems => StadiaBluetoothPairingOutcome.AlreadyPaired,
                ErrorCancelled => StadiaBluetoothPairingOutcome.Cancelled,
                _ => StadiaBluetoothPairingOutcome.Failed
            };
            var detail = outcome switch
            {
                StadiaBluetoothPairingOutcome.Paired => "Bluetooth pairing completed",
                StadiaBluetoothPairingOutcome.AlreadyPaired => "Windows reports that the controller is already paired",
                StadiaBluetoothPairingOutcome.Cancelled => "Bluetooth pairing was cancelled",
                _ => Win32ErrorMessage(error)
            };
            attempts.Add(new StadiaBluetoothPairingAttempt(candidate.Device, outcome, error, detail));
        }

        progress?.Invoke(new StadiaBluetoothPairingProgress(
            "Complete",
            92,
            attempts.Any(attempt => attempt.Outcome == StadiaBluetoothPairingOutcome.Paired)
                ? "Bluetooth pairing completed; waiting for the Stadia HID device"
                : "Bluetooth scan completed; waiting for the Stadia HID device"));

        return new StadiaBluetoothPairingResult(
            true,
            discoveryError,
            candidates.Select(candidate => candidate.Device).ToArray(),
            attempts);
    }

    private static IReadOnlyList<NativeCandidate> FindStadiaDevices(out int error)
    {
        var search = new BluetoothDeviceSearchParams
        {
            Size = Marshal.SizeOf<BluetoothDeviceSearchParams>(),
            ReturnAuthenticated = true,
            ReturnRemembered = true,
            ReturnUnknown = true,
            ReturnConnected = true,
            IssueInquiry = true,
            TimeoutMultiplier = InquiryTimeoutMultiplier,
            Radio = IntPtr.Zero
        };
        var info = CreateDeviceInfo();
        var findHandle = BluetoothFindFirstDevice(ref search, ref info);
        if (findHandle == IntPtr.Zero)
        {
            error = Marshal.GetLastWin32Error();
            return Array.Empty<NativeCandidate>();
        }

        var devices = new Dictionary<ulong, NativeCandidate>();
        try
        {
            do
            {
                if (IsStadiaName(info.Name))
                {
                    var device = new StadiaBluetoothDevice(
                        info.Name.Trim(),
                        FormatAddress(info.Address),
                        info.Connected,
                        info.Remembered,
                        info.Authenticated);
                    devices[info.Address] = new NativeCandidate(device, info);
                }

                info = CreateDeviceInfo();
            }
            while (BluetoothFindNextDevice(findHandle, ref info));

            error = Marshal.GetLastWin32Error();
            if (error == ErrorNoMoreItems)
            {
                error = ErrorSuccess;
            }
        }
        finally
        {
            _ = BluetoothFindDeviceClose(findHandle);
        }

        return devices.Values.ToArray();
    }

    private static bool TryFindBluetoothRadio(out int error)
    {
        var parameters = new BluetoothFindRadioParams
        {
            Size = Marshal.SizeOf<BluetoothFindRadioParams>()
        };
        var findHandle = BluetoothFindFirstRadio(ref parameters, out var radioHandle);
        if (findHandle == IntPtr.Zero)
        {
            error = Marshal.GetLastWin32Error();
            return false;
        }

        try
        {
            error = ErrorSuccess;
            return radioHandle != IntPtr.Zero;
        }
        finally
        {
            if (radioHandle != IntPtr.Zero)
            {
                _ = CloseHandle(radioHandle);
            }
            _ = BluetoothFindRadioClose(findHandle);
        }
    }

    private static async Task<StadiaBluetoothPairingResult> RunDemoAsync(
        int maxControllers,
        Action<StadiaBluetoothPairingProgress>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Invoke(new StadiaBluetoothPairingProgress("Discovery", 18, "Searching for Bluetooth devices named Stadia"));
        await Task.Delay(350, cancellationToken).ConfigureAwait(false);
        var devices = new[]
        {
            new StadiaBluetoothDevice("Stadia Controller P1", "E4:17:D8:42:7A:01", true, true, true),
            new StadiaBluetoothDevice("Stadia Controller P2", "E4:17:D8:42:7A:02", false, false, false)
        }.Take(maxControllers).ToArray();
        progress?.Invoke(new StadiaBluetoothPairingProgress("Found", 48, $"Found {devices.Length} Stadia Bluetooth device(s)"));
        await Task.Delay(250, cancellationToken).ConfigureAwait(false);
        var attempts = devices.Select((device, index) => new StadiaBluetoothPairingAttempt(
            device,
            index == 0 ? StadiaBluetoothPairingOutcome.AlreadyPaired : StadiaBluetoothPairingOutcome.Paired,
            ErrorSuccess,
            index == 0 ? "Already paired and connected" : "Bluetooth pairing completed")).ToArray();
        progress?.Invoke(new StadiaBluetoothPairingProgress("Complete", 92, "Bluetooth pairing completed; waiting for the Stadia HID device"));
        return new StadiaBluetoothPairingResult(true, ErrorSuccess, devices, attempts);
    }

    private static BluetoothDeviceInfo CreateDeviceInfo()
    {
        return new BluetoothDeviceInfo
        {
            Size = Marshal.SizeOf<BluetoothDeviceInfo>(),
            Name = string.Empty
        };
    }

    private static string Win32ErrorMessage(int error)
    {
        return error == ErrorSuccess
            ? "Success"
            : $"{new Win32Exception(error).Message} (Win32 {error})";
    }

    private static bool IsDemoMode()
    {
        return string.Equals(
            Environment.GetEnvironmentVariable("STADIAX_DEMO_BLUETOOTH"),
            "1",
            StringComparison.OrdinalIgnoreCase);
    }

    private sealed record NativeCandidate(
        StadiaBluetoothDevice Device,
        BluetoothDeviceInfo NativeInfo);

    private enum BluetoothAuthenticationRequirements : uint
    {
        ProtectionNotRequiredBonding = 0x2
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BluetoothFindRadioParams
    {
        public int Size;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BluetoothDeviceSearchParams
    {
        public int Size;

        [MarshalAs(UnmanagedType.Bool)]
        public bool ReturnAuthenticated;

        [MarshalAs(UnmanagedType.Bool)]
        public bool ReturnRemembered;

        [MarshalAs(UnmanagedType.Bool)]
        public bool ReturnUnknown;

        [MarshalAs(UnmanagedType.Bool)]
        public bool ReturnConnected;

        [MarshalAs(UnmanagedType.Bool)]
        public bool IssueInquiry;

        public byte TimeoutMultiplier;
        public IntPtr Radio;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct BluetoothDeviceInfo
    {
        public int Size;
        public ulong Address;
        public uint ClassOfDevice;

        [MarshalAs(UnmanagedType.Bool)]
        public bool Connected;

        [MarshalAs(UnmanagedType.Bool)]
        public bool Remembered;

        [MarshalAs(UnmanagedType.Bool)]
        public bool Authenticated;

        public SystemTime LastSeen;
        public SystemTime LastUsed;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = BluetoothMaxNameSize)]
        public string Name;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemTime
    {
        public ushort Year;
        public ushort Month;
        public ushort DayOfWeek;
        public ushort Day;
        public ushort Hour;
        public ushort Minute;
        public ushort Second;
        public ushort Milliseconds;
    }

    [DllImport("bthprops.cpl", ExactSpelling = true, SetLastError = true)]
    private static extern IntPtr BluetoothFindFirstRadio(
        ref BluetoothFindRadioParams parameters,
        out IntPtr radioHandle);

    [DllImport("bthprops.cpl", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BluetoothFindRadioClose(IntPtr findHandle);

    [DllImport("bthprops.cpl", ExactSpelling = true, SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr BluetoothFindFirstDevice(
        ref BluetoothDeviceSearchParams searchParams,
        ref BluetoothDeviceInfo deviceInfo);

    [DllImport("bthprops.cpl", ExactSpelling = true, SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BluetoothFindNextDevice(
        IntPtr findHandle,
        ref BluetoothDeviceInfo deviceInfo);

    [DllImport("bthprops.cpl", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BluetoothFindDeviceClose(IntPtr findHandle);

    [DllImport("bthprops.cpl", ExactSpelling = true, SetLastError = true)]
    private static extern int BluetoothAuthenticateDeviceEx(
        IntPtr parentWindow,
        IntPtr radioHandle,
        ref BluetoothDeviceInfo deviceInfo,
        IntPtr outOfBandData,
        BluetoothAuthenticationRequirements authenticationRequirement);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}
