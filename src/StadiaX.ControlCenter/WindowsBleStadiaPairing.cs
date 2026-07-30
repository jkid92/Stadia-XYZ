using System.Collections.Concurrent;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;

namespace StadiaX.ControlCenter;

internal static class WindowsBleStadiaPairing
{
    private static readonly TimeSpan DiscoveryWindow = TimeSpan.FromSeconds(8);
    private static readonly string[] RequestedProperties =
    [
        "System.Devices.Aep.DeviceAddress",
        "System.Devices.Aep.IsConnected"
    ];

    internal static async Task<StadiaBluetoothPairingResult> DiscoverAndPairAsync(
        int maxControllers,
        Action<StadiaBluetoothPairingProgress>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Invoke(new StadiaBluetoothPairingProgress(
            "Radio",
            8,
            "Checking the Windows Bluetooth LE adapter"));

        var adapter = await BluetoothAdapter.GetDefaultAsync().AsTask(cancellationToken).ConfigureAwait(false);
        if (adapter is null || !adapter.IsLowEnergySupported)
        {
            return new StadiaBluetoothPairingResult(
                false,
                0,
                Array.Empty<StadiaBluetoothDevice>(),
                Array.Empty<StadiaBluetoothPairingAttempt>());
        }

        progress?.Invoke(new StadiaBluetoothPairingProgress(
            "Discovery",
            18,
            "Scanning Windows Bluetooth LE advertisements for Stadia controllers"));

        var found = new ConcurrentDictionary<string, DeviceInformation>(StringComparer.OrdinalIgnoreCase);
        var pairedWatcher = CreateWatcher(isPaired: true, found);
        var unpairedWatcher = CreateWatcher(isPaired: false, found);
        pairedWatcher.Start();
        unpairedWatcher.Start();

        try
        {
            await Task.Delay(DiscoveryWindow, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            StopWatcher(pairedWatcher);
            StopWatcher(unpairedWatcher);
        }

        var candidates = found.Values
            .Where(device => WindowsStadiaBluetoothPairingService.IsStadiaName(device.Name))
            .OrderByDescending(device => device.Pairing.IsPaired)
            .ThenByDescending(IsConnected)
            .ThenBy(device => device.Name, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Clamp(maxControllers, 1, 4))
            .ToArray();
        if (candidates.Length == 0)
        {
            return new StadiaBluetoothPairingResult(
                true,
                0,
                Array.Empty<StadiaBluetoothDevice>(),
                Array.Empty<StadiaBluetoothPairingAttempt>());
        }

        progress?.Invoke(new StadiaBluetoothPairingProgress(
            "Found",
            48,
            $"Found {candidates.Length} Stadia Bluetooth LE device(s)"));

        var attempts = new List<StadiaBluetoothPairingAttempt>(candidates.Length);
        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var device = ToStadiaDevice(candidate);
            if (candidate.Pairing.IsPaired)
            {
                attempts.Add(new StadiaBluetoothPairingAttempt(
                    device,
                    StadiaBluetoothPairingOutcome.AlreadyPaired,
                    0,
                    device.Connected
                        ? "Already paired and connected through Windows Bluetooth LE"
                        : "Already paired; waiting for the Stadia HID interface"));
                continue;
            }

            if (!candidate.Pairing.CanPair)
            {
                attempts.Add(new StadiaBluetoothPairingAttempt(
                    device,
                    StadiaBluetoothPairingOutcome.Failed,
                    (int)DevicePairingResultStatus.NotReadyToPair,
                    "Windows discovered the controller but does not currently allow pairing"));
                continue;
            }

            var percent = 52 + (int)Math.Round((attempts.Count + 1d) / candidates.Length * 34d);
            progress?.Invoke(new StadiaBluetoothPairingProgress(
                "Pairing",
                percent,
                $"Pairing {candidate.Name} through Windows Bluetooth LE"));

            var result = await candidate.Pairing
                .PairAsync(DevicePairingProtectionLevel.None)
                .AsTask(cancellationToken)
                .ConfigureAwait(false);
            var outcome = MapOutcome(result.Status);
            attempts.Add(new StadiaBluetoothPairingAttempt(
                device with
                {
                    Connected = IsConnected(candidate),
                    Remembered = outcome is StadiaBluetoothPairingOutcome.Paired or StadiaBluetoothPairingOutcome.AlreadyPaired,
                    Authenticated = outcome is StadiaBluetoothPairingOutcome.Paired or StadiaBluetoothPairingOutcome.AlreadyPaired
                },
                outcome,
                (int)result.Status,
                PairingDetail(result.Status)));
        }

        progress?.Invoke(new StadiaBluetoothPairingProgress(
            "Complete",
            92,
            attempts.Any(attempt => attempt.Outcome == StadiaBluetoothPairingOutcome.Paired)
                ? "Bluetooth LE pairing completed; waiting for the Stadia HID interface"
                : "Bluetooth LE scan completed; waiting for controller input"));

        return new StadiaBluetoothPairingResult(
            true,
            0,
            attempts.Select(attempt => attempt.Device).ToArray(),
            attempts);
    }

    internal static void RunSelfTest()
    {
        if (MapOutcome(DevicePairingResultStatus.Paired) != StadiaBluetoothPairingOutcome.Paired ||
            MapOutcome(DevicePairingResultStatus.AlreadyPaired) != StadiaBluetoothPairingOutcome.AlreadyPaired ||
            MapOutcome(DevicePairingResultStatus.PairingCanceled) != StadiaBluetoothPairingOutcome.Cancelled ||
            MapOutcome(DevicePairingResultStatus.Failed) != StadiaBluetoothPairingOutcome.Failed)
        {
            throw new InvalidOperationException("Windows Bluetooth LE pairing status self-test failed.");
        }
    }

    private static DeviceWatcher CreateWatcher(
        bool isPaired,
        ConcurrentDictionary<string, DeviceInformation> found)
    {
        var selector = BluetoothLEDevice.GetDeviceSelectorFromPairingState(isPaired);
        var watcher = DeviceInformation.CreateWatcher(
            selector,
            RequestedProperties,
            DeviceInformationKind.AssociationEndpoint);
        watcher.Added += (_, device) => found[device.Id] = device;
        watcher.Updated += (_, update) =>
        {
            if (found.TryGetValue(update.Id, out var device))
            {
                device.Update(update);
            }
        };
        watcher.Removed += (sender, update) => found.TryRemove(update.Id, out _);
        return watcher;
    }

    private static void StopWatcher(DeviceWatcher watcher)
    {
        if (watcher.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted)
        {
            watcher.Stop();
        }
    }

    private static StadiaBluetoothDevice ToStadiaDevice(DeviceInformation device)
    {
        return new StadiaBluetoothDevice(
            string.IsNullOrWhiteSpace(device.Name) ? "Stadia Controller" : device.Name.Trim(),
            PropertyString(device, "System.Devices.Aep.DeviceAddress"),
            IsConnected(device),
            device.Pairing.IsPaired,
            device.Pairing.IsPaired);
    }

    private static bool IsConnected(DeviceInformation device)
    {
        return device.Properties.TryGetValue("System.Devices.Aep.IsConnected", out var value) &&
               value is bool connected &&
               connected;
    }

    private static string PropertyString(DeviceInformation device, string property)
    {
        return device.Properties.TryGetValue(property, out var value)
            ? Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? ""
            : "";
    }

    private static StadiaBluetoothPairingOutcome MapOutcome(DevicePairingResultStatus status)
    {
        return status switch
        {
            DevicePairingResultStatus.Paired => StadiaBluetoothPairingOutcome.Paired,
            DevicePairingResultStatus.AlreadyPaired => StadiaBluetoothPairingOutcome.AlreadyPaired,
            DevicePairingResultStatus.PairingCanceled => StadiaBluetoothPairingOutcome.Cancelled,
            _ => StadiaBluetoothPairingOutcome.Failed
        };
    }

    private static string PairingDetail(DevicePairingResultStatus status)
    {
        return status switch
        {
            DevicePairingResultStatus.Paired => "Bluetooth LE pairing completed",
            DevicePairingResultStatus.AlreadyPaired => "Windows reports that the controller is already paired",
            DevicePairingResultStatus.PairingCanceled => "Bluetooth pairing was cancelled",
            DevicePairingResultStatus.NotReadyToPair => "The controller left pairing mode before Windows completed",
            DevicePairingResultStatus.AuthenticationFailure => "Bluetooth authentication failed",
            DevicePairingResultStatus.ConnectionRejected => "The controller rejected the Bluetooth connection",
            DevicePairingResultStatus.TooManyConnections => "The controller or Bluetooth adapter has too many active connections",
            _ => $"Windows Bluetooth pairing failed: {status}"
        };
    }
}
