using System.Drawing;
using AiClassAssistant.Ui.Rendering;
using Theme = AiClassAssistant.Ui.Theming.Theme;

namespace AiClassAssistant.Ui.Controls;

/// <summary>五星评级：已评金色实心 / 未评灰色空心（效果图 04 老师总结）。</summary>
public sealed class StarRating : Canvas
{
    private int _stars;

    public int Stars
    {
        get => _stars;
        set
        {
            _stars = Math.Clamp(value, 0, Count);
            Invalidate();
        }
    }

    /// <summary>星数量（默认 5）。</summary>
    public int Count { get; set; } = 5;

    protected override void Draw(Graphics g, Rectangle rect)
    {
        ClearHits();
        int n = Math.Max(1, Count);
        float gap = 4f;
        float size = Math.Min(rect.Height, (rect.Width - gap * (n - 1)) / n);
        if (size <= 2)
        {
            return;
        }

        float total = size * n + gap * (n - 1);
        float x = rect.X + (rect.Width - total) / 2f;
        int sy = rect.Y + (int)Math.Max(0f, (rect.Height - size) / 2f);
        for (int i = 0; i < n; i++)
        {
            Rectangle sr = new((int)x, sy, (int)size, (int)size);
            IconSet.Star(g, sr, i < _stars ? Theme.Star : Theme.StarEmpty, i < _stars);
            x += size + gap;
        }
    }
}
