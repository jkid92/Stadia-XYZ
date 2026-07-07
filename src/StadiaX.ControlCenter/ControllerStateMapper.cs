namespace StadiaX.ControlCenter;

internal static class ControllerStateMapper
{
    public static VigemNative.XusbReport ToXusb(ControllerState state)
    {
        ushort buttons = 0;
        if (state.Has(ButtonBits.A)) buttons |= VigemNative.XusbGamepadA;
        if (state.Has(ButtonBits.B)) buttons |= VigemNative.XusbGamepadB;
        if (state.Has(ButtonBits.X)) buttons |= VigemNative.XusbGamepadX;
        if (state.Has(ButtonBits.Y)) buttons |= VigemNative.XusbGamepadY;
        if (state.Has(ButtonBits.Lb)) buttons |= VigemNative.XusbGamepadLeftShoulder;
        if (state.Has(ButtonBits.Rb)) buttons |= VigemNative.XusbGamepadRightShoulder;
        if (state.Has(ButtonBits.Select)) buttons |= VigemNative.XusbGamepadBack;
        if (state.Has(ButtonBits.Start)) buttons |= VigemNative.XusbGamepadStart;
        if (state.Has(ButtonBits.Stadia)) buttons |= VigemNative.XusbGamepadGuide;
        if (state.Has(ButtonBits.L3)) buttons |= VigemNative.XusbGamepadLeftThumb;
        if (state.Has(ButtonBits.R3)) buttons |= VigemNative.XusbGamepadRightThumb;
        if (state.Has(ButtonBits.DpadUp)) buttons |= VigemNative.XusbGamepadDpadUp;
        if (state.Has(ButtonBits.DpadDown)) buttons |= VigemNative.XusbGamepadDpadDown;
        if (state.Has(ButtonBits.DpadLeft)) buttons |= VigemNative.XusbGamepadDpadLeft;
        if (state.Has(ButtonBits.DpadRight)) buttons |= VigemNative.XusbGamepadDpadRight;

        return new VigemNative.XusbReport
        {
            Buttons = buttons,
            LeftTrigger = state.TriggerLeft,
            RightTrigger = state.TriggerRight,
            ThumbLX = state.StickLeftX,
            ThumbLY = state.StickLeftY == short.MinValue + 1 ? short.MaxValue : (short)-state.StickLeftY,
            ThumbRX = state.StickRightX,
            ThumbRY = state.StickRightY == short.MinValue + 1 ? short.MaxValue : (short)-state.StickRightY
        };
    }
}
