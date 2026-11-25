using System.Drawing;
using AiClassAssistant.Ui.Rendering;
using Theme = AiClassAssistant.Ui.Theming.Theme;

namespace AiClassAssistant.Ui.Controls;

/// <summary>胶囊标签：选中态实心主色 / 未选中描边（效果图 03 科目选择）。</summary>
public sealed class Chip : Canvas
{
    private string _caption = "";
    private bool _selected;

    /// <summary>标签文字（避开 Control.Text）。</summary>
    public string Caption
    {
        get => _caption;
        set
        {
            _caption = value ?? "";
            Invalidate();
        }
    }

    /// <summary>是否选中。</summary>
    public bool Selected
    {
        get => _selected;
        set
        {
            _selected = value;
            Invalidate();
        }
    }

    /// <summary>点击回调（避开 Control.OnClick 方法）。</summary>
    public Action? ClickAction { get; set; }

    protected override void Draw(Graphics g, Rectangle rect)
    {
        ClearHits();
        Rectangle r = Rectangle.Inflate(rect, -1, -1);
        if (r.Width < 8 || r.Height < 8)
        {
            return;
        }

        bool hover = IsHovered(r);
        AddHit(r, () =>
        {
            _selected = !_selected;
            ClickAction?.Invoke();
            Invalidate();
        });

        if (_selected)
        {
            Painter.Pill(g, r, _caption, Theme.SubFont,
                hover ? Theme.PrimaryLight : Theme.Primary, Color.White);
        }
        else
        {
            Painter.OutlinePill(g, r, _caption, Theme.SubFont,
                hover ? Theme.Accent : Theme.Border, Theme.Text2);
        }
    }
}
