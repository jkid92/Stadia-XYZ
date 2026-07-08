using System.Drawing.Drawing2D;

namespace StadiaX.ControlCenter;

internal sealed class ControllerVisualizer : Control
{
    private const int SourceWidth = 2048;
    private const int SourceHeight = 1024;
    private static readonly Color FaceGlow = Color.FromArgb(255, 132, 64);
    private static readonly Color DpadGlow = Color.FromArgb(74, 220, 211);
    private static readonly Color SystemGlow = Color.FromArgb(116, 154, 255);
    private static readonly Color TriggerGlow = Color.FromArgb(255, 206, 78);
    private static readonly Color SurfaceTop = Color.FromArgb(255, 255, 255);
    private static readonly Color SurfaceBottom = Color.FromArgb(246, 246, 246);

    private Image? _controllerImage;
    private ControllerTelemetryRow? _controller;
    private string _status = "Waiting for controller telemetry.";
    private string _missingImageDetail = "Controller image not found";

    public ControllerVisualizer()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        BackColor = SurfaceBottom;
        Font = new Font("Segoe UI", 9, FontStyle.Bold);
    }

    public bool LoadControllerImage(params string[] candidatePaths)
    {
        _controllerImage?.Dispose();
        _controllerImage = null;

        foreach (var path in candidatePaths.Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                using var stream = File.OpenRead(path);
                using var source = Image.FromStream(stream);
                _controllerImage = new Bitmap(source);
                _missingImageDetail = "";
                Invalidate();
                return true;
            }
            catch (Exception ex)
            {
                _missingImageDetail = "Controller image could not be loaded: " + ex.Message;
            }
        }

        if (_controllerImage is null && string.IsNullOrWhiteSpace(_missingImageDetail))
        {
            _missingImageDetail = "Controller image not found";
        }

        Invalidate();
        return false;
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

        using var background = new LinearGradientBrush(ClientRectangle, SurfaceTop, SurfaceBottom, 90f);
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
    }

    private RectangleF GetImageBounds()
    {
        var margin = 18f;
        var available = new RectangleF(margin, margin, Math.Max(1, Width - margin * 2), Math.Max(1, Height - margin * 2));
        var scale = Math.Min(available.Width / SourceWidth, available.Height / SourceHeight);
        var width = SourceWidth * scale;
        var height = SourceHeight * scale;
        var x = available.Left + (available.Width - width) / 2f;
        var y = available.Top + (available.Height - height) / 2f;
        return new RectangleF(x, y, width, height);
    }

    private void DrawControllerOverlays(Graphics g, RectangleF bounds)
    {
        DrawVirtualTrigger(g, bounds, "L2", _controller?.TriggerLeft ?? 0, 0.22f);
        DrawVirtualTrigger(g, bounds, "R2", _controller?.TriggerRight ?? 0, 0.62f);

        DrawDpad(g, bounds);
        DrawCircleButton(g, bounds, "y", 1487, 162, 50, "Y", FaceGlow);
        DrawCircleButton(g, bounds, "b", 1605, 269, 50, "B", FaceGlow);
        DrawCircleButton(g, bounds, "x", 1384, 270, 50, "X", FaceGlow);
        DrawCircleButton(g, bounds, "a", 1485, 373, 50, "A", FaceGlow);

        DrawPillButton(g, bounds, "select", 839, 157, 94, 48, "SEL", SystemGlow);
        DrawPillButton(g, bounds, "start", 1214, 157, 94, 48, "MENU", SystemGlow);
        DrawCircleButton(g, bounds, "assistant", 908, 274, 34, "AST", SystemGlow);
        DrawCircleButton(g, bounds, "capture", 1146, 272, 34, "CAP", SystemGlow);
        DrawCircleButton(g, bounds, "stadia", 1027, 496, 58, "S", SystemGlow);

        DrawStick(g, bounds, "l3", 755, 501, _controller?.StickLeftX ?? 0, _controller?.StickLeftY ?? 0, "L3");
        DrawStick(g, bounds, "r3", 1286, 496, _controller?.StickRightX ?? 0, _controller?.StickRightY ?? 0, "R3");

        DrawVisibleShoulder(g, bounds, "lb", 545, 34, 370, 72, "LB");
        DrawVisibleShoulder(g, bounds, "rb", 1318, 34, 370, 72, "RB");
    }

    private void DrawDpad(Graphics g, RectangleF bounds)
    {
        using var fullPath = new GraphicsPath();
        fullPath.AddPath(RoundedRect(RectOnImage(bounds, 520, 144, 92, 256), Scale(bounds, 42)), false);
        fullPath.AddPath(RoundedRect(RectOnImage(bounds, 428, 232, 270, 86), Scale(bounds, 42)), false);
        DrawContour(g, fullPath, IsPressed("dpad_up") || IsPressed("dpad_down") || IsPressed("dpad_left") || IsPressed("dpad_right"), "", DpadGlow);

        DrawDpadSegment(g, bounds, "dpad_up", 522, 144, 88, 100, "UP");
        DrawDpadSegment(g, bounds, "dpad_down", 522, 306, 88, 94, "DOWN");
        DrawDpadSegment(g, bounds, "dpad_left", 428, 232, 96, 86, "LEFT");
        DrawDpadSegment(g, bounds, "dpad_right", 610, 232, 88, 86, "RIGHT");
    }

    private void DrawDpadSegment(Graphics g, RectangleF bounds, string button, float x, float y, float w, float h, string label)
    {
        if (!IsPressed(button))
        {
            return;
        }

        using var path = RoundedRect(RectOnImage(bounds, x, y, w, h), Scale(bounds, 28));
        DrawContour(g, path, true, label, DpadGlow);
    }

    private void DrawCircleButton(Graphics g, RectangleF bounds, string button, float x, float y, float radius, string label, Color color)
    {
        using var path = new GraphicsPath();
        path.AddEllipse(RectOnImage(bounds, x - radius, y - radius, radius * 2, radius * 2));
        DrawContour(g, path, IsPressed(button), label, color);
    }

    private void DrawPillButton(Graphics g, RectangleF bounds, string button, float centerX, float centerY, float width, float height, string label, Color color)
    {
        using var path = RoundedRect(RectOnImage(bounds, centerX - width / 2f, centerY - height / 2f, width, height), Scale(bounds, height / 2f));
        DrawContour(g, path, IsPressed(button), label, color);
    }

    private void DrawVisibleShoulder(Graphics g, RectangleF bounds, string button, float x, float y, float width, float height, string label)
    {
        using var path = RoundedRect(RectOnImage(bounds, x, y, width, height), Scale(bounds, 42));
        DrawContour(g, path, IsPressed(button), label, SystemGlow);
    }

    private void DrawVirtualTrigger(Graphics g, RectangleF bounds, string label, int value, float xRatio)
    {
        var width = Math.Clamp(bounds.Width * 0.18f, 126f, 188f);
        var height = Math.Clamp(bounds.Height * 0.08f, 34f, 46f);
        var x = bounds.Left + bounds.Width * xRatio;
        var y = bounds.Top - height - 10f;
        if (y < 8f)
        {
            y = bounds.Top + 10f;
        }

        var rect = new RectangleF(x, y, width, height);
        using var path = RoundedRect(rect, Scale(bounds, 24));
        var active = value > 18;

        using var idleFill = new SolidBrush(Color.FromArgb(active ? 160 : 92, 16, 22, 30));
        using var idleBorder = new Pen(Color.FromArgb(180, 230, 236, 244), Math.Max(1.4f, Scale(bounds, 3)));
        g.FillPath(idleFill, path);
        g.DrawPath(idleBorder, path);
        DrawContour(g, path, active, active ? $"{label} {value}" : label, TriggerGlow, activeOnlyFill: true);
        DrawCenteredText(g, active ? $"{label} {value}" : label, rect, active ? Color.FromArgb(18, 24, 33) : Color.FromArgb(230, 236, 244));
    }

    private void DrawStick(Graphics g, RectangleF bounds, string clickButton, float x, float y, int stickX, int stickY, string label)
    {
        var moved = Math.Abs(stickX) > 3500 || Math.Abs(stickY) > 3500;
        var clicked = IsPressed(clickButton);
        using var path = new GraphicsPath();
        path.AddEllipse(RectOnImage(bounds, x - 94, y - 94, 188, 188));
        DrawContour(g, path, moved || clicked, clicked ? label : "", clicked ? FaceGlow : DpadGlow);

        if (moved)
        {
            var center = PointOnImage(bounds, x, y);
            var offsetX = NormalizeStick(stickX) * Scale(bounds, 44);
            var offsetY = -NormalizeStick(stickY) * Scale(bounds, 44);
            using var pen = new Pen(Color.FromArgb(245, DpadGlow), Math.Max(2f, Scale(bounds, 5)));
            using var brush = new SolidBrush(Color.FromArgb(235, DpadGlow));
            g.DrawLine(pen, center.X, center.Y, center.X + offsetX, center.Y + offsetY);
            var knob = Scale(bounds, 12);
            g.FillEllipse(brush, center.X + offsetX - knob, center.Y + offsetY - knob, knob * 2, knob * 2);
        }
    }

    private void DrawContour(Graphics g, GraphicsPath path, bool active, string label, Color color, bool activeOnlyFill = false)
    {
        if (active)
        {
            for (var i = 5; i >= 1; i--)
            {
                using var glowPen = new Pen(Color.FromArgb(20 + i * 13, color), i * Math.Max(2.8f, Width / 390f))
                {
                    LineJoin = LineJoin.Round
                };
                g.DrawPath(glowPen, path);
            }

            using var fill = new SolidBrush(Color.FromArgb(88, color));
            g.FillPath(fill, path);
            using var hotBorder = new Pen(Color.FromArgb(245, 255, 255, 255), Math.Max(1.5f, Width / 760f))
            {
                LineJoin = LineJoin.Round
            };
            g.DrawPath(hotBorder, path);
        }
        else if (!activeOnlyFill)
        {
            using var outline = new Pen(Color.FromArgb(95, 72, 220, 211), Math.Max(1f, Width / 1120f))
            {
                DashStyle = DashStyle.Solid,
                LineJoin = LineJoin.Round
            };
            g.DrawPath(outline, path);
        }

        if (active && !string.IsNullOrWhiteSpace(label))
        {
            DrawCenteredText(g, label, path.GetBounds(), Color.FromArgb(12, 18, 26));
        }
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
        using var fill = new SolidBrush(Color.FromArgb(241, 245, 249));
        using var border = new Pen(Color.FromArgb(148, 163, 184), 2f);
        g.FillRectangle(fill, bounds);
        g.DrawRectangle(border, bounds.X, bounds.Y, bounds.Width, bounds.Height);
        DrawCenteredText(g, _missingImageDetail, bounds, Color.FromArgb(51, 65, 85));
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

    private static GraphicsPath RoundedRect(RectangleF rect, float radius)
    {
        var path = new GraphicsPath();
        radius = Math.Min(radius, Math.Min(rect.Width, rect.Height) / 2f);
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
