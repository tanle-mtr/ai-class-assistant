using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using Theme = AiClassAssistant.Ui.Theming.Theme;

namespace AiClassAssistant.Ui.Rendering;

/// <summary>
/// GDI+ 绘制工具：圆角矩形、渐变、阴影、文本、胶囊，统一开启抗锯齿。
/// 所有方法只依赖 System.Drawing，零 HTML / 零浏览器控件。
/// </summary>
public static class Painter
{
    /// <summary>统一开启高质量抗锯齿与 ClearType 文本渲染。</summary>
    public static void EnableAntialias(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.CompositingMode = CompositingMode.SourceOver;
        g.CompositingQuality = CompositingQuality.HighQuality;
    }

    /// <summary>生成圆角矩形路径（radius 自动收敛到短边一半，支持胶囊）。</summary>
    public static GraphicsPath RoundedRectPath(Rectangle r, int radius)
    {
        var path = new GraphicsPath();
        if (r.Width <= 0 || r.Height <= 0)
        {
            return path;
        }

        int rad = Math.Max(0, Math.Min(radius, Math.Min(r.Width, r.Height) / 2));
        int d = rad * 2;
        if (d <= 0)
        {
            path.AddRectangle(r);
            return path;
        }

        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    /// <summary>圆角矩形填充 + 可选描边。</summary>
    public static void RoundRect(Graphics g, Rectangle r, int radius, Color fill, Color? border = null, float borderWidth = 1f)
    {
        if (r.Width <= 0 || r.Height <= 0)
        {
            return;
        }

        using GraphicsPath path = RoundedRectPath(r, radius);
        using var brush = new SolidBrush(fill);
        g.FillPath(brush, path);
        if (border.HasValue && border.Value.A > 0)
        {
            using var pen = new Pen(border.Value, borderWidth);
            g.DrawPath(pen, path);
        }
    }

    /// <summary>青绿渐变圆角矩形（品牌感），可指定渐变角度与描边。</summary>
    public static void GradientRoundRect(Graphics g, Rectangle r, int radius, Color from, Color to, float angle = 90f, Color? border = null)
    {
        if (r.Width <= 0 || r.Height <= 0)
        {
            return;
        }

        using GraphicsPath path = RoundedRectPath(r, radius);
        using var brush = new LinearGradientBrush(r, from, to, angle);
        g.FillPath(brush, path);
        if (border.HasValue && border.Value.A > 0)
        {
            using var pen = new Pen(border.Value, 1f);
            g.DrawPath(pen, path);
        }
    }

    /// <summary>品牌主色渐变圆角矩形（深青→青绿）。</summary>
    public static void PrimaryRoundRect(Graphics g, Rectangle r, int radius = 12)
        => GradientRoundRect(g, r, radius, Theme.Primary, Theme.PrimaryLight, 90f);

    /// <summary>轻阴影：向下偏移的半透明圆角矩形，多次叠加模拟弥散。</summary>
    public static void Shadow(Graphics g, Rectangle r, int radius, int depth = 4, int spread = 3, Color? color = null)
    {
        if (r.Width <= 0 || r.Height <= 0)
        {
            return;
        }

        Color c = color ?? Theme.Shadow;
        for (int i = depth; i >= 1; i--)
        {
            int a = Math.Max(2, c.A - (depth - i) * 2);
            using var brush = new SolidBrush(Color.FromArgb(a, c.R, c.G, c.B));
            Rectangle sr = new(r.X - spread, r.Y - spread + i, r.Width + spread * 2, r.Height + spread * 2);
            using GraphicsPath path = RoundedRectPath(sr, radius + spread);
            g.FillPath(brush, path);
        }
    }

    /// <summary>文本绘制（支持 ContentAlignment 九宫格对齐 + 省略号）。</summary>
    public static void Text(Graphics g, string? text, RectangleF rect, Font font, Color color,
        ContentAlignment align = ContentAlignment.MiddleLeft, bool ellipsis = true)
    {
        if (string.IsNullOrEmpty(text) || rect.Width <= 0 || rect.Height <= 0)
        {
            return;
        }

        using var brush = new SolidBrush(color);
        using var sf = new StringFormat
        {
            Alignment = align switch
            {
                ContentAlignment.TopCenter or ContentAlignment.MiddleCenter or ContentAlignment.BottomCenter => StringAlignment.Center,
                ContentAlignment.TopRight or ContentAlignment.MiddleRight or ContentAlignment.BottomRight => StringAlignment.Far,
                _ => StringAlignment.Near
            },
            LineAlignment = align switch
            {
                ContentAlignment.TopLeft or ContentAlignment.TopCenter or ContentAlignment.TopRight => StringAlignment.Near,
                ContentAlignment.BottomLeft or ContentAlignment.BottomCenter or ContentAlignment.BottomRight => StringAlignment.Far,
                _ => StringAlignment.Center
            },
            Trimming = ellipsis ? StringTrimming.EllipsisCharacter : StringTrimming.None,
            FormatFlags = StringFormatFlags.NoWrap
        };
        g.DrawString(text, font, brush, rect, sf);
    }

    /// <summary>在指定点绘制文本（左上角锚点）。</summary>
    public static void TextAt(Graphics g, string? text, float x, float y, Font font, Color color)
        => Text(g, text, new RectangleF(x, y, 4096, 4096), font, color, ContentAlignment.TopLeft, false);

    /// <summary>胶囊标签：实心填充 + 文字（可选描边）。</summary>
    public static void Pill(Graphics g, Rectangle r, string text, Font font, Color fill, Color textColor, Color? border = null,
        ContentAlignment align = ContentAlignment.MiddleCenter)
    {
        RoundRect(g, r, Math.Min(r.Height / 2, Theme.RadiusPill), fill, border);
        Text(g, text, r, font, textColor, align);
    }

    /// <summary>描边胶囊（未选中 chip）。</summary>
    public static void OutlinePill(Graphics g, Rectangle r, string text, Font font, Color border, Color textColor, Color? fill = null)
        => Pill(g, r, text, font, fill ?? Theme.Card, textColor, border);

    /// <summary>实心圆点（状态灯）。</summary>
    public static void Dot(Graphics g, float cx, float cy, float radius, Color color)
    {
        using var brush = new SolidBrush(color);
        g.FillEllipse(brush, cx - radius, cy - radius, radius * 2, radius * 2);
    }

    /// <summary>虚线圆角矩形（拖拽框/建议框）。</summary>
    public static void DashedRoundRect(Graphics g, Rectangle r, int radius, Color border, float width = 1.2f, float dash = 6f, float space = 4f)
    {
        if (r.Width <= 0 || r.Height <= 0)
        {
            return;
        }

        using GraphicsPath path = RoundedRectPath(r, radius);
        using var pen = new Pen(border, width) { DashStyle = DashStyle.Custom };
        pen.DashPattern = new float[] { dash, space };
        g.DrawPath(pen, path);
    }

    /// <summary>两色线性混合（t=0 取 a，t=1 取 b）。</summary>
    public static Color Blend(Color a, Color b, double t)
    {
        t = Math.Clamp(t, 0.0, 1.0);
        int Lerp(int x, int y) => (int)Math.Round(x + (y - x) * t);
        return Color.FromArgb(Lerp(a.A, b.A), Lerp(a.R, b.R), Lerp(a.G, b.G), Lerp(a.B, b.B));
    }

    /// <summary>测量文本宽度（像素）。</summary>
    public static float MeasureText(Graphics g, string? text, Font font)
        => string.IsNullOrEmpty(text) ? 0f : g.MeasureString(text, font).Width;
}
