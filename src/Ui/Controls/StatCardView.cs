using System.Drawing;
using AiClassAssistant.Ui.Rendering;
using Theme = AiClassAssistant.Ui.Theming.Theme;

namespace AiClassAssistant.Ui.Controls;

/// <summary>统计卡图标类型。</summary>
public enum StatIcon
{
    Course,
    Overtime,
    Decibel,
    Export,
    Monitor,
    Folder
}

/// <summary>统计卡：左侧大数字 + 小标题，右侧浅青圆角图标块（效果图 02 顶部四卡）。</summary>
public sealed class StatCardView : Canvas
{
    private string _number = "";
    private string _caption = "";

    public string Number
    {
        get => _number;
        set
        {
            _number = value ?? "";
            Invalidate();
        }
    }

    public string Caption
    {
        get => _caption;
        set
        {
            _caption = value ?? "";
            Invalidate();
        }
    }

    public StatIcon IconKind { get; set; } = StatIcon.Course;

    /// <summary>图标块是否置灰（如拖堂次数为 0）。</summary>
    public bool IconMuted { get; set; }

    protected override void Draw(Graphics g, Rectangle rect)
    {
        ClearHits();
        if (rect.Width < 20 || rect.Height < 20)
        {
            return;
        }

        Painter.Shadow(g, rect, Theme.Radius, 3, 2);
        Painter.RoundRect(g, rect, Theme.Radius, Theme.Card, Theme.Border);

        int iconSize = Math.Min(46, rect.Height - 28);
        Rectangle iconBox = new(rect.Right - iconSize - 18, rect.Y + (rect.Height - iconSize) / 2, iconSize, iconSize);
        Color boxBg = IconMuted ? Color.FromArgb(0xF1, 0xF5, 0xF9) : Theme.Soft;
        Color iconColor = IconMuted ? Color.FromArgb(0x94, 0xA3, 0xB8) : Theme.Primary;
        Painter.RoundRect(g, iconBox, 12, boxBg);

        Rectangle ir = Rectangle.Inflate(iconBox, -iconSize / 4, -iconSize / 4);
        switch (IconKind)
        {
            case StatIcon.Overtime: IconSet.Shield(g, ir, iconColor); break;
            case StatIcon.Decibel: IconSet.Waveform(g, ir, iconColor); break;
            case StatIcon.Export: IconSet.Download(g, ir, iconColor); break;
            case StatIcon.Monitor: IconSet.Screen(g, ir, iconColor); break;
            case StatIcon.Folder: IconSet.Folder(g, ir, iconColor); break;
            default: IconSet.Document(g, ir, iconColor); break;
        }

        RectangleF textRect = new(rect.X + 18, rect.Y + 10, Math.Max(10, iconBox.X - rect.X - 26), rect.Height - 20);
        Painter.Text(g, _number, new RectangleF(textRect.X, textRect.Y, textRect.Width, textRect.Height * 0.58f),
            Theme.NumberFont, Theme.Text, ContentAlignment.BottomLeft);
        Painter.Text(g, _caption, new RectangleF(textRect.X, textRect.Y + textRect.Height * 0.56f, textRect.Width, textRect.Height * 0.36f),
            Theme.SubFont, Theme.Text2, ContentAlignment.TopLeft);
    }
}
