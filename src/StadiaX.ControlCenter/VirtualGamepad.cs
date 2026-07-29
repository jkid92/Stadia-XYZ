namespace StadiaX.ControlCenter;

internal static class XboxButtonBits
{
    public const ushort DpadUp = 0x0001;
    public const ushort DpadDown = 0x0002;
    public const ushort DpadLeft = 0x0004;
    public const ushort DpadRight = 0x0008;
    public const ushort Start = 0x0010;
    public const ushort Back = 0x0020;
    public const ushort LeftThumb = 0x0040;
    public const ushort RightThumb = 0x0080;
    public const ushort LeftShoulder = 0x0100;
    public const ushort RightShoulder = 0x0200;
    public const ushort Guide = 0x0400;
    public const ushort A = 0x1000;
    public const ushort B = 0x2000;
    public const ushort X = 0x4000;
    public const ushort Y = 0x8000;
}

[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
internal struct VirtualGamepadReport
{
    public ushort Buttons;
    public byte LeftTrigger;
    public byte RightTrigger;
    public short ThumbLX;
    public short ThumbLY;
    public short ThumbRX;
    public short ThumbRY;
}

internal interface IVirtualGamepadBus : IDisposable
{
    int ControllerCount { get; }

    IReadOnlyList<string> InitializationWarnings { get; }

    event Action<int, byte, byte>? RumbleRequested;

    bool TryUpdate(int controllerIndex, VirtualGamepadReport report, out string error);

    bool TryNeutralize(int controllerIndex, out string error);
}

internal interface IVirtualGamepadBusFactory
{
    IVirtualGamepadBus Create(int controllerCount);
}
