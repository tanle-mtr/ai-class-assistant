using System.Drawing;
using AiClassAssistant.Ui.Controls;
using AiClassAssistant.Ui.Rendering;
using AiClassAssistant.Ui.Shell;
using AiClassAssistant.Ui.Theming;

namespace AiClassAssistant.Ui.Views;

/// <summary>设置视图：模型选择/创建、感知开关、隧道 token、保留天数（全部自绘控件）。</summary>
public sealed class SettingsView : ViewBase
{
    private readonly ToggleSwitch _monitor = new();
    private readonly ToggleSwitch _always = new();
    private readonly ToggleSwitch _tunnel = new();
    private readonly InputField _token = new() { Placeholder = "粘贴 Cloudflare Tunnel Token" };
    private readonly InputField _retention = new() { Placeholder = "天数" };
    private IReadOnlyList<string> _models = Array.Empty<string>();
    private string _current = "";

    public SettingsView() : base("设置")
    {
        Controls.Add(_monitor);
        Controls.Add(_always);
        Controls.Add(_tunnel);
        Controls.Add(_token);
        Controls.Add(_retention);

        _monitor.Toggled += on => MonitoringChanged?.Invoke(on);
        _always.Toggled += on => AlwaysRecordChanged?.Invoke(on);
        _tunnel.Toggled += on => TunnelEnabledChanged?.Invoke(on);
    }

    // ---------- 数据注入点 ----------
    public Func<Task<List<string>>> ModelsProvider { get; set; } = () => Task.FromResult(new List<string>());
    public Func<string> CurrentModelProvider { get; set; } = () => "";
    public Action<string>? ModelSelected { get; set; }
    public Func<string, Task<bool>>? CreateModelRequested { get; set; }

    public Func<bool> MonitoringProvider { get; set; } = () => true;
    public Func<bool> AlwaysRecordProvider { get; set; } = () => false;
    public Func<bool> TunnelEnabledProvider { get; set; } = () => false;
    public Action<bool>? MonitoringChanged { get; set; }
    public Action<bool>? AlwaysRecordChanged { get; set; }
    public Action<bool>? TunnelEnabledChanged { get; set; }

    public Func<string> TokenProvider { get; set; } = () => "";
    public Action<string>? TokenSaved { get; set; }
    public Func<int> RetentionProvider { get; set; } = () => 14;
    public Action<int>? RetentionSaved { get; set; }

    protected override async Task LoadAsync(CancellationToken ct)
    {
        try
        {
            List<string> models = await ModelsProvider();
            _models = models ?? new List<string>();
            _current = CurrentModelProvider();
        }
        catch
        {
            _models = Array.Empty<string>();
        }

        try
        {
            _monitor.On = MonitoringProvider();
            _always.On = AlwaysRecordProvider();
            _tunnel.On = TunnelEnabledProvider();
            _token.Value = TokenProvider();
            _retention.Value = RetentionProvider().ToString();
        }
        catch
        {
            // 保持默认。
        }
    }

    protected override void Draw(Graphics g, Rectangle rect)
    {
        ClearHits();
        Painter.Text(g, "设置", new RectangleF(20, 16, 160, 32), Theme.TitleFont, Theme.Text, ContentAlignment.MiddleLeft);

        // 模型区
        Rectangle modelCard = new(20, 60, rect.Width - 40, 132);
        Section(g, modelCard, "AI 模型", "选择主力模型，或基于技能模板创建专属模型");
        int mx = modelCard.X + 18;
        int my = modelCard.Y + 62;
        if (_models.Count == 0)
        {
            Painter.Text(g, "未检测到可用模型，请先安装 Ollama 模型",
                new RectangleF(mx, my, modelCard.Width - 220, 40), Theme.SubFont, Theme.Text2, ContentAlignment.MiddleLeft);
        }
        else
        {
            foreach (string m in _models)
            {
                int w = (int)Math.Clamp(Painter.MeasureText(g, m, Theme.SubFont) + 24, 76, 180);
                if (mx + w > modelCard.Right - 200)
                {
                    break;
                }

                Rectangle pill = new(mx, my, w, 32);
                bool sel = m == _current;
                if (sel)
                {
                    Painter.Pill(g, pill, m, Theme.TinyFont, Theme.Primary, Color.White);
                }
                else
                {
                    Painter.OutlinePill(g, pill, m, Theme.TinyFont, Theme.Border, Theme.Text2);
                }

                string captured = m;
                AddHit(pill, () =>
                {
                    _current = captured;
                    ModelSelected?.Invoke(captured);
                    Invalidate();
                }, () => Invalidate());
                mx += w + 10;
            }
        }

        Rectangle refresh = new(modelCard.Right - 184, modelCard.Y + 14, 76, 30);
        Rectangle create = new(modelCard.Right - 98, modelCard.Y + 14, 78, 30);
        SmallButton(g, refresh, "刷新", false);
        SmallButton(g, create, "新建模型", true);
        AddHit(refresh, () => Reload(), () => Invalidate());
        AddHit(create, () =>
        {
            string? name = InputDialog.Ask("新建专属模型", "如 qwen3-class:latest");
            if (!string.IsNullOrEmpty(name))
            {
                _ = CreateModelRequested?.Invoke(name);
            }
        }, () => Invalidate());

        // 感知区
        Rectangle sense = new(20, 206, rect.Width - 40, 116);
        Section(g, sense, "感知与隐私", "共享采集麦克风/摄像头，不独占设备");
        Painter.Text(g, "开启监控", new RectangleF(sense.X + 20, sense.Y + 58, 160, 28), Theme.BodyFont, Theme.Text2, ContentAlignment.MiddleLeft);
        Painter.Text(g, "无人时仍录制", new RectangleF(sense.X + 300, sense.Y + 58, 160, 28), Theme.BodyFont, Theme.Text2, ContentAlignment.MiddleLeft);

        // 隧道区
        Rectangle tunnel = new(20, 336, rect.Width - 40, 132);
        Section(g, tunnel, "隧道与网络", "Cloudflare Tunnel Token 由面板复制粘贴");
        Painter.Text(g, "Token", new RectangleF(tunnel.X + 20, tunnel.Y + 58, 60, 28), Theme.BodyFont, Theme.Text2, ContentAlignment.MiddleLeft);
        Rectangle saveToken = new(tunnel.Right - 100, tunnel.Y + 58, 80, 32);
        SmallButton(g, saveToken, "保存", true);
        AddHit(saveToken, () => TokenSaved?.Invoke(_token.Value), () => Invalidate());

        // 数据保留区
        Rectangle data = new(20, 482, rect.Width - 40, 100);
        Section(g, data, "数据保留", "监控录像按天数自动清理，md 文档永久保留");
        Painter.Text(g, "保留天数", new RectangleF(data.X + 20, data.Y + 56, 80, 28), Theme.BodyFont, Theme.Text2, ContentAlignment.MiddleLeft);
        Rectangle saveRetention = new(data.Right - 100, data.Y + 56, 80, 32);
        SmallButton(g, saveRetention, "保存", true);
        AddHit(saveRetention, () =>
        {
            if (int.TryParse(_retention.Value, out int days))
            {
                RetentionSaved?.Invoke(days);
            }
        }, () => Invalidate());
    }

    private static void Section(Graphics g, Rectangle card, string title, string subtitle)
    {
        Painter.Shadow(g, card, Theme.Radius, 3, 2);
        Painter.RoundRect(g, card, Theme.Radius, Theme.Card, Theme.Border);
        Painter.Text(g, title, new RectangleF(card.X + 18, card.Y + 10, 200, 28),
            Theme.SectionFont, Theme.Text, ContentAlignment.MiddleLeft);
        Painter.Text(g, subtitle, new RectangleF(card.X + 18, card.Y + 36, card.Width - 40, 22),
            Theme.TinyFont, Theme.Text2, ContentAlignment.MiddleLeft);
    }

    private static void SmallButton(Graphics g, Rectangle r, string text, bool primary)
    {
        if (primary)
        {
            Painter.GradientRoundRect(g, r, Theme.RadiusSm, Theme.Primary, Theme.PrimaryLight, 90f);
            Painter.Text(g, text, r, Theme.TinyFont, Color.White, ContentAlignment.MiddleCenter);
        }
        else
        {
            Painter.RoundRect(g, r, Theme.RadiusSm, Theme.Soft, Theme.Border);
            Painter.Text(g, text, r, Theme.TinyFont, Theme.Primary, ContentAlignment.MiddleCenter);
        }
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        Rectangle r = ClientRectangle;
        _monitor.Bounds = new Rectangle(r.Right - 80, 264, 48, 26);
        _always.Bounds = new Rectangle(480, 264, 48, 26);
        _tunnel.Bounds = new Rectangle(r.Right - 80, 396, 48, 26);
        _token.Bounds = new Rectangle(90, 394, Math.Max(160, r.Width - 220), 34);
        _retention.Bounds = new Rectangle(108, 538, 160, 34);
    }
}
