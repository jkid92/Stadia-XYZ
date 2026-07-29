namespace StadiaX.ControlCenter;

internal readonly record struct ControllerState(
    uint Buttons,
    byte TriggerLeft,
    byte TriggerRight,
    short StickLeftX,
    short StickLeftY,
    short StickRightX,
    short StickRightY)
{
    public bool Has(uint bit) => (Buttons & bit) != 0;
}

internal static class ButtonBits
{
    public const uint A = 1u << 0;
    public const uint B = 1u << 1;
    public const uint X = 1u << 2;
    public const uint Y = 1u << 3;
    public const uint Lb = 1u << 4;
    public const uint Rb = 1u << 5;
    public const uint Select = 1u << 6;
    public const uint Start = 1u << 7;
    public const uint Stadia = 1u << 8;
    public const uint L3 = 1u << 9;
    public const uint R3 = 1u << 10;
    public const uint Assistant = 1u << 11;
    public const uint DpadUp = 1u << 12;
    public const uint DpadDown = 1u << 13;
    public const uint DpadLeft = 1u << 14;
    public const uint DpadRight = 1u << 15;
    public const uint Capture = 1u << 16;
}
