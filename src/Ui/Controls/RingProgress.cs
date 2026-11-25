using System.Drawing;
using System.Drawing.Drawing2D;
using AiClassAssistant.Ui.Rendering;
using Theme = AiClassAssistant.Ui.Theming.Theme;

namespace AiClassAssistant.Ui.Controls;

/// <summary>GDI+ 环形倒计时进度条：浅灰底环 + 青绿渐变弧 + 中心文字。</summary>
public sealed class RingProgress : Canvas
{
    private double _progress;

    /// <summary>进度 0~1。</summary>
    public double Progress
    {
        get => _progress;
        set
        {
            _progress = Math.Clamp(value, 0.0, 1.0);
            Invalidate();
        }
    }

    /// <summary>环中心大文字（如剩余分钟数）；为 null 时不绘制。</summary>
    public string? CenterText { get; set; }

    /// <summary>环中心小字（如“分钟”）；为 null 时不绘制。</summary>
    public string? SubText { get; set; }

    /// <summary>环线宽。</summary>
    public float Thickness { get; set; } = 12f;

    /// <summary>一次性设置进度与中心文字。</summary>
    public void Update(double progress, string? center, string? sub)
    {
        _progress = Math.Clamp(progress, 0.0, 1.0);
        CenterText = center;
        SubText = sub;
        Invalidate();
    }

    protected override void Draw(Graphics g, Rectangle rect)
    {
        ClearHits();
        int d = Math.Min(rect.Width, rect.Height) - 6;
        if (d <= 20)
        {
            return;
        }

        Rectangle box = new(rect.X + (rect.Width - d) / 2, rect.Y + (rect.Height - d) / 2, d, d);
        float th = Math.Max(4f, Thickness);
        RectangleF ring = new(box.X + th / 2f, box.Y + th / 2f, box.Width - th, box.Height - th);

        using (var basePen = new Pen(Theme.Border, th))
        {
            g.DrawEllipse(basePen, ring);
        }

        if (_progress > 0.001)
        {
            using var brush = new LinearGradientBrush(box, Theme.AccentLight, Theme.Primary, 90f);
            using var pen = new Pen(brush, th)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };
            float sweep = (float)(360.0 * _progress);
            g.DrawArc(pen, ring, -90f, sweep);
        }

        if (!string.IsNullOrEmpty(CenterText))
        {
            RectangleF tr = new(box.X, box.Y + box.Height * 0.30f, box.Width, box.Height * 0.40f);
            Painter.Text(g, CenterText, tr, Theme.NumberFont, Theme.Text, ContentAlignment.MiddleCenter);
            if (!string.IsNullOrEmpty(SubText))
            {
                RectangleF sr = new(box.X, box.Y + box.Height * 0.56f, box.Width, box.Height * 0.18f);
                Painter.Text(g, SubText, sr, Theme.SubFont, Theme.Text2, ContentAlignment.MiddleCenter);
            }
        }
    }
}
