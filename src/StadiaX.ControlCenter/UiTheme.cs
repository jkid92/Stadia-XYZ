using System.Drawing.Drawing2D;

namespace StadiaX.ControlCenter;

internal static class UiTheme
{
    internal static readonly Color Canvas = Color.FromArgb(244, 247, 250);
    internal static readonly Color Surface = Color.White;
    internal static readonly Color SurfaceMuted = Color.FromArgb(249, 251, 253);
    internal static readonly Color HeaderTop = Color.FromArgb(17, 30, 45);
    internal static readonly Color HeaderBottom = Color.FromArgb(24, 47, 64);
    internal static readonly Color TextPrimary = Color.FromArgb(25, 34, 47);
    internal static readonly Color TextMuted = Color.FromArgb(91, 105, 124);
    internal static readonly Color Border = Color.FromArgb(218, 226, 234);
    internal static readonly Color BorderStrong = Color.FromArgb(197, 209, 221);
    internal static readonly Color Accent = Color.FromArgb(18, 150, 151);
    internal static readonly Color AccentDark = Color.FromArgb(15, 112, 114);
    internal static readonly Color AccentSoft = Color.FromArgb(226, 246, 245);
    internal static readonly Color Success = Color.FromArgb(38, 136, 88);
    internal static readonly Color Danger = Color.FromArgb(187, 69, 69);
    internal static readonly Color Warning = Color.FromArgb(196, 126, 33);
    internal static readonly Color LogSurface = Color.FromArgb(18, 24, 33);
    internal static readonly Color LogText = Color.FromArgb(222, 231, 240);

    internal static GraphicsPath RoundedPath(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        var diameter = Math.Max(1, Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height)));
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    internal static Color OpaqueParentColor(Control control)
    {
        var color = control.Parent?.BackColor ?? Canvas;
        return color.A == byte.MaxValue ? color : Canvas;
    }
}

internal sealed class SurfaceGroupBox : GroupBox
{
    internal SurfaceGroupBox()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);
        BackColor = UiTheme.Surface;
        ForeColor = UiTheme.TextPrimary;
        FlatStyle = FlatStyle.Flat;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.Clear(UiTheme.OpaqueParentColor(this));

        var bounds = new Rectangle(0, 5, Math.Max(1, Width - 1), Math.Max(1, Height - 6));
        using (var path = UiTheme.RoundedPath(bounds, 7))
        using (var fill = new SolidBrush(UiTheme.Surface))
        using (var border = new Pen(UiTheme.Border))
        {
            e.Graphics.FillPath(fill, path);
            e.Graphics.DrawPath(border, path);
        }

        if (string.IsNullOrWhiteSpace(Text))
        {
            return;
        }

        var measureFlags = TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;
        var drawFlags = measureFlags | TextFormatFlags.EndEllipsis;
        var measured = TextRenderer.MeasureText(e.Graphics, Text, Font, new Size(int.MaxValue, int.MaxValue), measureFlags);
        var textBounds = new Rectangle(12, 0, Math.Max(1, Math.Min(measured.Width + 8, Width - 24)), measured.Height + 2);
        using (var textBackground = new SolidBrush(UiTheme.Surface))
        {
            e.Graphics.FillRectangle(textBackground, textBounds);
        }
        TextRenderer.DrawText(
            e.Graphics,
            Text,
            Font,
            new Rectangle(textBounds.Left + 4, textBounds.Top, Math.Max(1, textBounds.Width - 8), textBounds.Height),
            Enabled ? UiTheme.TextPrimary : UiTheme.TextMuted,
            drawFlags | TextFormatFlags.VerticalCenter);
    }
}

internal sealed class ModernButton : Button
{
    private bool _hover;
    private bool _pressed;

    internal ModernButton()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hover = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hover = false;
        _pressed = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        _pressed = e.Button == MouseButtons.Left;
        Invalidate();
        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        _pressed = false;
        Invalidate();
        base.OnMouseUp(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.Clear(UiTheme.OpaqueParentColor(this));

        var fillColor = !Enabled
            ? Color.FromArgb(236, 240, 244)
            : _pressed && FlatAppearance.MouseDownBackColor != Color.Empty
                ? FlatAppearance.MouseDownBackColor
                : _hover && FlatAppearance.MouseOverBackColor != Color.Empty
                    ? FlatAppearance.MouseOverBackColor
                    : BackColor;
        var textColor = Enabled ? ForeColor : UiTheme.TextMuted;
        var bounds = new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
        var radius = Math.Max(5, 6 * DeviceDpi / 96);
        using (var path = UiTheme.RoundedPath(bounds, radius))
        using (var fill = new SolidBrush(fillColor))
        {
            e.Graphics.FillPath(fill, path);
            if (FlatAppearance.BorderSize > 0)
            {
                using var border = new Pen(FlatAppearance.BorderColor, FlatAppearance.BorderSize);
                e.Graphics.DrawPath(border, path);
            }
        }

        TextRenderer.DrawText(
            e.Graphics,
            Text,
            Font,
            Rectangle.Inflate(bounds, -Padding.Horizontal / 2 - 4, -2),
            textColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);

        if (Focused && ShowFocusCues)
        {
            var focusBounds = Rectangle.Inflate(bounds, -4, -4);
            ControlPaint.DrawFocusRectangle(e.Graphics, focusBounds, textColor, fillColor);
        }
    }
}
