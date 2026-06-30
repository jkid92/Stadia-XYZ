using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace StadiaX.ControlCenter;

internal static class BrandLogo
{
    private static readonly Color Navy = Color.FromArgb(16, 38, 59);
    private static readonly Color NavyDeep = Color.FromArgb(10, 28, 45);
    private static readonly Color Teal = Color.FromArgb(88, 218, 210);
    private static readonly Color Orange = Color.FromArgb(255, 123, 38);
    private static readonly Color Graphite = Color.FromArgb(26, 32, 40);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);

    public static Bitmap CreateBitmap(int size)
    {
        var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.CompositingQuality = CompositingQuality.HighQuality;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.ScaleTransform(size / 256f, size / 256f);

        using (var tile = RoundedRect(new RectangleF(10, 10, 236, 236), 32))
        using (var fill = new LinearGradientBrush(new RectangleF(10, 10, 236, 236), Navy, NavyDeep, LinearGradientMode.ForwardDiagonal))
        using (var border = new Pen(Teal, 5))
        {
            g.FillPath(fill, tile);
            g.DrawPath(border, tile);
        }

        DrawSignal(g);
        DrawController(g);
        DrawX(g);
        return bitmap;
    }

    public static Icon CreateIcon(int size = 64)
    {
        using var bitmap = CreateBitmap(size);
        var handle = bitmap.GetHicon();
        try
        {
            return (Icon)Icon.FromHandle(handle).Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    private static void DrawSignal(Graphics g)
    {
        using var white = new Pen(Color.White, 8) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        using var teal = new Pen(Teal, 7) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        using var orange = new Pen(Orange, 7) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawArc(white, 72, 34, 112, 76, 205, 130);
        g.DrawArc(teal, 92, 56, 72, 50, 210, 120);
        g.DrawArc(orange, 108, 78, 40, 28, 215, 110);
        using var dot = new SolidBrush(Color.White);
        g.FillEllipse(dot, 122, 98, 12, 12);
    }

    private static void DrawController(Graphics g)
    {
        using var shadow = new SolidBrush(Color.FromArgb(55, 0, 0, 0));
        using var body = ControllerBody();
        using var shadowPath = ControllerBody(0, 4);
        g.FillPath(shadow, shadowPath);

        using var fill = new SolidBrush(Color.White);
        using var rim = new Pen(Color.FromArgb(218, 226, 235), 2);
        g.FillPath(fill, body);
        g.DrawPath(rim, body);

        DrawDpad(g, 62, 119);
        DrawStick(g, 102, 162);
        DrawStick(g, 154, 162);
        DrawFaceButtons(g);
        DrawSystemButtons(g);
    }

    private static GraphicsPath ControllerBody(float dx = 0, float dy = 0)
    {
        var p = new GraphicsPath();
        p.StartFigure();
        p.AddBezier(P(44, 104), P(58, 84), P(96, 86), P(121, 94));
        p.AddBezier(P(121, 94), P(130, 96), P(138, 96), P(147, 94));
        p.AddBezier(P(147, 94), P(172, 86), P(210, 84), P(224, 104));
        p.AddBezier(P(224, 104), P(238, 126), P(241, 190), P(222, 207));
        p.AddBezier(P(222, 207), P(207, 221), P(189, 199), P(177, 176));
        p.AddLine(P(177, 176), P(79, 176));
        p.AddBezier(P(79, 176), P(67, 199), P(49, 221), P(34, 207));
        p.AddBezier(P(34, 207), P(15, 190), P(18, 126), P(44, 104));
        p.CloseFigure();
        return p;

        PointF P(float x, float y) => new(x + dx, y + dy);
    }

    private static void DrawDpad(Graphics g, float x, float y)
    {
        using var brush = new SolidBrush(Graphite);
        using var vertical = RoundedRect(new RectangleF(x - 8, y - 24, 16, 48), 7);
        using var horizontal = RoundedRect(new RectangleF(x - 24, y - 8, 48, 16), 7);
        g.FillPath(brush, vertical);
        g.FillPath(brush, horizontal);
    }

    private static void DrawStick(Graphics g, float x, float y)
    {
        using var rim = new SolidBrush(Color.FromArgb(226, 230, 236));
        using var accent = new Pen(Orange, 3);
        using var dark = new SolidBrush(Graphite);
        g.FillEllipse(rim, x - 21, y - 21, 42, 42);
        g.DrawArc(accent, x - 18, y - 18, 36, 36, 22, 136);
        g.FillEllipse(dark, x - 16, y - 16, 32, 32);
    }

    private static void DrawFaceButtons(Graphics g)
    {
        using var dark = new SolidBrush(Graphite);
        g.FillEllipse(dark, 194, 108, 16, 16);
        g.FillEllipse(dark, 213, 126, 16, 16);
        g.FillEllipse(dark, 176, 126, 16, 16);
        g.FillEllipse(dark, 195, 146, 16, 16);
    }

    private static void DrawSystemButtons(Graphics g)
    {
        using var dark = new SolidBrush(Graphite);
        using var light = new SolidBrush(Color.FromArgb(224, 232, 240));
        using var left = RoundedRect(new RectangleF(102, 103, 24, 12), 6);
        using var right = RoundedRect(new RectangleF(144, 103, 24, 12), 6);
        g.FillPath(dark, left);
        g.FillPath(dark, right);
        g.FillEllipse(dark, 124, 124, 18, 18);
        g.FillEllipse(light, 111, 107, 3, 3);
        g.FillEllipse(light, 116, 107, 3, 3);
        g.FillEllipse(light, 121, 107, 3, 3);
        using var pen = new Pen(light, 2) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLine(pen, 151, 107, 162, 107);
        g.DrawLine(pen, 151, 111, 162, 111);
    }

    private static void DrawX(Graphics g)
    {
        using var orange = new Pen(Orange, 16) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        using var teal = new Pen(Teal, 16) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLine(orange, 181, 180, 222, 221);
        g.DrawLine(teal, 222, 180, 181, 221);
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
}
