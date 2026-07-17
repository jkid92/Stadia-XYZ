using System.Runtime.InteropServices;

namespace StadiaX.ControlCenter;

internal sealed record WindowsBatteryReading(int Percent, string Source);

internal static class WindowsNativeBatteryReader
{
    private const int CrSuccess = 0;
    private const int CrBufferSmall = 0x1A;
    private const uint DevPropTypeByte = 0x00000003;
    private const uint DevPropTypeUInt32 = 0x00000007;
    private const int MaximumParentDepth = 8;

    private static DevPropKey DeviceBatteryLevel = new(
        new Guid("104EA319-6EE2-4701-BD47-8DDBF425BBE5"),
        2);

    private static DevPropKey ContainerBatteryLevel = new(
        new Guid("CD600218-4DD5-49E6-97D7-086D4DEACED1"),
        2);

    internal static WindowsBatteryReading? Read(string deviceInstancePath)
    {
        if (string.IsNullOrWhiteSpace(deviceInstancePath) ||
            CM_Locate_DevNodeW(out var deviceNode, deviceInstancePath.Trim(), 0) != CrSuccess)
        {
            return null;
        }

        for (var depth = 0; depth <= MaximumParentDepth; depth++)
        {
            if (TryReadPercent(deviceNode, ref DeviceBatteryLevel, out var devicePercent))
            {
                return new WindowsBatteryReading(devicePercent, depth == 0 ? "Windows device" : "Windows parent device");
            }

            if (TryReadPercent(deviceNode, ref ContainerBatteryLevel, out var containerPercent))
            {
                return new WindowsBatteryReading(containerPercent, depth == 0 ? "Windows container" : "Windows parent container");
            }

            if (CM_Get_Parent(out deviceNode, deviceNode, 0) != CrSuccess)
            {
                break;
            }
        }

        return null;
    }

    internal static int? ParsePercent(uint propertyType, ReadOnlySpan<byte> data)
    {
        var value = propertyType switch
        {
            DevPropTypeByte when data.Length >= 1 => data[0],
            DevPropTypeUInt32 when data.Length >= sizeof(uint) => (int)BitConverter.ToUInt32(data[..sizeof(uint)]),
            _ => -1
        };
        return value is >= 0 and <= 100 ? value : null;
    }

    internal static void RunSelfTest()
    {
        if (ParsePercent(DevPropTypeByte, new byte[] { 73 }) != 73 ||
            ParsePercent(DevPropTypeUInt32, BitConverter.GetBytes(44U)) != 44 ||
            ParsePercent(DevPropTypeByte, new byte[] { 101 }) is not null ||
            Read("NOT_A_DEVICE_INSTANCE") is not null)
        {
            throw new InvalidOperationException("Windows battery reader failed its self-test.");
        }

        var presentDevice = Environment.GetEnvironmentVariable("STADIAX_BATTERY_TEST_DEVICE");
        if (!string.IsNullOrWhiteSpace(presentDevice))
        {
            _ = Read(presentDevice);
        }
    }

    private static bool TryReadPercent(uint deviceNode, ref DevPropKey key, out int percent)
    {
        var buffer = new byte[sizeof(uint)];
        uint size = (uint)buffer.Length;
        var result = CM_Get_DevNode_PropertyW(deviceNode, ref key, out var propertyType, buffer, ref size, 0);
        if (result == CrBufferSmall && size is > sizeof(uint) and <= 64)
        {
            buffer = new byte[size];
            result = CM_Get_DevNode_PropertyW(deviceNode, ref key, out propertyType, buffer, ref size, 0);
        }

        var parsed = result == CrSuccess
            ? ParsePercent(propertyType, buffer.AsSpan(0, Math.Min(buffer.Length, checked((int)size))))
            : null;
        percent = parsed ?? 0;
        return parsed.HasValue;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DevPropKey(Guid formatId, uint propertyId)
    {
        internal Guid FormatId = formatId;
        internal uint PropertyId = propertyId;
    }

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Locate_DevNodeW(out uint deviceNode, string deviceId, uint flags);

    [DllImport("cfgmgr32.dll")]
    private static extern int CM_Get_DevNode_PropertyW(
        uint deviceNode,
        ref DevPropKey propertyKey,
        out uint propertyType,
        [Out] byte[] propertyBuffer,
        ref uint propertyBufferSize,
        uint flags);

    [DllImport("cfgmgr32.dll")]
    private static extern int CM_Get_Parent(out uint parentDeviceNode, uint deviceNode, uint flags);
}
