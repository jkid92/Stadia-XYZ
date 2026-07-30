using System.Collections.Concurrent;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;

namespace StadiaX.ControlCenter;

internal static class WindowsBleBatteryReader
{
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(3);
    private static readonly ConcurrentDictionary<ulong, CacheEntry> Cache = new();

    internal static async Task<WindowsBatteryReading?> ReadAsync(
        string bluetoothAddress,
        CancellationToken cancellationToken = default)
    {
        if (!TryParseAddress(bluetoothAddress, out var address))
        {
            return null;
        }

        var now = DateTimeOffset.UtcNow;
        if (Cache.TryGetValue(address, out var cached) && cached.ExpiresAt > now)
        {
            return cached.Reading;
        }

        WindowsBatteryReading? reading = null;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(ReadTimeout);
            reading = await ReadUncachedAsync(address, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            AppDiagnosticsLogger.Record(
                "WINDOWS_BLE_BATTERY_TIMEOUT",
                ("address", FormatAddress(address)));
        }
        catch (Exception ex)
        {
            AppDiagnosticsLogger.Record(
                "WINDOWS_BLE_BATTERY_READ_WARN",
                ("address", FormatAddress(address)),
                ("error", ex.Message));
        }

        Cache[address] = new CacheEntry(reading, now.Add(CacheLifetime));
        return reading;
    }

    internal static bool TryParseAddress(string? text, out ulong address)
    {
        address = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var hex = new string(text.Where(Uri.IsHexDigit).ToArray());
        return hex.Length == 12 &&
               ulong.TryParse(
                   hex,
                   System.Globalization.NumberStyles.HexNumber,
                   System.Globalization.CultureInfo.InvariantCulture,
                   out address);
    }

    internal static void RunSelfTest()
    {
        if (!TryParseAddress("00:11:22:33:44:55", out var address) ||
            address != 0x001122334455 ||
            TryParseAddress("not-an-address", out _) ||
            !WindowsNativeRumbleReport.BuildGattPayload(255, 128)
                .SequenceEqual(new byte[] { 0x00, 0xFF, 0x00, 0x80 }))
        {
            throw new InvalidOperationException("Windows BLE protocol self-test failed.");
        }
    }

    private static async Task<WindowsBatteryReading?> ReadUncachedAsync(
        ulong address,
        CancellationToken cancellationToken)
    {
        using var device = await BluetoothLEDevice.FromBluetoothAddressAsync(address)
            .AsTask(cancellationToken)
            .ConfigureAwait(false);
        if (device is null)
        {
            return null;
        }

        var services = await device
            .GetGattServicesForUuidAsync(GattServiceUuids.Battery, BluetoothCacheMode.Uncached)
            .AsTask(cancellationToken)
            .ConfigureAwait(false);
        if (services.Status != GattCommunicationStatus.Success)
        {
            return null;
        }

        foreach (var service in services.Services)
        {
            using (service)
            {
                var characteristics = await service
                    .GetCharacteristicsForUuidAsync(GattCharacteristicUuids.BatteryLevel, BluetoothCacheMode.Uncached)
                    .AsTask(cancellationToken)
                    .ConfigureAwait(false);
                if (characteristics.Status != GattCommunicationStatus.Success)
                {
                    continue;
                }

                foreach (var characteristic in characteristics.Characteristics)
                {
                    var result = await characteristic
                        .ReadValueAsync(BluetoothCacheMode.Uncached)
                        .AsTask(cancellationToken)
                        .ConfigureAwait(false);
                    if (result.Status != GattCommunicationStatus.Success || result.Value.Length < 1)
                    {
                        continue;
                    }

                    using var reader = DataReader.FromBuffer(result.Value);
                    var percent = Math.Clamp(reader.ReadByte(), (byte)0, (byte)100);
                    return new WindowsBatteryReading(percent, "Bluetooth GATT");
                }
            }
        }

        return null;
    }

    private static string FormatAddress(ulong address)
    {
        return string.Join(
            ":",
            Enumerable.Range(0, 6)
                .Select(index => ((address >> ((5 - index) * 8)) & 0xFF).ToString("X2")));
    }

    private sealed record CacheEntry(WindowsBatteryReading? Reading, DateTimeOffset ExpiresAt);
}
