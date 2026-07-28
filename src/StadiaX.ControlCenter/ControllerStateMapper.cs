namespace StadiaX.ControlCenter;

internal static class ControllerStateMapper
{
    public static VigemNative.XusbReport ToXusb(ControllerState state, ControllerButtonMapping? mapping = null)
    {
        mapping ??= ControllerButtonMapping.CreateDefault();

        return new VigemNative.XusbReport
        {
            Buttons = mapping.MapButtons(state),
            LeftTrigger = state.TriggerLeft,
            RightTrigger = state.TriggerRight,
            ThumbLX = state.StickLeftX,
            ThumbLY = state.StickLeftY == short.MinValue + 1 ? short.MaxValue : (short)-state.StickLeftY,
            ThumbRX = state.StickRightX,
            ThumbRY = state.StickRightY == short.MinValue + 1 ? short.MaxValue : (short)-state.StickRightY
        };
    }
}
