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
    private ControllerInputButton? _selectedInput;
    private string _status = "Waiting for controller telemetry.";
    private string _missingImageDetail = "Controller image not found";

    public ControllerVisualizer()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        TabStop = true;
        AccessibleRole = AccessibleRole.Diagram;
        AccessibleName = "Stadia controller input map";
        BackColor = SurfaceBottom;
        Font = new Font("Segoe UI", 9, FontStyle.Bold);
    }

    public event Action<ControllerInputButton>? InputSelected;

    public ControllerInputButton? SelectedInput
    {
        get => _selectedInput;
        set
        {
            if (_selectedInput == value)
            {
                return;
            }

            _selectedInput = value;
            Invalidate();
        }
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

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        Cursor = HitTestInput(e.Location) is null ? Cursors.Default : Cursors.Hand;
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        Cursor = Cursors.Default;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        var input = HitTestInput(e.Location);
        if (input is null)
        {
            return;
        }

        SelectedInput = input;
        InputSelected?.Invoke(input.Value);
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
        DrawCircleButton(g, bounds, "y", 1487, 162, 50, "", FaceGlow);
        DrawCircleButton(g, bounds, "b", 1605, 269, 50, "", FaceGlow);
        DrawCircleButton(g, bounds, "x", 1384, 270, 50, "", FaceGlow);
        DrawCircleButton(g, bounds, "a", 1485, 373, 50, "", FaceGlow);

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
        var active = IsPressed(button);
        var selected = IsSelected(button);
        if (!active && !selected)
        {
            return;
        }

        using var path = RoundedRect(RectOnImage(bounds, x, y, w, h), Scale(bounds, 28));
        DrawContour(g, path, active, label, DpadGlow, selected: selected);
    }

    private void DrawCircleButton(Graphics g, RectangleF bounds, string button, float x, float y, float radius, string label, Color color)
    {
        using var path = new GraphicsPath();
        path.AddEllipse(RectOnImage(bounds, x - radius, y - radius, radius * 2, radius * 2));
        DrawContour(g, path, IsPressed(button), label, color, selected: IsSelected(button));
    }

    private void DrawPillButton(Graphics g, RectangleF bounds, string button, float centerX, float centerY, float width, float height, string label, Color color)
    {
        using var path = RoundedRect(RectOnImage(bounds, centerX - width / 2f, centerY - height / 2f, width, height), Scale(bounds, height / 2f));
        DrawContour(g, path, IsPressed(button), label, color, selected: IsSelected(button));
    }

    private void DrawVisibleShoulder(Graphics g, RectangleF bounds, string button, float x, float y, float width, float height, string label)
    {
        using var path = RoundedRect(RectOnImage(bounds, x, y, width, height), Scale(bounds, 42));
        DrawContour(g, path, IsPressed(button), label, SystemGlow, selected: IsSelected(button));
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
        DrawContour(
            g,
            path,
            moved || clicked,
            clicked ? label : "",
            clicked ? FaceGlow : DpadGlow,
            selected: IsSelected(clickButton));

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

    private void DrawContour(
        Graphics g,
        GraphicsPath path,
        bool active,
        string label,
        Color color,
        bool activeOnlyFill = false,
        bool selected = false)
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
        else if (selected)
        {
            using var selectedFill = new SolidBrush(Color.FromArgb(58, SystemGlow));
            using var selectedBorder = new Pen(Color.FromArgb(245, 35, 116, 146), Math.Max(2.2f, Width / 620f))
            {
                LineJoin = LineJoin.Round
            };
            g.FillPath(selectedFill, path);
            g.DrawPath(selectedBorder, path);
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

        if ((active || selected) && !string.IsNullOrWhiteSpace(label))
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

    private bool IsSelected(string telemetryKey)
    {
        return _selectedInput is not null &&
               ControllerButtonCatalog.FindInput(telemetryKey)?.Id == _selectedInput;
    }

    private ControllerInputButton? HitTestInput(Point location)
    {
        var bounds = GetImageBounds();
        if (!bounds.Contains(location))
        {
            return null;
        }

        var source = new PointF(
            (location.X - bounds.Left) * SourceWidth / bounds.Width,
            (location.Y - bounds.Top) * SourceHeight / bounds.Height);

        if (HitRectangle(source, 545, 34, 370, 86)) return ControllerInputButton.Lb;
        if (HitRectangle(source, 1318, 34, 370, 86)) return ControllerInputButton.Rb;
        if (HitCircle(source, 1487, 162, 70)) return ControllerInputButton.Y;
        if (HitCircle(source, 1605, 269, 70)) return ControllerInputButton.B;
        if (HitCircle(source, 1384, 270, 70)) return ControllerInputButton.X;
        if (HitCircle(source, 1485, 373, 70)) return ControllerInputButton.A;
        if (HitRectangle(source, 782, 125, 114, 66)) return ControllerInputButton.Select;
        if (HitRectangle(source, 1157, 125, 114, 66)) return ControllerInputButton.Start;
        if (HitCircle(source, 908, 274, 52)) return ControllerInputButton.Assistant;
        if (HitCircle(source, 1146, 272, 52)) return ControllerInputButton.Capture;
        if (HitCircle(source, 1027, 496, 76)) return ControllerInputButton.Stadia;
        if (HitCircle(source, 755, 501, 108)) return ControllerInputButton.L3;
        if (HitCircle(source, 1286, 496, 108)) return ControllerInputButton.R3;
        if (HitRectangle(source, 520, 140, 96, 108)) return ControllerInputButton.DpadUp;
        if (HitRectangle(source, 520, 300, 96, 108)) return ControllerInputButton.DpadDown;
        if (HitRectangle(source, 420, 226, 112, 98)) return ControllerInputButton.DpadLeft;
        if (HitRectangle(source, 602, 226, 104, 98)) return ControllerInputButton.DpadRight;
        return null;
    }

    private static bool HitRectangle(PointF point, float x, float y, float width, float height)
    {
        return point.X >= x && point.X <= x + width && point.Y >= y && point.Y <= y + height;
    }

    private static bool HitCircle(PointF point, float centerX, float centerY, float radius)
    {
        var dx = point.X - centerX;
        var dy = point.Y - centerY;
        return dx * dx + dy * dy <= radius * radius;
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
