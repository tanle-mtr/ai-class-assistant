using System.Drawing;
using AiClassAssistant.Ui.Rendering;
using Theme = AiClassAssistant.Ui.Theming.Theme;

namespace AiClassAssistant.Ui.Controls;

/// <summary>自绘开关：ON 青绿 / OFF 灰，圆角轨道 + 白色滑块（效果图 01 快捷操作）。</summary>
public sealed class ToggleSwitch : Canvas
{
    private bool _on;

    public bool On
    {
        get => _on;
        set
        {
            _on = value;
            Invalidate();
        }
    }

    /// <summary>开关状态变更回调。</summary>
    public Action<bool>? Toggled { get; set; }

    protected override void Draw(Graphics g, Rectangle rect)
    {
        ClearHits();
        int th = Math.Max(16, Math.Min(24, rect.Height));
        int tw = th * 2;
        Rectangle track = new(rect.X + (rect.Width - tw) / 2, rect.Y + (rect.Height - th) / 2, tw, th);
        if (track.Width < 10)
        {
            return;
        }

        bool hover = IsHovered(track);
        AddHit(track, () =>
        {
            _on = !_on;
            Toggled?.Invoke(_on);
            Invalidate();
        });

        Color fill = _on ? (hover ? Theme.PrimaryLight : Theme.Primary) : Theme.Muted;
        Painter.RoundRect(g, track, th / 2, fill);

        int d = th - 4;
        int knobX = _on ? track.Right - d - 2 : track.X + 2;
        Rectangle knob = new(knobX, track.Y + 2, d, d);
        using var brush = new SolidBrush(Color.White);
        g.FillEllipse(brush, knob);

        if (_on)
        {
            IconSet.Check(g, new Rectangle(knob.X + 3, knob.Y + 3, d - 6, d - 6), Theme.Primary, 1.4f);
        }
    }
}
