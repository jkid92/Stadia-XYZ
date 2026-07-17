using System.Runtime.InteropServices;

namespace StadiaX.ControlCenter;

internal static class DisplayLayout
{
    internal const int BaseDpi = 96;

    internal static int SystemDpi
    {
        get
        {
            try
            {
                return Math.Max(BaseDpi, checked((int)GetDpiForSystem()));
            }
            catch
            {
                return BaseDpi;
            }
        }
    }

    internal static int AuditScalePercent
    {
        get
        {
            var value = Environment.GetEnvironmentVariable("STADIAX_UI_SCALE_PERCENT");
            return int.TryParse(value, out var percent) ? Math.Clamp(percent, 100, 200) : 100;
        }
    }

    internal static bool IsConstrained(Rectangle workingArea, int dpi)
    {
        var logical = ToLogical(workingArea.Size, dpi);
        return logical.Width < 1240 || logical.Height < 760;
    }

    internal static (Size Minimum, Size Initial) CalculateStartupSizing(bool compactUi, Rectangle workingArea, int dpi)
    {
        var constrained = IsConstrained(workingArea, dpi);
        var desired = compactUi ? new Size(1120, 720) : new Size(1280, 820);
        var desiredMinimum = compactUi
            ? new Size(constrained ? 820 : 1080, constrained ? 520 : 560)
            : new Size(constrained ? 900 : 1100, constrained ? 560 : 620);
        var logicalArea = ToLogical(workingArea.Size, dpi);
        var margin = logicalArea.Width < 900 || logicalArea.Height < 640 ? 20 : 48;
        var available = new Size(
            Math.Max(560, logicalArea.Width - margin),
            Math.Max(420, logicalArea.Height - margin));

        var minimum = new Size(
            Math.Min(desiredMinimum.Width, available.Width),
            Math.Min(desiredMinimum.Height, available.Height));
        var initial = new Size(
            Math.Max(minimum.Width, Math.Min(desired.Width, available.Width)),
            Math.Max(minimum.Height, Math.Min(desired.Height, available.Height)));
        return (minimum, initial);
    }

    internal static (Rectangle Bounds, Size Minimum) FitWindow(
        Rectangle currentBounds,
        Size currentMinimum,
        Rectangle workingArea)
    {
        var margin = workingArea.Width < 900 || workingArea.Height < 640 ? 20 : 48;
        var maxWidth = Math.Max(560, workingArea.Width - margin);
        var maxHeight = Math.Max(420, workingArea.Height - margin);
        var minimum = new Size(
            Math.Min(currentMinimum.Width, maxWidth),
            Math.Min(currentMinimum.Height, maxHeight));
        var width = Math.Max(minimum.Width, Math.Min(currentBounds.Width, maxWidth));
        var height = Math.Max(minimum.Height, Math.Min(currentBounds.Height, maxHeight));
        var x = Math.Min(Math.Max(currentBounds.Left, workingArea.Left), Math.Max(workingArea.Left, workingArea.Right - width));
        var y = Math.Min(Math.Max(currentBounds.Top, workingArea.Top), Math.Max(workingArea.Top, workingArea.Bottom - height));
        return (new Rectangle(x, y, width, height), minimum);
    }

    internal static Size ToLogical(Size physical, int dpi)
    {
        dpi = Math.Max(BaseDpi, dpi);
        return new Size(
            Math.Max(1, (int)Math.Floor(physical.Width * BaseDpi / (double)dpi)),
            Math.Max(1, (int)Math.Floor(physical.Height * BaseDpi / (double)dpi)));
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll")]
    private static extern uint GetDpiForSystem();
}
