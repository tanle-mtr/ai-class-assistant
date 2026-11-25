using System.Drawing;
using AiClassAssistant.Ui.Controls;
using AiClassAssistant.Ui.Rendering;
using AiClassAssistant.Ui.Shell;
using AiClassAssistant.Ui.Theming;

namespace AiClassAssistant.Ui.Views;

/// <summary>监控视图：实时分贝面积图/波形、会话状态、拖堂监测、监控开关。</summary>
public sealed class MonitorView : ViewBase
{
    private readonly AreaChart _chart = new();
    private readonly ToggleSwitch _toggle = new();
    private bool _active;
    private bool _overtime;
    private double _currentDb;

    public MonitorView() : base("实时监控")
    {
        Controls.Add(_chart);
        Controls.Add(_toggle);
        _toggle.Toggled += on => MonitoringToggled?.Invoke(on);
        StartPolling(1000, Tick);
    }

    // ---------- 数据注入点 ----------
    public Func<double[]> DbProvider { get; set; } = () => DbWaveform.DemoSamples(50);
    public Func<bool> ActiveProvider { get; set; } = () => false;
    public Func<bool> OvertimeProvider { get; set; } = () => false;
    public Func<double> CurrentDbProvider { get; set; } = () => 0;
    public Func<bool> MonitoringProvider { get; set; } = () => true;
    public Action<bool>? MonitoringToggled { get; set; }

    protected override Task LoadAsync(CancellationToken ct)
    {
        RefreshData();
        return Task.CompletedTask;
    }

    private void RefreshData()
    {
        try
        {
            double[] data = DbProvider();
            _chart.SetData(data, data.Length > 0 ? data.Average() : 0, data.Length > 0 ? data.Max() : 0);
            _active = ActiveProvider();
            _overtime = OvertimeProvider();
            _currentDb = CurrentDbProvider();
            _toggle.On = MonitoringProvider();
            Invalidate();
        }
        catch
        {
            Invalidate();
        }
    }

    private void Tick() => RefreshData();

    protected override void Draw(Graphics g, Rectangle rect)
    {
        ClearHits();
        Painter.Text(g, "实时监控", new RectangleF(20, 16, 200, 32), Theme.TitleFont, Theme.Text, ContentAlignment.MiddleLeft);

        // 状态卡
        Rectangle state = new(20, 62, rect.Width - 40, 74);
        Painter.RoundRect(g, state, Theme.Radius, Theme.Card, Theme.Border);
        Painter.Dot(g, state.X + 26, state.Y + 28, 6f, _active ? Theme.Green : Theme.Muted);
        Painter.Text(g, _active ? "会话进行中" : "当前无会话",
            new RectangleF(state.X + 40, state.Y + 12, 180, 28), Theme.SectionFont, Theme.Text, ContentAlignment.MiddleLeft);
        Painter.Text(g, $"实时分贝  {_currentDb:0.0} dB",
            new RectangleF(state.X + 240, state.Y + 12, 200, 28), Theme.SubFont, Theme.Text2, ContentAlignment.MiddleLeft);
        Painter.Dot(g, state.X + 470, state.Y + 28, 6f, _overtime ? Theme.Amber : Theme.Green);
        Painter.Text(g, _overtime ? "检测到拖堂" : "未检测到拖堂",
            new RectangleF(state.X + 484, state.Y + 12, 220, 28), Theme.SubFont, Theme.Text2, ContentAlignment.MiddleLeft);

        Painter.Text(g, "开启监控", new RectangleF(state.X + 22, state.Y + 44, 100, 24),
            Theme.SubFont, Theme.Text2, ContentAlignment.MiddleLeft);

        // 图卡
        int chartY = 152;
        Rectangle chartCard = new(20, chartY, rect.Width - 40, Math.Max(140, rect.Bottom - chartY - 20));
        Painter.RoundRect(g, chartCard, Theme.Radius, Theme.Card, Theme.Border);
        Painter.Text(g, "课堂分贝趋势", new RectangleF(chartCard.X + 18, chartCard.Y + 8, 240, 28),
            Theme.SectionFont, Theme.Text, ContentAlignment.MiddleLeft);
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        Rectangle r = ClientRectangle;
        _toggle.Bounds = new Rectangle(r.Right - 92, 100, 52, 26);
        int chartY = 152;
        Rectangle chartCard = new(20, chartY, r.Width - 40, Math.Max(140, r.Bottom - chartY - 20));
        _chart.Bounds = new Rectangle(chartCard.X + 8, chartCard.Y + 36, chartCard.Width - 16, chartCard.Height - 44);
    }
}
