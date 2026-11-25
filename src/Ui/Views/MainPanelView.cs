using System.Drawing;
using System.Windows.Forms;
using AiClassAssistant.Core.Models;
using AiClassAssistant.Ui.Controls;
using AiClassAssistant.Ui.Rendering;
using AiClassAssistant.Ui.Shell;
using AiClassAssistant.Ui.Theming;

namespace AiClassAssistant.Ui.Views;

/// <summary>
/// 效果图 01：桌面托盘主面板（1080x648 无边框）。
/// 数据全部通过委托注入，不直接依赖 App / Core 具体实现。
/// </summary>
public sealed class MainPanelView : ViewBase
{
    private readonly RingProgress _ring;
    private readonly DbWaveform _wave;
    private readonly ToggleSwitch _toggle;
    private int _tick;
    private LessonInfo? _lesson;
    private LessonState _state;
    private IReadOnlyList<LessonInfo> _schedule = Array.Empty<LessonInfo>();
    private bool _overtime;
    private bool _tunnel;

    public MainPanelView() : base("主面板")
    {
        Dock = DockStyle.None;
        Size = Theme.PanelSize;
        BackColor = Theme.Bg;

        _ring = new RingProgress { Location = new Point(61, 370), Size = new Size(170, 170) };
        _wave = new DbWaveform
        {
            Location = new Point(310, 175),
            Size = new Size(440, 268),
            Samples = DbWaveform.DemoSamples()
        };
        _toggle = new ToggleSwitch { Location = new Point(1002, 420), Size = new Size(46, 24) };
        _toggle.Toggled += on => MonitoringToggled?.Invoke(on);
        Controls.Add(_ring);
        Controls.Add(_wave);
        Controls.Add(_toggle);

        StartPolling(1000, OnTick);
        LoadData();
    }

    // ---------- 数据注入点 ----------
    public Func<LessonInfo?> CurrentLesson { get; set; } = () => null;
    public Func<LessonState> State { get; set; } = () => LessonState.None;
    public Func<double> RemainingMinutes { get; set; } = () => 12;
    public Func<string> ModelName { get; set; } = () => "未选择模型";
    public Func<double[]> DbSamples { get; set; } = () => DbWaveform.DemoSamples();
    public Func<IReadOnlyList<LessonInfo>> TodaySchedule { get; set; } = () => Array.Empty<LessonInfo>();
    public Func<bool> MonitoringEnabled { get; set; } = () => true;
    public Func<bool> OvertimeDetected { get; set; } = () => false;
    public Func<bool> TunnelConnected { get; set; } = () => false;
    public Func<string> StorageLabel { get; set; } = () => $"存储: 导出 {DateTime.Now:yyyy-MM-dd}";

    public Action<bool>? MonitoringToggled { get; set; }
    public Action? GenerateSummaryRequested { get; set; }
    public Action? ExportRequested { get; set; }
    public Action? CloseRequested { get; set; }

    public override void OnShow()
    {
        base.OnShow();
        LoadData();
    }

    protected override Task LoadAsync(CancellationToken ct)
    {
        LoadData();
        return Task.CompletedTask;
    }

    private void OnTick()
    {
        // 分贝 1s 刷新；其余状态 5s 刷新。
        _wave.Samples = DbSamples();
        _tick++;
        if (_tick % 5 == 0)
        {
            LoadData();
        }
    }

    private void LoadData()
    {
        try
        {
            _lesson = CurrentLesson();
            _state = State();
            _schedule = TodaySchedule() ?? Array.Empty<LessonInfo>();
            _overtime = OvertimeDetected();
            _tunnel = TunnelConnected();
            _toggle.On = MonitoringEnabled();

            _wave.Samples = DbSamples();
            double left = RemainingMinutes();
            bool active = _lesson is not null && _state == LessonState.OnClass;
            _ring.Update(active ? Math.Clamp(1.0 - left / 45.0, 0.05, 1.0) : 0.0,
                active ? Math.Max(0, Math.Round(left)).ToString() : "—",
                active ? "分钟" : "空闲");
            Invalidate();
        }
        catch
        {
            // 后端不可用时空态占位。
            Invalidate();
        }
    }

    protected override void Draw(Graphics g, Rectangle rect)
    {
        ClearHits();

        // 顶栏
        using (var brush = new SolidBrush(Theme.HeaderBg))
        {
            g.FillRectangle(brush, new Rectangle(0, 0, rect.Width, Theme.HeaderHeight));
        }

        Rectangle logo = new(16, 12, 190, 32);
        Painter.RoundRect(g, logo, 16, Color.White);
        IconSet.Robot(g, new Rectangle(logo.X + 7, logo.Y + 5, 22, 22), Theme.Primary);
        using (var font = Theme.Font(14f, FontStyle.Bold))
        {
            Painter.Text(g, "AI 课堂助手", new RectangleF(logo.X + 32, logo.Y, logo.Width - 36, logo.Height),
                font, Theme.Primary, ContentAlignment.MiddleLeft);
        }

        Painter.Dot(g, rect.Right - 104, Theme.HeaderHeight / 2f, 5f, Theme.Green);
        Painter.Text(g, "运行中", new RectangleF(rect.Right - 92, 0, 60, Theme.HeaderHeight),
            Theme.SubFont, Color.FromArgb(0xD8, 0xF3, 0xEE), ContentAlignment.MiddleLeft);

        Rectangle closeBtn = new(rect.Right - 44, 12, 32, 32);
        bool closeHover = IsHovered(closeBtn);
        if (closeHover)
        {
            Painter.RoundRect(g, closeBtn, 6, Color.FromArgb(30, 255, 255, 255));
        }
        IconSet.Close(g, new Rectangle(closeBtn.X + 8, closeBtn.Y + 8, 16, 16), Color.White);
        AddHit(closeBtn, () => CloseRequested?.Invoke(), () => Invalidate());

        DrawLeftCard(g);
        DrawCenterCard(g);
        DrawRightCards(g);

        // 底部状态条
        using (var statusBrush = new SolidBrush(Theme.Card))
        {
            g.FillRectangle(statusBrush, new Rectangle(0, rect.Height - Theme.StatusHeight, rect.Width, Theme.StatusHeight));
        }
        int sy = rect.Height - Theme.StatusHeight;
        Painter.Dot(g, 26, sy + 22, 4f, Theme.Green);
        Painter.Text(g, "监控", new RectangleF(36, sy, 44, Theme.StatusHeight), Theme.SubFont, Theme.Text2, ContentAlignment.MiddleLeft);
        Painter.Dot(g, 86, sy + 22, 4f, Theme.Accent);
        Painter.Text(g, "开启", new RectangleF(96, sy, 44, Theme.StatusHeight), Theme.SubFont, Theme.Text2, ContentAlignment.MiddleLeft);
        Painter.Text(g, StorageLabel(), new RectangleF(300, sy, 480, Theme.StatusHeight), Theme.SubFont, Theme.Text, ContentAlignment.MiddleCenter);
        Painter.Dot(g, rect.Right - 226, sy + 22, 4f, _tunnel ? Theme.Green : Theme.Muted);
        Painter.Text(g, "隧道", new RectangleF(rect.Right - 216, sy, 44, Theme.StatusHeight), Theme.SubFont, Theme.Text2, ContentAlignment.MiddleLeft);
        Painter.Dot(g, rect.Right - 156, sy + 22, 4f, Theme.Green);
        Painter.Text(g, _tunnel ? "已连接" : "未连接", new RectangleF(rect.Right - 146, sy, 80, Theme.StatusHeight),
            Theme.SubFont, Theme.Text2, ContentAlignment.MiddleLeft);
    }

    private void DrawLeftCard(Graphics g)
    {
        Rectangle card = new(16, 70, 260, 534);
        Painter.Shadow(g, card, Theme.Radius, 3, 2);
        Painter.RoundRect(g, card, Theme.Radius, Theme.Card, Theme.Border);

        Painter.Pill(g, new Rectangle(32, 88, 76, 26), "当前课程", Theme.SubFont, Theme.Soft, Theme.Primary);

        string subject = string.IsNullOrEmpty(_lesson?.Subject) ? "暂无课程" : _lesson.Subject;
        Painter.Text(g, subject, new RectangleF(16, 126, 260, 68), Theme.SubjectFont, Theme.Text, ContentAlignment.MiddleCenter);

        string meta = _lesson is null || string.IsNullOrEmpty(_lesson.ClassName)
            ? "暂无课程安排"
            : $"{_lesson.ClassName}  ·  {_lesson.Start}-{_lesson.End}";
        Painter.Text(g, meta, new RectangleF(16, 200, 260, 26), Theme.SubFont, Theme.Text2, ContentAlignment.MiddleCenter);

        bool active = _lesson is not null && _state == LessonState.OnClass;
        string hint = active
            ? $"距下课 {Math.Max(0, Math.Round(RemainingMinutes()))} 分钟"
            : (_state == LessonState.AfterSchool ? "拖堂监测中" : "当前未在上课");
        Painter.Text(g, hint, new RectangleF(16, 246, 260, 26), Theme.BodyFont, Theme.Primary, ContentAlignment.MiddleCenter);
    }

    private void DrawCenterCard(Graphics g)
    {
        Rectangle card = new(290, 70, 480, 534);
        Painter.Shadow(g, card, Theme.Radius, 3, 2);
        Painter.RoundRect(g, card, Theme.Radius, Theme.Card, Theme.Border);

        Painter.Text(g, "课堂记录中", new RectangleF(310, 82, 200, 28), Theme.SectionFont, Theme.Text, ContentAlignment.MiddleLeft);

        // 模型选择行
        Rectangle modelRow = new(310, 118, 440, 42);
        Painter.RoundRect(g, modelRow, Theme.RadiusSm, Color.FromArgb(0xF8, 0xFA, 0xFC), Theme.Border);
        Painter.Text(g, "模型：", new RectangleF(modelRow.X + 14, modelRow.Y, 48, modelRow.Height),
            Theme.BodyFont, Theme.Text2, ContentAlignment.MiddleLeft);
        Painter.Text(g, ModelName(), new RectangleF(modelRow.X + 62, modelRow.Y, modelRow.Width - 90, modelRow.Height),
            Theme.BodyFont, Theme.Text, ContentAlignment.MiddleLeft);
        IconSet.ArrowRight(g, new Rectangle(modelRow.Right - 30, modelRow.Y + 13, 18, 16), Theme.Text2);

        // 按钮
        Rectangle genBtn = new(310, 500, 214, 50);
        Rectangle expBtn = new(538, 500, 212, 50);
        bool gh = IsHovered(genBtn), eh = IsHovered(expBtn);
        Painter.GradientRoundRect(g, genBtn, Theme.Radius,
            gh ? Theme.PrimaryLight : Theme.Primary, gh ? Theme.Accent : Theme.PrimaryLight, 90f);
        Painter.Text(g, "生成总结", genBtn, Theme.Font(15f, FontStyle.Bold), Color.White, ContentAlignment.MiddleCenter);
        Painter.RoundRect(g, expBtn, Theme.Radius, eh ? Theme.Soft : Theme.Card, Theme.Primary, 1.5f);
        Painter.Text(g, "导出课堂", expBtn, Theme.Font(15f), Theme.Primary, ContentAlignment.MiddleCenter);
        AddHit(genBtn, () => GenerateSummaryRequested?.Invoke(), () => Invalidate());
        AddHit(expBtn, () => ExportRequested?.Invoke(), () => Invalidate());
    }

    private void DrawRightCards(Graphics g)
    {
        // 今日课表
        Rectangle schedule = new(784, 70, 280, 170);
        Painter.Shadow(g, schedule, Theme.Radius, 3, 2);
        Painter.RoundRect(g, schedule, Theme.Radius, Theme.Card, Theme.Border);
        Painter.Text(g, "今日课表", new RectangleF(schedule.X + 16, schedule.Y + 10, 200, 30),
            Theme.SectionFont, Theme.Text, ContentAlignment.MiddleLeft);

        if (_schedule.Count == 0)
        {
            Painter.Text(g, "暂无课程安排", new RectangleF(schedule.X + 16, schedule.Y + 54, 248, 90),
                Theme.SubFont, Theme.Text2, ContentAlignment.MiddleCenter);
        }
        else
        {
            int shown = Math.Min(4, _schedule.Count);
            for (int i = 0; i < shown; i++)
            {
                LessonInfo l = _schedule[i];
                Rectangle row = new(schedule.X + 12, schedule.Y + 46 + i * 30, 256, 28);
                bool active = ReferenceEquals(l, _lesson) || (l.Subject == _lesson?.Subject && l.Start == _lesson?.Start);
                if (active)
                {
                    Painter.RoundRect(g, row, 6, Theme.Soft);
                }
                Painter.Text(g, l.Subject, new RectangleF(row.X + 8, row.Y, 130, row.Height),
                    Theme.SubFont, active ? Theme.Primary : Theme.Text2, ContentAlignment.MiddleLeft);
                Painter.Text(g, l.Start, new RectangleF(row.Right - 80, row.Y, 72, row.Height),
                    Theme.SubFont, active ? Theme.Primary : Theme.Text2, ContentAlignment.MiddleRight);
            }
        }

        // 拖堂监测
        Rectangle overtime = new(784, 252, 280, 96);
        Painter.Shadow(g, overtime, Theme.Radius, 3, 2);
        Painter.RoundRect(g, overtime, Theme.Radius, Theme.Card, Theme.Border);
        Painter.Text(g, "拖堂监测", new RectangleF(overtime.X + 16, overtime.Y + 8, 200, 28),
            Theme.SectionFont, Theme.Text, ContentAlignment.MiddleLeft);
        Rectangle badge = new(overtime.X + 16, overtime.Y + 44, 20, 20);
        Painter.Dot(g, badge.X + 10, badge.Y + 10, 10f, _overtime ? Theme.Amber : Theme.Green);
        IconSet.Check(g, new Rectangle(badge.X + 4, badge.Y + 4, 12, 12), Color.White, 1.8f);
        Painter.Text(g, _overtime ? "检测到拖堂 · 请关注" : "监测中 · 未拖堂",
            new RectangleF(badge.Right + 8, overtime.Y + 42, 210, 24), Theme.SubFont, Theme.Text2, ContentAlignment.MiddleLeft);

        // 快捷操作
        Rectangle quick = new(784, 360, 280, 244);
        Painter.Shadow(g, quick, Theme.Radius, 3, 2);
        Painter.RoundRect(g, quick, Theme.Radius, Theme.Card, Theme.Border);
        Painter.Text(g, "快捷操作", new RectangleF(quick.X + 16, quick.Y + 10, 200, 30),
            Theme.SectionFont, Theme.Text, ContentAlignment.MiddleLeft);
        Painter.Text(g, "开启监控", new RectangleF(quick.X + 18, quick.Y + 60, 150, 30),
            Theme.BodyFont, Theme.Text2, ContentAlignment.MiddleLeft);
        Painter.Dot(g, quick.X + 20, quick.Y + 112, 5f, _tunnel ? Theme.Green : Theme.Muted);
        Painter.Text(g, _tunnel ? "隧道已连接" : "隧道未连接",
            new RectangleF(quick.X + 32, quick.Y + 98, 200, 28), Theme.SubFont, Theme.Text2, ContentAlignment.MiddleLeft);
    }
}
