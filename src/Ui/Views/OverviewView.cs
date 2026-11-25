using System.Drawing;
using AiClassAssistant.Core.Models;
using AiClassAssistant.Ui.Controls;
using AiClassAssistant.Ui.Rendering;
using AiClassAssistant.Ui.Shell;
using AiClassAssistant.Ui.Theming;

namespace AiClassAssistant.Ui.Views;

/// <summary>总览中一条课堂总结记录。</summary>
public sealed class SummaryEntry
{
    public string Title { get; set; } = "";
    public string Meta { get; set; } = "";
    public string Path { get; set; } = "";
    public bool Generated { get; set; } = true;
}

/// <summary>
/// 效果图 02：数据中心总览 —— 四张统计卡 + 今日课程时间线 + 课堂总结列表 + 导出文件夹区。
/// </summary>
public sealed class OverviewView : ViewBase
{
    private readonly StatCardView[] _stats = new StatCardView[4];
    private readonly TimelineView _timeline = new();
    private Size _lastSize;

    public OverviewView() : base("数据中心")
    {
        _stats[0] = new StatCardView { IconKind = StatIcon.Course };
        _stats[1] = new StatCardView { IconKind = StatIcon.Overtime };
        _stats[2] = new StatCardView { IconKind = StatIcon.Decibel };
        _stats[3] = new StatCardView { IconKind = StatIcon.Export };
        foreach (StatCardView card in _stats)
        {
            Controls.Add(card);
        }

        Controls.Add(_timeline);
    }

    // ---------- 数据注入点 ----------
    public Func<IReadOnlyList<LessonInfo>> ScheduleProvider { get; set; } = () => Array.Empty<LessonInfo>();
    public Func<int> CourseCountProvider { get; set; } = () => 0;
    public Func<int> OvertimeCountProvider { get; set; } = () => 0;
    public Func<double> AverageDbProvider { get; set; } = () => 0;
    public Func<int> ExportCountProvider { get; set; } = () => 0;
    public Func<IReadOnlyList<SummaryEntry>> SummariesProvider { get; set; } = () => Array.Empty<SummaryEntry>();
    public Func<IReadOnlyList<string>> FoldersProvider { get; set; } = () => Array.Empty<string>();
    public Func<int> CurrentLessonIndexProvider { get; set; } = () => -1;
    public Func<string> BubbleProvider { get; set; } = () => "正在上课 · 距下课 12 分钟";

    public Action<string>? OpenSummaryRequested { get; set; }
    public Action<string>? OpenFolderRequested { get; set; }

    protected override Task LoadAsync(CancellationToken ct)
    {
        try
        {
            _stats[0].Number = CourseCountProvider().ToString();
            _stats[0].Caption = "今日课程";
            int overtime = OvertimeCountProvider();
            _stats[1].Number = overtime.ToString();
            _stats[1].Caption = "拖堂次数";
            _stats[1].IconMuted = overtime == 0;
            _stats[2].Number = $"{AverageDbProvider():0} dB";
            _stats[2].Caption = "平均分贝";
            _stats[3].Number = ExportCountProvider().ToString();
            _stats[3].Caption = "已导出文件";

            IReadOnlyList<LessonInfo> schedule = ScheduleProvider() ?? Array.Empty<LessonInfo>();
            _timeline.SetData(schedule, Math.Clamp(CurrentLessonIndexProvider(), -1, schedule.Count - 1), BubbleProvider());
        }
        catch
        {
            // 保持空态。
        }

        return Task.CompletedTask;
    }

    protected override void Draw(Graphics g, Rectangle rect)
    {
        ClearHits();
        LayoutControls(rect);

        // 四张统计卡
        IReadOnlyList<SummaryEntry> summaries;
        IReadOnlyList<string> folders;
        try
        {
            summaries = SummariesProvider() ?? Array.Empty<SummaryEntry>();
            folders = FoldersProvider() ?? Array.Empty<string>();
        }
        catch
        {
            summaries = Array.Empty<SummaryEntry>();
            folders = Array.Empty<string>();
        }

        int row2 = _stats[0].Bottom + 14;
        int row3 = row2 + 246 + 14;

        // 课堂总结卡
        Rectangle summaryCard = new(_timeline.Right + 14, row2, rect.Right - 20 - _timeline.Right - 14, 246);
        Painter.Shadow(g, summaryCard, Theme.Radius, 3, 2);
        Painter.RoundRect(g, summaryCard, Theme.Radius, Theme.Card, Theme.Border);
        Painter.Text(g, "课堂总结", new RectangleF(summaryCard.X + 18, summaryCard.Y + 10, 200, 30),
            Theme.SectionFont, Theme.Text, ContentAlignment.MiddleLeft);

        if (summaries.Count == 0)
        {
            Painter.Text(g, "暂无课堂总结", new RectangleF(summaryCard.X + 18, summaryCard.Y + 56, summaryCard.Width - 36, 160),
                Theme.SubFont, Theme.Text2, ContentAlignment.MiddleCenter);
        }
        else
        {
            int shown = Math.Min(3, summaries.Count);
            for (int i = 0; i < shown; i++)
            {
                SummaryEntry entry = summaries[i];
                Rectangle row = new(summaryCard.X + 14, summaryCard.Y + 52 + i * 58, summaryCard.Width - 28, 50);
                Painter.RoundRect(g, row, 10, Color.FromArgb(0xF8, 0xFA, 0xFC), Theme.Border);
                Rectangle iconBox = new(row.X + 10, row.Y + 9, 32, 32);
                Painter.RoundRect(g, iconBox, 9, Theme.Soft);
                IconSet.Document(g, Rectangle.Inflate(iconBox, -7, -7), Theme.Primary);
                Painter.Text(g, entry.Title, new RectangleF(row.X + 52, row.Y + 6, row.Width - 150, 24),
                    Theme.BodyFont, Theme.Text, ContentAlignment.BottomLeft);
                Painter.Text(g, entry.Meta, new RectangleF(row.X + 52, row.Y + 26, row.Width - 150, 20),
                    Theme.TinyFont, Theme.Text2, ContentAlignment.TopLeft);

                Rectangle btn = new(row.Right - 76, row.Y + 12, 62, 26);
                bool hover = IsHovered(btn);
                Painter.Pill(g, btn, "查看", Theme.TinyFont,
                    entry.Generated ? (hover ? Theme.PrimaryLight : Theme.Primary) : Theme.Muted,
                    Color.White);
                string path = entry.Path;
                AddHit(btn, () => OpenSummaryRequested?.Invoke(path), () => Invalidate());
            }
        }

        // 导出文件夹区
        Rectangle folderCard = new(20, row3, rect.Width - 40, Math.Max(120, rect.Bottom - row3 - 20));
        Painter.Shadow(g, folderCard, Theme.Radius, 3, 2);
        Painter.RoundRect(g, folderCard, Theme.Radius, Theme.Card, Theme.Border);
        Painter.Text(g, "导出文件（OpenList）", new RectangleF(folderCard.X + 18, folderCard.Y + 10, 260, 30),
            Theme.SectionFont, Theme.Text, ContentAlignment.MiddleLeft);

        if (folders.Count == 0)
        {
            Painter.Text(g, "暂无导出文件", new RectangleF(folderCard.X + 18, folderCard.Y + 56, folderCard.Width - 36, 50),
                Theme.SubFont, Theme.Text2, ContentAlignment.MiddleCenter);
        }
        else
        {
            int n = Math.Min(4, folders.Count);
            int slot = (folderCard.Width - 40) / n;
            for (int i = 0; i < n; i++)
            {
                int cx = folderCard.X + 20 + slot * i + slot / 2;
                Rectangle icon = new(cx - 26, folderCard.Y + 50, 52, 44);
                bool hover = IsHovered(icon);
                IconSet.Folder(g, icon, hover ? Theme.Primary : Color.FromArgb(0x7A, 0x8B, 0x9C));
                string name = Path.GetFileName(folders[i].TrimEnd('/', '\\'));
                Painter.Text(g, string.IsNullOrEmpty(name) ? folders[i] : name,
                    new RectangleF(cx - slot / 2f + 4, icon.Bottom + 4, slot - 8, 22),
                    Theme.TinyFont, Theme.Text2, ContentAlignment.MiddleCenter);
                string path = folders[i];
                AddHit(icon, () => OpenFolderRequested?.Invoke(path), () => Invalidate());
            }
        }
    }

    private void LayoutControls(Rectangle rect)
    {
        if (_lastSize == rect.Size)
        {
            return;
        }

        _lastSize = rect.Size;
        int gap = 14;
        int cardW = (rect.Width - 40 - gap * 3) / 4;
        for (int i = 0; i < 4; i++)
        {
            _stats[i].Bounds = new Rectangle(20 + i * (cardW + gap), 20, cardW, 92);
        }

        int leftW = (rect.Width - 54) / 2;
        _timeline.Bounds = new Rectangle(20, 126, leftW, 246);
    }
}
