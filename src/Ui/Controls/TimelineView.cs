using System.Drawing;
using AiClassAssistant.Core.Models;
using AiClassAssistant.Ui.Rendering;
using Theme = AiClassAssistant.Ui.Theming.Theme;

namespace AiClassAssistant.Ui.Controls;

/// <summary>今日课程时间轴：竖线 + 圆点 + 时间 + 科目 + 当前课浅青高亮气泡（效果图 02）。</summary>
public sealed class TimelineView : Canvas
{
    private IReadOnlyList<LessonInfo> _lessons = Array.Empty<LessonInfo>();
    private int _currentIndex = -1;
    private string _bubble = "正在上课 · 距下课 12 分钟";

    public IReadOnlyList<LessonInfo> Lessons
    {
        get => _lessons;
        set
        {
            _lessons = value ?? Array.Empty<LessonInfo>();
            Invalidate();
        }
    }

    /// <summary>当前课索引；-1 表示无。</summary>
    public int CurrentIndex
    {
        get => _currentIndex;
        set
        {
            _currentIndex = value;
            Invalidate();
        }
    }

    /// <summary>当前课高亮气泡文字。</summary>
    public string Bubble
    {
        get => _bubble;
        set
        {
            _bubble = value ?? "";
            Invalidate();
        }
    }

    public void SetData(IReadOnlyList<LessonInfo> lessons, int currentIndex, string bubble)
    {
        _lessons = lessons ?? Array.Empty<LessonInfo>();
        _currentIndex = currentIndex;
        _bubble = bubble ?? "";
        Invalidate();
    }

    protected override void Draw(Graphics g, Rectangle rect)
    {
        ClearHits();
        if (_lessons.Count == 0)
        {
            Painter.Text(g, "暂无课程安排", rect, Theme.SubFont, Theme.Text2, ContentAlignment.MiddleCenter);
            return;
        }

        int lineX = rect.X + 78;
        int rowH = Math.Min(46, Math.Max(32, rect.Height / Math.Max(1, _lessons.Count)));
        int firstY = rect.Y + rowH / 2;

        using (var axis = new Pen(Theme.Border, 1.5f))
        {
            g.DrawLine(axis, lineX, firstY - rowH / 3f, lineX, firstY + rowH * (_lessons.Count - 1));
        }

        for (int i = 0; i < _lessons.Count; i++)
        {
            LessonInfo lesson = _lessons[i];
            int cy = firstY + i * rowH;
            bool active = i == _currentIndex;

            Painter.Text(g, lesson.Start, new RectangleF(rect.X + 4, cy - 11, 64, 22),
                Theme.SubFont, active ? Theme.Primary : Theme.Text2, ContentAlignment.MiddleRight);

            if (active)
            {
                using var halo = new SolidBrush(Color.FromArgb(40, Theme.Accent));
                g.FillEllipse(halo, lineX - 8, cy - 8, 16, 16);
            }

            Painter.Dot(g, lineX, cy, active ? 5f : 3.5f, active ? Theme.Primary : Theme.Muted);

            if (active && !string.IsNullOrEmpty(_bubble))
            {
                Rectangle bubble = new(lineX + 16, cy - 15, Math.Min(240, rect.Right - lineX - 24), 30);
                Painter.Shadow(g, bubble, 8, 2, 1);
                Painter.RoundRect(g, bubble, 8, Theme.Soft, Theme.SoftBorder);
                Painter.Text(g, $"{lesson.Subject}  ·  {_bubble}", bubble, Theme.SubFont, Theme.Primary, ContentAlignment.MiddleCenter);
            }
            else
            {
                Painter.Text(g, lesson.Subject, new RectangleF(lineX + 16, cy - 12, 120, 24),
                    Theme.SubFont, Theme.Text2, ContentAlignment.MiddleLeft);
            }
        }
    }
}
