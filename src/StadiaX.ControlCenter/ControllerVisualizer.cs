using System.Drawing.Drawing2D;

namespace StadiaX.ControlCenter;

internal sealed class ControllerVisualizer : Control
{
    private const int SourceWidth = 1024;
    private const int SourceHeight = 768;
    private Image? _controllerImage;
    private ControllerTelemetryRow? _controller;
    private string _status = "Waiting for controller telemetry.";

    public ControllerVisualizer()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        BackColor = Color.FromArgb(18, 24, 33);
        Font = new Font("Segoe UI", 9, FontStyle.Bold);
    }

    public void LoadControllerImage(string path)
    {
        _controllerImage?.Dispose();
        _controllerImage = File.Exists(path) ? Image.FromFile(path) : null;
        Invalidate();
    }

    public void SetTelemetry(ControllerTelemetryRow? controller, string status)
    {
        _controller = controller;
        _status = status;
        Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _controllerImage?.Dispose();
        }
        base.Dispose(disposing);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        using var background = new LinearGradientBrush(ClientRectangle, Color.FromArgb(18, 24, 33), Color.FromArgb(36, 51, 68), 25f);
        g.FillRectangle(background, ClientRectangle);

        var imageBounds = GetImageBounds();
        if (_controllerImage is not null)
        {
            g.DrawImage(_controllerImage, imageBounds);
        }
        else
        {
            DrawMissingImage(g, imageBounds);
        }

        DrawControllerOverlays(g, imageBounds);
        DrawStatus(g);
    }

    private RectangleF GetImageBounds()
    {
        var margin = 18f;
        var available = new RectangleF(margin, margin, Math.Max(1, Width - margin * 2), Math.Max(1, Height - margin * 2 - 30));
        var scale = available.Width / SourceWidth;
        var width = SourceWidth * scale;
        var height = SourceHeight * scale;
        var focusY = 285f * scale;
        var y = available.Top + available.Height / 2f - focusY;
        if (height < available.Height)
        {
            y = available.Top + (available.Height - height) / 2f;
        }

        return new RectangleF(
            available.Left + (available.Width - width) / 2f,
            y,
            width,
            height);
    }

    private void DrawControllerOverlays(Graphics g, RectangleF bounds)
    {
        var controller = _controller;
        if (controller is null)
        {
            DrawIdleMarkers(g, bounds);
            return;
        }

        DrawFaceButton(g, bounds, "y", 685, 80, 43, "Y");
        DrawFaceButton(g, bounds, "b", 735, 126, 43, "B");
        DrawFaceButton(g, bounds, "x", 635, 128, 43, "X");
        DrawFaceButton(g, bounds, "a", 685, 176, 43, "A");

        DrawDpadButton(g, bounds, "dpad_up", 197, 242, 72, 42, "UP");
        DrawDpadButton(g, bounds, "dpad_down", 198, 316, 72, 42, "DOWN");
        DrawDpadButton(g, bounds, "dpad_left", 157, 280, 56, 58, "LEFT");
        DrawDpadButton(g, bounds, "dpad_right", 238, 280, 56, 58, "RIGHT");

        DrawFaceButton(g, bounds, "select", 356, 232, 42, "SEL");
        DrawFaceButton(g, bounds, "assistant", 422, 276, 38, "A");
        DrawFaceButton(g, bounds, "start", 512, 142, 42, "MENU");
        DrawFaceButton(g, bounds, "stadia", 515, 350, 58, "S");

        DrawShoulder(g, bounds, "lb", 300, 135, 155, 54, "LB");
        DrawShoulder(g, bounds, "rb", 612, 64, 168, 54, "RB");
        DrawTrigger(g, bounds, controller.TriggerLeft, 270, 82, 170, 44, "L2");
        DrawTrigger(g, bounds, controller.TriggerRight, 604, 12, 185, 44, "R2");

        DrawStick(g, bounds, "l3", 365, 338, controller.StickLeftX, controller.StickLeftY, "L3");
        DrawStick(g, bounds, "r3", 612, 252, controller.StickRightX, controller.StickRightY, "R3");
    }

    private void DrawIdleMarkers(Graphics g, RectangleF bounds)
    {
        using var brush = new SolidBrush(Color.FromArgb(64, 58, 198, 188));
        using var pen = new Pen(Color.FromArgb(120, 110, 238, 226), 2f);
        foreach (var point in new[] { PointOnImage(bounds, 685, 176), PointOnImage(bounds, 365, 338), PointOnImage(bounds, 612, 252), PointOnImage(bounds, 515, 350) })
        {
            var radius = Scale(bounds, 16);
            g.FillEllipse(brush, point.X - radius, point.Y - radius, radius * 2, radius * 2);
            g.DrawEllipse(pen, point.X - radius, point.Y - radius, radius * 2, radius * 2);
        }
    }

    private void DrawFaceButton(Graphics g, RectangleF bounds, string button, float x, float y, float radius, string label)
    {
        if (!IsPressed(button))
        {
            return;
        }

        var center = PointOnImage(bounds, x, y);
        var scaledRadius = Scale(bounds, radius);
        DrawGlowCircle(g, center, scaledRadius, label, Color.FromArgb(255, 132, 64));
    }

    private void DrawDpadButton(Graphics g, RectangleF bounds, string button, float x, float y, float w, float h, string label)
    {
        if (!IsPressed(button))
        {
            return;
        }

        var rect = RectOnImage(bounds, x - w / 2f, y - h / 2f, w, h);
        DrawGlowCapsule(g, rect, label, Color.FromArgb(74, 220, 211));
    }

    private void DrawShoulder(Graphics g, RectangleF bounds, string button, float x, float y, float w, float h, string label)
    {
        if (!IsPressed(button))
        {
            return;
        }

        DrawGlowCapsule(g, RectOnImage(bounds, x, y, w, h), label, Color.FromArgb(116, 154, 255));
    }

    private void DrawTrigger(Graphics g, RectangleF bounds, int value, float x, float y, float w, float h, string label)
    {
        if (value <= 18)
        {
            return;
        }

        var rect = RectOnImage(bounds, x, y, w, h);
        DrawGlowCapsule(g, rect, $"{label} {value}", Color.FromArgb(255, 206, 78));
    }

    private void DrawStick(Graphics g, RectangleF bounds, string clickButton, float x, float y, int stickX, int stickY, string label)
    {
        var moved = Math.Abs(stickX) > 3500 || Math.Abs(stickY) > 3500;
        var clicked = IsPressed(clickButton);
        if (!moved && !clicked)
        {
            return;
        }

        var center = PointOnImage(bounds, x, y);
        var radius = Scale(bounds, clicked ? 58 : 46);
        var color = clicked ? Color.FromArgb(255, 132, 64) : Color.FromArgb(74, 220, 211);
        DrawGlowCircle(g, center, radius, label, color);

        if (moved)
        {
            var offsetX = NormalizeStick(stickX) * Scale(bounds, 34);
            var offsetY = -NormalizeStick(stickY) * Scale(bounds, 34);
            using var pen = new Pen(Color.FromArgb(240, color), Math.Max(2f, Scale(bounds, 4)));
            using var brush = new SolidBrush(Color.FromArgb(230, color));
            g.DrawLine(pen, center.X, center.Y, center.X + offsetX, center.Y + offsetY);
            var knob = Scale(bounds, 10);
            g.FillEllipse(brush, center.X + offsetX - knob, center.Y + offsetY - knob, knob * 2, knob * 2);
        }
    }

    private void DrawGlowCircle(Graphics g, PointF center, float radius, string label, Color color)
    {
        using var glow = new SolidBrush(Color.FromArgb(72, color));
        using var fill = new SolidBrush(Color.FromArgb(190, color));
        using var border = new Pen(Color.White, Math.Max(1.4f, radius / 16f));
        g.FillEllipse(glow, center.X - radius * 1.75f, center.Y - radius * 1.75f, radius * 3.5f, radius * 3.5f);
        g.FillEllipse(fill, center.X - radius, center.Y - radius, radius * 2, radius * 2);
        g.DrawEllipse(border, center.X - radius, center.Y - radius, radius * 2, radius * 2);
        DrawCenteredText(g, label, new RectangleF(center.X - radius, center.Y - radius / 2f, radius * 2, radius), Color.FromArgb(18, 24, 33));
    }

    private void DrawGlowCapsule(Graphics g, RectangleF rect, string label, Color color)
    {
        using var path = RoundedRect(rect, Math.Min(rect.Width, rect.Height) / 2f);
        using var glowPath = RoundedRect(Inflate(rect, rect.Height * 0.55f), Math.Min(rect.Width, rect.Height));
        using var glow = new SolidBrush(Color.FromArgb(62, color));
        using var fill = new SolidBrush(Color.FromArgb(185, color));
        using var border = new Pen(Color.White, Math.Max(1.2f, rect.Height / 16f));
        g.FillPath(glow, glowPath);
        g.FillPath(fill, path);
        g.DrawPath(border, path);
        DrawCenteredText(g, label, rect, Color.FromArgb(18, 24, 33));
    }

    private void DrawStatus(Graphics g)
    {
        var rect = new RectangleF(12, Height - 31, Width - 24, 22);
        using var brush = new SolidBrush(Color.FromArgb(225, 245, 248, 252));
        using var background = new SolidBrush(Color.FromArgb(142, 10, 16, 24));
        g.FillRectangle(background, rect);
        g.DrawString(_status, Font, brush, rect, StringFormat.GenericDefault);
    }

    private void DrawMissingImage(Graphics g, RectangleF bounds)
    {
        using var fill = new SolidBrush(Color.FromArgb(26, 38, 52));
        using var border = new Pen(Color.FromArgb(84, 109, 132), 2f);
        g.FillRectangle(fill, bounds);
        g.DrawRectangle(border, bounds.X, bounds.Y, bounds.Width, bounds.Height);
        DrawCenteredText(g, "Controller image not found", bounds, Color.FromArgb(226, 232, 240));
    }

    private bool IsPressed(string button)
    {
        return _controller?.Buttons.TryGetValue(button, out var pressed) == true && pressed;
    }

    private static float NormalizeStick(int value)
    {
        return Math.Clamp(value / 32767f, -1f, 1f);
    }

    private static float Scale(RectangleF bounds, float value)
    {
        return value * bounds.Width / SourceWidth;
    }

    private static PointF PointOnImage(RectangleF bounds, float x, float y)
    {
        return new PointF(bounds.Left + x / SourceWidth * bounds.Width, bounds.Top + y / SourceHeight * bounds.Height);
    }

    private static RectangleF RectOnImage(RectangleF bounds, float x, float y, float w, float h)
    {
        return new RectangleF(
            bounds.Left + x / SourceWidth * bounds.Width,
            bounds.Top + y / SourceHeight * bounds.Height,
            w / SourceWidth * bounds.Width,
            h / SourceHeight * bounds.Height);
    }

    private static RectangleF Inflate(RectangleF rect, float value)
    {
        return new RectangleF(rect.X - value, rect.Y - value, rect.Width + value * 2, rect.Height + value * 2);
    }

    private static GraphicsPath RoundedRect(RectangleF rect, float radius)
    {
        var path = new GraphicsPath();
        var diameter = radius * 2;
        path.AddArc(rect.Left, rect.Top, diameter, diameter, 180, 90);
        path.AddArc(rect.Right - diameter, rect.Top, diameter, diameter, 270, 90);
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rect.Left, rect.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private void DrawCenteredText(Graphics g, string text, RectangleF rect, Color color)
    {
        using var brush = new SolidBrush(color);
        using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString(text, Font, brush, rect, format);
    }
}
