using System.Drawing.Drawing2D;

namespace StadiaX.ControlCenter;

internal sealed class ControllerMappingDiagram : Control
{
    private static readonly XboxOutputButton[] LeftOutputs =
    [
        XboxOutputButton.LeftShoulder,
        XboxOutputButton.Back,
        XboxOutputButton.LeftStick,
        XboxOutputButton.DpadUp,
        XboxOutputButton.DpadLeft,
        XboxOutputButton.DpadRight,
        XboxOutputButton.DpadDown
    ];

    private static readonly XboxOutputButton[] RightOutputs =
    [
        XboxOutputButton.RightShoulder,
        XboxOutputButton.Start,
        XboxOutputButton.RightStick,
        XboxOutputButton.Y,
        XboxOutputButton.X,
        XboxOutputButton.B,
        XboxOutputButton.A,
        XboxOutputButton.Guide
    ];

    private readonly Dictionary<XboxOutputButton, Rectangle> _nodeBounds = new();
    private readonly HashSet<ControllerInputButton> _pressedInputs = [];
    private ControllerButtonMapping _mapping = ControllerButtonMapping.CreateDefault();
    private XboxOutputButton _selectedOutput = XboxOutputButton.A;

    public ControllerMappingDiagram()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        TabStop = true;
        AccessibleRole = AccessibleRole.Diagram;
        AccessibleName = "Xbox controller mapping";
        BackColor = Color.FromArgb(248, 250, 252);
        ForeColor = Color.FromArgb(26, 38, 54);
        Font = new Font("Segoe UI", 8.5f, FontStyle.Regular);
    }

    public event Action<XboxOutputButton>? OutputSelected;

    public XboxOutputButton SelectedOutput
    {
        get => _selectedOutput;
        set
        {
            if (value == XboxOutputButton.None || _selectedOutput == value)
            {
                return;
            }

            _selectedOutput = value;
            Invalidate();
        }
    }

    public void SetMapping(ControllerButtonMapping mapping)
    {
        _mapping = mapping;
        Invalidate();
    }

    public void SetPressedInputs(IEnumerable<ControllerInputButton> inputs)
    {
        var next = inputs.ToHashSet();
        if (_pressedInputs.SetEquals(next))
        {
            return;
        }

        _pressedInputs.Clear();
        _pressedInputs.UnionWith(next);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        graphics.Clear(BackColor);

        _nodeBounds.Clear();
        var dpiScale = Math.Max(1f, DeviceDpi / 96f);
        var nodeWidth = Math.Clamp(
            (int)(ClientSize.Width * 0.255f),
            (int)(126 * dpiScale),
            (int)(152 * dpiScale));
        var rowHeight = Math.Clamp(
            (ClientSize.Height - (int)(30 * dpiScale) - 7 * (int)(5 * dpiScale)) / 8,
            (int)(28 * dpiScale),
            (int)(38 * dpiScale));
        var gap = Math.Max(
            (int)(4 * dpiScale),
            Math.Min((int)(8 * dpiScale), (ClientSize.Height - (int)(24 * dpiScale) - rowHeight * 8) / 7));
        var edge = (int)(10 * dpiScale);
        var top = Math.Max(edge, (ClientSize.Height - (rowHeight * 8 + gap * 7)) / 2);

        var left = BuildNodeColumn(LeftOutputs, edge, top, nodeWidth, rowHeight, gap);
        var right = BuildNodeColumn(
            RightOutputs,
            Math.Max(edge, ClientSize.Width - nodeWidth - edge),
            top,
            nodeWidth,
            rowHeight,
            gap);

        var controllerBounds = new RectangleF(
            left.Right + 14 * dpiScale,
            Math.Max(20 * dpiScale, ClientSize.Height * 0.18f),
            Math.Max(80 * dpiScale, right.Left - left.Right - 28 * dpiScale),
            Math.Max(90, ClientSize.Height * 0.64f));
        DrawConnections(graphics, controllerBounds);
        DrawController(graphics, controllerBounds);

        foreach (var pair in _nodeBounds)
        {
            DrawMappingNode(graphics, pair.Key, pair.Value);
        }

        if (Focused)
        {
            ControlPaint.DrawFocusRectangle(graphics, ClientRectangle);
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        Cursor = _nodeBounds.Values.Any(bounds => bounds.Contains(e.Location))
            ? Cursors.Hand
            : Cursors.Default;
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
        var hit = _nodeBounds.FirstOrDefault(pair => pair.Value.Contains(e.Location));
        if (hit.Key == XboxOutputButton.None)
        {
            return;
        }

        SelectOutput(hit.Key);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode is not (Keys.Left or Keys.Right or Keys.Up or Keys.Down))
        {
            return;
        }

        var outputs = ControllerButtonCatalog.Outputs
            .Where(output => output.Id != XboxOutputButton.None)
            .Select(output => output.Id)
            .ToArray();
        var currentIndex = Array.IndexOf(outputs, _selectedOutput);
        var delta = e.KeyCode is Keys.Left or Keys.Up ? -1 : 1;
        var nextIndex = (currentIndex + delta + outputs.Length) % outputs.Length;
        SelectOutput(outputs[nextIndex]);
        e.Handled = true;
    }

    private Rectangle BuildNodeColumn(
        IReadOnlyList<XboxOutputButton> outputs,
        int x,
        int top,
        int width,
        int height,
        int gap)
    {
        for (var index = 0; index < outputs.Count; index++)
        {
            _nodeBounds[outputs[index]] = new Rectangle(x, top + index * (height + gap), width, height);
        }

        return new Rectangle(x, top, width, outputs.Count * height + Math.Max(0, outputs.Count - 1) * gap);
    }

    private void DrawConnections(Graphics graphics, RectangleF controllerBounds)
    {
        using var pen = new Pen(Color.FromArgb(201, 211, 222), 1.4f);
        foreach (var pair in _nodeBounds)
        {
            var fromLeft = pair.Value.Left < ClientSize.Width / 2;
            var start = new PointF(
                fromLeft ? pair.Value.Right : pair.Value.Left,
                pair.Value.Top + pair.Value.Height / 2f);
            var end = new PointF(
                fromLeft ? controllerBounds.Left : controllerBounds.Right,
                Math.Clamp(start.Y, controllerBounds.Top + 12, controllerBounds.Bottom - 12));
            graphics.DrawLine(pen, start, end);
        }
    }

    private void DrawController(Graphics graphics, RectangleF bounds)
    {
        using var bodyPath = CreateControllerPath(bounds);
        using var shadow = new SolidBrush(Color.FromArgb(22, 15, 23, 34));
        using var body = new SolidBrush(Color.FromArgb(231, 236, 242));
        using var border = new Pen(Color.FromArgb(120, 137, 156), 1.6f);
        using var shadowPath = (GraphicsPath)bodyPath.Clone();
        using var shadowMatrix = new Matrix();
        shadowMatrix.Translate(0, 5);
        shadowPath.Transform(shadowMatrix);
        graphics.FillPath(shadow, shadowPath);
        graphics.FillPath(body, bodyPath);
        graphics.DrawPath(border, bodyPath);

        var scale = Math.Max(0.45f, Math.Min(bounds.Width / 300f, bounds.Height / 180f));
        var centerX = bounds.Left + bounds.Width / 2f;
        var centerY = bounds.Top + bounds.Height * 0.48f;
        DrawStick(graphics, centerX - 58 * scale, centerY - 16 * scale, 18 * scale);
        DrawStick(graphics, centerX + 45 * scale, centerY + 18 * scale, 18 * scale);
        DrawDpad(graphics, centerX - 62 * scale, centerY + 37 * scale, 24 * scale);
        DrawFaceButtons(graphics, centerX + 65 * scale, centerY - 22 * scale, scale);

        using var guide = new SolidBrush(Color.FromArgb(71, 85, 105));
        graphics.FillEllipse(guide, centerX - 9 * scale, centerY - 12 * scale, 18 * scale, 18 * scale);
    }

    private void DrawMappingNode(Graphics graphics, XboxOutputButton output, Rectangle bounds)
    {
        var inputs = _mapping.InputsFor(output);
        var selected = output == _selectedOutput;
        var active = inputs.Any(input => _pressedInputs.Contains(input));
        var duplicate = inputs.Count > 1;

        var fillColor = duplicate
            ? Color.FromArgb(254, 226, 226)
            : active
                ? Color.FromArgb(209, 250, 229)
                : selected
                    ? Color.FromArgb(219, 247, 249)
                    : Color.White;
        var borderColor = duplicate
            ? Color.FromArgb(185, 28, 28)
            : active
                ? Color.FromArgb(5, 150, 105)
                : selected
                    ? Color.FromArgb(8, 145, 151)
                    : Color.FromArgb(203, 213, 225);
        using var path = RoundedRectangle(bounds, 7);
        using var fill = new SolidBrush(fillColor);
        using var border = new Pen(borderColor, selected || active || duplicate ? 2f : 1f);
        graphics.FillPath(fill, path);
        graphics.DrawPath(border, path);

        var outputDescriptor = ControllerButtonCatalog.Output(output);
        var outputText = ShortOutputLabel(outputDescriptor.Id);
        var inputText = inputs.Count == 0
            ? UiLocalization.Current.Translate("Not assigned")
            : string.Join(
                ", ",
                inputs.Select(input => UiLocalization.Current.Translate(ControllerButtonCatalog.Input(input).DisplayName)));
        var dpiScale = Math.Max(1f, DeviceDpi / 96f);
        var padding = (int)(7 * dpiScale);
        var outputBounds = new Rectangle(
            bounds.Left + padding,
            bounds.Top + (int)(3 * dpiScale),
            Math.Max((int)(36 * dpiScale), (int)(bounds.Width * 0.36f)),
            bounds.Height - (int)(6 * dpiScale));
        var inputBounds = new Rectangle(
            outputBounds.Right + (int)(3 * dpiScale),
            bounds.Top + (int)(3 * dpiScale),
            Math.Max(1, bounds.Right - outputBounds.Right - padding - (int)(3 * dpiScale)),
            bounds.Height - (int)(6 * dpiScale));
        using var outputFont = new Font(Font, FontStyle.Bold);
        TextRenderer.DrawText(
            graphics,
            outputText,
            outputFont,
            outputBounds,
            Color.FromArgb(25, 39, 57),
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        TextRenderer.DrawText(
            graphics,
            inputText,
            Font,
            inputBounds,
            inputs.Count == 0 ? Color.FromArgb(148, 62, 62) : Color.FromArgb(71, 85, 105),
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    private void SelectOutput(XboxOutputButton output)
    {
        SelectedOutput = output;
        OutputSelected?.Invoke(output);
    }

    private static string ShortOutputLabel(XboxOutputButton output)
    {
        return output switch
        {
            XboxOutputButton.LeftShoulder => "LB",
            XboxOutputButton.RightShoulder => "RB",
            XboxOutputButton.LeftStick => "L3",
            XboxOutputButton.RightStick => "R3",
            XboxOutputButton.DpadUp => "Up",
            XboxOutputButton.DpadDown => "Down",
            XboxOutputButton.DpadLeft => "Left",
            XboxOutputButton.DpadRight => "Right",
            XboxOutputButton.Guide => "Guide",
            _ => ControllerButtonCatalog.Output(output).DisplayName
        };
    }

    private static GraphicsPath CreateControllerPath(RectangleF bounds)
    {
        var path = new GraphicsPath();
        var x = bounds.Left;
        var y = bounds.Top;
        var w = bounds.Width;
        var h = bounds.Height;
        path.AddBezier(x + w * 0.20f, y + h * 0.08f, x + w * 0.04f, y + h * 0.14f, x, y + h * 0.67f, x + w * 0.16f, y + h * 0.92f);
        path.AddBezier(x + w * 0.16f, y + h * 0.92f, x + w * 0.27f, y + h, x + w * 0.31f, y + h * 0.73f, x + w * 0.39f, y + h * 0.68f);
        path.AddBezier(x + w * 0.39f, y + h * 0.68f, x + w * 0.46f, y + h * 0.63f, x + w * 0.54f, y + h * 0.63f, x + w * 0.61f, y + h * 0.68f);
        path.AddBezier(x + w * 0.61f, y + h * 0.68f, x + w * 0.69f, y + h * 0.73f, x + w * 0.73f, y + h, x + w * 0.84f, y + h * 0.92f);
        path.AddBezier(x + w * 0.84f, y + h * 0.92f, x + w, y + h * 0.67f, x + w * 0.96f, y + h * 0.14f, x + w * 0.80f, y + h * 0.08f);
        path.AddBezier(x + w * 0.80f, y + h * 0.08f, x + w * 0.66f, y, x + w * 0.34f, y, x + w * 0.20f, y + h * 0.08f);
        path.CloseFigure();
        return path;
    }

    private static void DrawStick(Graphics graphics, float x, float y, float radius)
    {
        using var outer = new SolidBrush(Color.FromArgb(100, 116, 139));
        using var inner = new SolidBrush(Color.FromArgb(51, 65, 85));
        graphics.FillEllipse(outer, x - radius, y - radius, radius * 2, radius * 2);
        graphics.FillEllipse(inner, x - radius * 0.68f, y - radius * 0.68f, radius * 1.36f, radius * 1.36f);
    }

    private static void DrawDpad(Graphics graphics, float x, float y, float size)
    {
        using var brush = new SolidBrush(Color.FromArgb(71, 85, 105));
        graphics.FillRectangle(brush, x - size * 0.23f, y - size, size * 0.46f, size * 2);
        graphics.FillRectangle(brush, x - size, y - size * 0.23f, size * 2, size * 0.46f);
    }

    private static void DrawFaceButtons(Graphics graphics, float x, float y, float scale)
    {
        DrawFaceButton(graphics, x, y - 18 * scale, 7 * scale, Color.FromArgb(234, 179, 8));
        DrawFaceButton(graphics, x + 18 * scale, y, 7 * scale, Color.FromArgb(220, 38, 38));
        DrawFaceButton(graphics, x, y + 18 * scale, 7 * scale, Color.FromArgb(22, 163, 74));
        DrawFaceButton(graphics, x - 18 * scale, y, 7 * scale, Color.FromArgb(37, 99, 235));
    }

    private static void DrawFaceButton(Graphics graphics, float x, float y, float radius, Color color)
    {
        using var brush = new SolidBrush(color);
        graphics.FillEllipse(brush, x - radius, y - radius, radius * 2, radius * 2);
    }

    private static GraphicsPath RoundedRectangle(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        var diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}
