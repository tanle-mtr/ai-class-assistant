using System.Drawing;
using AiClassAssistant.Ui.Rendering;
using AiClassAssistant.Ui.Theming;

namespace AiClassAssistant.Ui.Controls;

/// <summary>青绿渐变进度条 + 百分比文字（效果图 03 上传列表）。</summary>
public sealed class ProgressBar : Canvas
{
    private double _value;

    /// <summary>进度百分比 0~100。</summary>
    public double Value
    {
        get => _value;
        set
        {
            _value = Math.Clamp(value, 0.0, 100.0);
            Invalidate();
        }
    }

    /// <summary>是否绘制百分比文字。</summary>
    public bool ShowPercent { get; set; } = true;

    /// <summary>是否视为已完成。</summary>
    public bool Completed => _value >= 99.5;

    protected override void Draw(Graphics g, Rectangle rect)
    {
        ClearHits();
        int h = Math.Max(7, Math.Min(12, rect.Height / 3));
        Rectangle track = new(rect.X, rect.Y + (rect.Height - h) / 2, rect.Width, h);
        Painter.RoundRect(g, track, h / 2, Theme.Border);

        int w = (int)(track.Width * _value / 100.0);
        if (w > 0)
        {
            Painter.GradientRoundRect(g, new Rectangle(track.X, track.Y, w, track.Height), h / 2,
                Theme.Primary, Theme.Accent, 0f);
        }

        if (ShowPercent)
        {
            Painter.Text(g, $"{(int)Math.Round(_value)}%",
                new RectangleF(rect.Right - 46, rect.Y, 44, rect.Height),
                Theme.SubFont, Theme.Text2, ContentAlignment.MiddleRight);
        }
    }
}
