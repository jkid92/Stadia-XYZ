using System.Runtime.InteropServices;

namespace StadiaX.ControlCenter;

internal sealed class DesktopBatteryOverlayForm : Form
{
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;
    private const int WmDisplayChange = 0x007E;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;
    private static readonly IntPtr HwndTopmost = new(-1);

    internal DesktopBatteryOverlayForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ExStyle |= WsExToolWindow | WsExNoActivate;
            return parameters;
        }
    }

    internal void ShowAtDesktopTopRight(Size overlaySize, int margin = 10)
    {
        Size = overlaySize;
        var area = (Screen.PrimaryScreen ?? Screen.AllScreens.First()).WorkingArea;
        var x = area.Right - Width - margin;
        var y = area.Top + margin;

        if (!Visible)
        {
            Show();
        }

        SetWindowPos(
            Handle,
            HwndTopmost,
            x,
            y,
            Width,
            Height,
            SwpNoActivate | SwpShowWindow);
    }

    protected override void WndProc(ref Message message)
    {
        base.WndProc(ref message);
        if (message.Msg == WmDisplayChange && Visible && IsHandleCreated)
        {
            BeginInvoke(new Action(() => ShowAtDesktopTopRight(Size)));
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr window,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);
}
