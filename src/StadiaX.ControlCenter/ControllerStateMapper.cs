namespace StadiaX.ControlCenter;

internal static class ControllerStateMapper
{
    public static VirtualGamepadReport ToXusb(ControllerState state, ControllerButtonMapping? mapping = null)
    {
        mapping ??= ControllerButtonMapping.CreateDefault();

        return new VirtualGamepadReport
        {
            Buttons = mapping.MapButtons(state),
            LeftTrigger = state.TriggerLeft,
            RightTrigger = state.TriggerRight,
            ThumbLX = state.StickLeftX,
            ThumbLY = state.StickLeftY,
            ThumbRX = state.StickRightX,
            ThumbRY = state.StickRightY
        };
    }

    internal static void RunSelfTest()
    {
        var state = new ControllerState(
            ButtonBits.A | ButtonBits.Start,
            17,
            231,
            -12000,
            -21000,
            13000,
            short.MinValue + 1);
        var report = ToXusb(state);
        if (report.Buttons != (XboxButtonBits.A | XboxButtonBits.Start) ||
            report.LeftTrigger != 17 ||
            report.RightTrigger != 231 ||
            report.ThumbLX != -12000 ||
            report.ThumbLY != -21000 ||
            report.ThumbRX != 13000 ||
            report.ThumbRY != short.MinValue + 1)
        {
            throw new InvalidOperationException("Controller-to-virtual-pad mapping self-test failed.");
        }
    }
}
