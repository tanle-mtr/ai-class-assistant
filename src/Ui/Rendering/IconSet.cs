using System.Drawing;
using System.Drawing.Drawing2D;

namespace AiClassAssistant.Ui.Rendering;

/// <summary>
/// 纯代码矢量图标集：全部用 GraphicsPath / GDI+ 图元绘制，不使用任何图片文件、不用 emoji。
/// 每个图标按给定矩形自适应缩放；坐标以 0~1 归一化。
/// </summary>
public static class IconSet
{
    private static PointF Pt(Rectangle r, float x, float y)
        => new(r.X + x * r.Width, r.Y + y * r.Height);

    private static Pen Line(Color c, float w = 1.6f)
        => new(c, w) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };

    /// <summary>云上传（云朵 + 向下箭头）。</summary>
    public static void CloudUpload(Graphics g, Rectangle r, Color color, Color? arrowColor = null)
    {
        float cx = r.X + r.Width / 2f;
        float cy = r.Y + r.Height * 0.36f;
        float s = Math.Min(r.Width, r.Height) * 0.30f;
        using var brush = new SolidBrush(color);
        g.FillEllipse(brush, cx - s * 1.15f, cy - s * 0.35f, s * 1.5f, s * 1.5f);
        g.FillEllipse(brush, cx - s * 0.45f, cy - s * 0.85f, s * 1.45f, s * 1.45f);
        g.FillEllipse(brush, cx + s * 0.15f, cy - s * 0.30f, s * 1.15f, s * 1.15f);
        g.FillRectangle(brush, cx - s * 1.1f, cy + s * 0.25f, s * 2.2f, s * 0.55f);

        Color ac = arrowColor ?? Color.White;
        using Pen pen = Line(ac, Math.Max(1.6f, s * 0.16f));
        float ax = cx;
        g.DrawLine(pen, ax, r.Y + r.Height * 0.42f, ax, r.Y + r.Height * 0.78f);
        g.DrawLine(pen, ax - s * 0.32f, r.Y + r.Height * 0.64f, ax, r.Y + r.Height * 0.80f);
        g.DrawLine(pen, ax + s * 0.32f, r.Y + r.Height * 0.64f, ax, r.Y + r.Height * 0.80f);
    }

    /// <summary>文档（折角 + 文本行）。</summary>
    public static void Document(Graphics g, Rectangle r, Color color, float w = 1.5f)
    {
        using Pen pen = Line(color, w);
        PointF a = Pt(r, 0.22f, 0.10f), b = Pt(r, 0.66f, 0.10f);
        PointF c = Pt(r, 0.82f, 0.28f), d = Pt(r, 0.82f, 0.90f);
        PointF e = Pt(r, 0.22f, 0.90f);
        using var path = new GraphicsPath();
        path.AddLine(a, b);
        path.AddLine(b, c);
        path.AddLine(c, d);
        path.AddLine(d, e);
        path.AddLine(e, a);
        g.DrawPath(pen, path);
        g.DrawLine(pen, b, Pt(r, 0.66f, 0.28f));
        g.DrawLine(pen, Pt(r, 0.66f, 0.28f), c);
        for (int i = 0; i < 3; i++)
        {
            float y = 0.46f + i * 0.14f;
            g.DrawLine(pen, Pt(r, 0.34f, y), Pt(r, 0.70f, y));
        }
    }

    /// <summary>文件夹。</summary>
    public static void Folder(Graphics g, Rectangle r, Color color, float w = 1.6f)
    {
        using Pen pen = Line(color, w);
        using var path = new GraphicsPath();
        path.AddLine(Pt(r, 0.10f, 0.30f), Pt(r, 0.40f, 0.30f));
        path.AddLine(Pt(r, 0.40f, 0.30f), Pt(r, 0.48f, 0.42f));
        path.AddLine(Pt(r, 0.48f, 0.42f), Pt(r, 0.90f, 0.42f));
        path.AddLine(Pt(r, 0.90f, 0.42f), Pt(r, 0.90f, 0.82f));
        path.AddLine(Pt(r, 0.90f, 0.82f), Pt(r, 0.10f, 0.82f));
        path.AddLine(Pt(r, 0.10f, 0.82f), Pt(r, 0.10f, 0.30f));
        g.DrawPath(pen, path);
    }

    /// <summary>柱状图。</summary>
    public static void BarChart(Graphics g, Rectangle r, Color color, float w = 1.6f)
    {
        using var brush = new SolidBrush(color);
        float[] xs = { 0.16f, 0.38f, 0.60f, 0.82f };
        float[] hs = { 0.34f, 0.58f, 0.44f, 0.72f };
        for (int i = 0; i < xs.Length; i++)
        {
            float bw = r.Width * 0.11f;
            float bh = r.Height * hs[i];
            float x = r.X + xs[i] * r.Width - bw / 2f;
            float y = r.Bottom - r.Height * 0.08f - bh;
            g.FillRectangle(brush, x, y, bw, bh);
        }
        using Pen pen = Line(color, w * 0.8f);
        g.DrawLine(pen, Pt(r, 0.08f, 0.92f), Pt(r, 0.92f, 0.92f));
    }

    /// <summary>齿轮。</summary>
    public static void Gear(Graphics g, Rectangle r, Color color, float w = 1.4f)
    {
        using Pen pen = Line(color, w);
        float cx = r.X + r.Width / 2f, cy = r.Y + r.Height / 2f;
        float ro = Math.Min(r.Width, r.Height) * 0.40f;
        float ri = ro * 0.62f;
        g.DrawEllipse(pen, cx - ro, cy - ro, ro * 2, ro * 2);
        g.DrawEllipse(pen, cx - ri * 0.4f, cy - ri * 0.4f, ri * 0.8f, ri * 0.8f);
        for (int i = 0; i < 8; i++)
        {
            double a = i * Math.PI / 4;
            float x1 = cx + (float)Math.Cos(a) * ro;
            float y1 = cy + (float)Math.Sin(a) * ro;
            float x2 = cx + (float)Math.Cos(a) * (ro + r.Width * 0.07f);
            float y2 = cy + (float)Math.Sin(a) * (ro + r.Height * 0.07f);
            g.DrawLine(pen, x1, y1, x2, y2);
        }
    }

    /// <summary>盾牌（含对勾）。</summary>
    public static void Shield(Graphics g, Rectangle r, Color color, float w = 1.6f)
    {
        using Pen pen = Line(color, w);
        using var path = new GraphicsPath();
        path.AddLine(Pt(r, 0.50f, 0.06f), Pt(r, 0.86f, 0.20f));
        path.AddLine(Pt(r, 0.86f, 0.20f), Pt(r, 0.80f, 0.62f));
        // GDI+ GraphicsPath 无 AddQuadCurve，用三次贝塞尔近似二次曲线
        path.AddBezier(Pt(r, 0.74f, 0.84f), Pt(r, 0.66f, 0.92f), Pt(r, 0.58f, 0.96f), Pt(r, 0.50f, 0.96f));
        path.AddBezier(Pt(r, 0.50f, 0.96f), Pt(r, 0.42f, 0.96f), Pt(r, 0.34f, 0.92f), Pt(r, 0.26f, 0.84f));
        path.AddBezier(Pt(r, 0.26f, 0.84f), Pt(r, 0.22f, 0.78f), Pt(r, 0.20f, 0.70f), Pt(r, 0.20f, 0.62f));
        path.AddLine(Pt(r, 0.20f, 0.62f), Pt(r, 0.14f, 0.20f));
        path.CloseFigure();
        g.DrawPath(pen, path);
        g.DrawLine(pen, Pt(r, 0.34f, 0.50f), Pt(r, 0.46f, 0.62f));
        g.DrawLine(pen, Pt(r, 0.46f, 0.62f), Pt(r, 0.68f, 0.38f));
    }

    /// <summary>时钟。</summary>
    public static void Clock(Graphics g, Rectangle r, Color color, float w = 1.6f)
    {
        using Pen pen = Line(color, w);
        g.DrawEllipse(pen, r.X + r.Width * 0.12f, r.Y + r.Height * 0.12f, r.Width * 0.76f, r.Height * 0.76f);
        g.DrawLine(pen, Pt(r, 0.50f, 0.50f), Pt(r, 0.50f, 0.28f));
        g.DrawLine(pen, Pt(r, 0.50f, 0.50f), Pt(r, 0.68f, 0.58f));
    }

    /// <summary>机器人（头 + 天线 + 眼 + 嘴）。</summary>
    public static void Robot(Graphics g, Rectangle r, Color color, float w = 1.5f)
    {
        using Pen pen = Line(color, w);
        using var brush = new SolidBrush(color);
        float ax = r.X + r.Width / 2f;
        g.DrawLine(pen, ax, r.Y + r.Height * 0.22f, ax, r.Y + r.Height * 0.08f);
        g.FillEllipse(brush, ax - r.Width * 0.05f, r.Y + r.Height * 0.02f, r.Width * 0.10f, r.Height * 0.10f);
        Painter.RoundRect(g, new Rectangle((int)(r.X + r.Width * 0.16f), (int)(r.Y + r.Height * 0.24f),
            (int)(r.Width * 0.68f), (int)(r.Height * 0.64f)), (int)(r.Height * 0.18f), Color.Transparent, color, w);
        g.FillEllipse(brush, r.X + r.Width * 0.36f, r.Y + r.Height * 0.44f, r.Width * 0.09f, r.Height * 0.12f);
        g.FillEllipse(brush, r.X + r.Width * 0.55f, r.Y + r.Height * 0.44f, r.Width * 0.09f, r.Height * 0.12f);
        g.DrawLine(pen, Pt(r, 0.38f, 0.72f), Pt(r, 0.62f, 0.72f));
    }

    /// <summary>五角星（filled=true 实心，否则空心）。</summary>
    public static void Star(Graphics g, Rectangle r, Color color, bool filled, float w = 1.4f)
    {
        using var path = new GraphicsPath();
        PointF[] pts = new PointF[10];
        for (int i = 0; i < 10; i++)
        {
            double a = -Math.PI / 2 + i * Math.PI / 5;
            float rad = i % 2 == 0 ? 0.48f : 0.21f;
            pts[i] = new PointF(
                r.X + r.Width / 2f + (float)Math.Cos(a) * r.Width * rad,
                r.Y + r.Height / 2f + (float)Math.Sin(a) * r.Height * rad);
        }
        path.AddPolygon(pts);
        if (filled)
        {
            using var brush = new SolidBrush(color);
            g.FillPath(brush, path);
        }
        else
        {
            using Pen pen = Line(color, w);
            g.DrawPath(pen, path);
        }
    }

    /// <summary>右箭头。</summary>
    public static void ArrowRight(Graphics g, Rectangle r, Color color, float w = 1.8f)
    {
        using Pen pen = Line(color, w);
        g.DrawLine(pen, Pt(r, 0.18f, 0.50f), Pt(r, 0.82f, 0.50f));
        g.DrawLine(pen, Pt(r, 0.62f, 0.28f), Pt(r, 0.84f, 0.50f));
        g.DrawLine(pen, Pt(r, 0.84f, 0.50f), Pt(r, 0.62f, 0.72f));
    }

    /// <summary>下箭头（下载/导入）。</summary>
    public static void ArrowDown(Graphics g, Rectangle r, Color color, float w = 1.8f)
    {
        using Pen pen = Line(color, w);
        g.DrawLine(pen, Pt(r, 0.50f, 0.16f), Pt(r, 0.50f, 0.78f));
        g.DrawLine(pen, Pt(r, 0.28f, 0.56f), Pt(r, 0.50f, 0.80f));
        g.DrawLine(pen, Pt(r, 0.50f, 0.80f), Pt(r, 0.72f, 0.56f));
    }

    /// <summary>上箭头（上传）。</summary>
    public static void ArrowUp(Graphics g, Rectangle r, Color color, float w = 1.8f)
    {
        using Pen pen = Line(color, w);
        g.DrawLine(pen, Pt(r, 0.50f, 0.84f), Pt(r, 0.50f, 0.22f));
        g.DrawLine(pen, Pt(r, 0.28f, 0.46f), Pt(r, 0.50f, 0.22f));
        g.DrawLine(pen, Pt(r, 0.50f, 0.22f), Pt(r, 0.72f, 0.46f));
    }

    /// <summary>对勾。</summary>
    public static void Check(Graphics g, Rectangle r, Color color, float w = 2f)
    {
        using Pen pen = Line(color, w);
        g.DrawLine(pen, Pt(r, 0.18f, 0.52f), Pt(r, 0.42f, 0.76f));
        g.DrawLine(pen, Pt(r, 0.42f, 0.76f), Pt(r, 0.84f, 0.24f));
    }

    /// <summary>加号。</summary>
    public static void Plus(Graphics g, Rectangle r, Color color, float w = 1.8f)
    {
        using Pen pen = Line(color, w);
        g.DrawLine(pen, Pt(r, 0.20f, 0.50f), Pt(r, 0.80f, 0.50f));
        g.DrawLine(pen, Pt(r, 0.50f, 0.20f), Pt(r, 0.50f, 0.80f));
    }

    /// <summary>书本（教科书）。</summary>
    public static void Book(Graphics g, Rectangle r, Color color, float w = 1.5f)
    {
        using Pen pen = Line(color, w);
        using var path = new GraphicsPath();
        path.AddLine(Pt(r, 0.50f, 0.28f), Pt(r, 0.14f, 0.20f));
        path.AddLine(Pt(r, 0.14f, 0.20f), Pt(r, 0.14f, 0.80f));
        path.AddLine(Pt(r, 0.14f, 0.80f), Pt(r, 0.50f, 0.88f));
        path.AddLine(Pt(r, 0.50f, 0.88f), Pt(r, 0.86f, 0.80f));
        path.AddLine(Pt(r, 0.86f, 0.80f), Pt(r, 0.86f, 0.20f));
        path.AddLine(Pt(r, 0.86f, 0.20f), Pt(r, 0.50f, 0.28f));
        path.AddLine(Pt(r, 0.50f, 0.28f), Pt(r, 0.50f, 0.88f));
        g.DrawPath(pen, path);
    }

    /// <summary>导出（下箭头 + 底托）。</summary>
    public static void Download(Graphics g, Rectangle r, Color color, float w = 1.7f)
    {
        using Pen pen = Line(color, w);
        g.DrawLine(pen, Pt(r, 0.50f, 0.14f), Pt(r, 0.50f, 0.62f));
        g.DrawLine(pen, Pt(r, 0.30f, 0.44f), Pt(r, 0.50f, 0.64f));
        g.DrawLine(pen, Pt(r, 0.50f, 0.64f), Pt(r, 0.70f, 0.44f));
        g.DrawLine(pen, Pt(r, 0.18f, 0.82f), Pt(r, 0.82f, 0.82f));
    }

    /// <summary>波形（音量柱）。</summary>
    public static void Waveform(Graphics g, Rectangle r, Color color, float w = 1.6f)
    {
        using Pen pen = Line(color, w);
        float[] hs = { 0.20f, 0.42f, 0.70f, 0.36f, 0.84f, 0.50f, 0.28f };
        for (int i = 0; i < hs.Length; i++)
        {
            float x = r.X + (0.14f + i * 0.12f) * r.Width;
            float h = hs[i] * r.Height * 0.8f;
            float y = r.Y + (r.Height - h) / 2f;
            g.DrawLine(pen, x, y, x, y + h);
        }
    }

    /// <summary>屏幕（监控）。</summary>
    public static void Screen(Graphics g, Rectangle r, Color color, float w = 1.6f)
    {
        using Pen pen = Line(color, w);
        Painter.RoundRect(g, new Rectangle((int)(r.X + r.Width * 0.10f), (int)(r.Y + r.Height * 0.16f),
            (int)(r.Width * 0.80f), (int)(r.Height * 0.56f)), 3, Color.Transparent, color, w);
        g.DrawLine(pen, Pt(r, 0.50f, 0.72f), Pt(r, 0.50f, 0.84f));
        g.DrawLine(pen, Pt(r, 0.32f, 0.86f), Pt(r, 0.68f, 0.86f));
    }

    /// <summary>关闭 ✕。</summary>
    public static void Close(Graphics g, Rectangle r, Color color, float w = 1.8f)
    {
        using Pen pen = Line(color, w);
        g.DrawLine(pen, Pt(r, 0.26f, 0.26f), Pt(r, 0.74f, 0.74f));
        g.DrawLine(pen, Pt(r, 0.74f, 0.26f), Pt(r, 0.26f, 0.74f));
    }

    /// <summary>最小化 —。</summary>
    public static void Minimize(Graphics g, Rectangle r, Color color, float w = 1.8f)
    {
        using Pen pen = Line(color, w);
        g.DrawLine(pen, Pt(r, 0.24f, 0.60f), Pt(r, 0.76f, 0.60f));
    }

    /// <summary>搜索放大镜。</summary>
    public static void Search(Graphics g, Rectangle r, Color color, float w = 1.6f)
    {
        using Pen pen = Line(color, w);
        g.DrawEllipse(pen, r.X + r.Width * 0.14f, r.Y + r.Height * 0.14f, r.Width * 0.52f, r.Height * 0.52f);
        g.DrawLine(pen, Pt(r, 0.60f, 0.60f), Pt(r, 0.86f, 0.86f));
    }

    /// <summary>开关符号（轨道 + 滑块）。</summary>
    public static void Switch(Graphics g, Rectangle r, Color color, bool on, float w = 1.4f)
    {
        Rectangle track = new(r.X, r.Y + r.Height / 4, r.Width, r.Height / 2);
        Painter.RoundRect(g, track, track.Height / 2, on ? color : Color.FromArgb(0xCB, 0xD5, 0xE1));
        int d = r.Height / 2;
        int x = on ? r.Right - d : r.X;
        using var brush = new SolidBrush(Color.White);
        g.FillEllipse(brush, x, r.Y + r.Height / 4, d, d);
        _ = w;
    }
}
