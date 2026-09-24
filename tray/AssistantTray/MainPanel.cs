using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Net.Http;
using System.Text.Json;
using System.Windows.Forms;

namespace AssistantTray;

/// <summary>
/// 桌面主面板（复刻效果图 01）：
/// 当前课程 + 环形倒计时 / 课堂记录（模型+分贝波形+操作按钮）/
/// 今日课表 / 拖堂监测 / 快捷操作 / 底部状态栏
/// 每 5 秒从核心轮询 /api/state 与 /api/db 刷新。
/// </summary>
internal class MainPanel : Form
{
    // 主题色（与效果图一致：深青教育科技风）
    private static readonly Color Primary = Color.FromArgb(0x0F, 0x76, 0x6E);
    private static readonly Color PrimarySoft = Color.FromArgb(0xCC, 0xFB, 0xF1);
    private static readonly Color Bg = Color.FromArgb(0xF8, 0xFA, 0xFC);
    private static readonly Color Card = Color.White;
    private static readonly Color Ink = Color.FromArgb(0x0F, 0x17, 0x2A);
    private static readonly Color Sub = Color.FromArgb(0x64, 0x74, 0x8B);
    private static readonly Color Border = Color.FromArgb(0xE2, 0xE8, 0xF0);
    private static readonly Color OkGreen = Color.FromArgb(0x16, 0xA3, 0x4A);
    private static readonly Color OkGreenSoft = Color.FromArgb(0xDC, 0xFC, 0xE7);

    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 10000 };

    // 状态数据
    private string _subject = "—";
    private string _className = "";
    private string _startEnd = "";
    private int _leftMin = 0;
    private string _model = "qwen3:1.7b";
    private bool _overtime = false;
    private bool _monitor = true;
    private bool _tunnel = false;
    private string _today = DateTime.Now.ToString("yyyy-MM-dd");
    private double[] _db = Array.Empty<double>();
    private double _avgDb = 0;

    // 控件
    private Label _runningBadge = null!;
    private Label _subjectLabel = null!;
    private Label _classLine = null!;
    private Label _leftLabel = null!;
    private Label _modelLabel = null!;
    private RingControl _ring = null!;
    private WaveControl _wave = null!;
    private Label _schedList = null!;
    private Label _overtimeState = null!;
    private Label _monitorPill = null!;
    private Label _tunnelPill = null!;
    private Label _statusBar = null!;

    public MainPanel()
    {
        Text = "AI课堂助手";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Size = new Size(760, 460);
        BackColor = Bg;
        Font = new Font("Microsoft YaHei UI", 9F);

        BuildUi();
        _timer.Tick += (_, _) => RefreshData();
        _timer.Start();
        RefreshData();
    }

    private void BuildUi()
    {
        // ---- 顶部条 ----
        var header = new Panel { Dock = DockStyle.Top, Height = 56, BackColor = Card };
        var title = new Label
        {
            Text = "AI课堂助手",
            Font = new Font("Microsoft YaHei UI", 15F, FontStyle.Bold),
            ForeColor = Primary,
            AutoSize = true,
            Location = new Point(22, 14),
        };
        _runningBadge = new Label
        {
            Text = "● 运行中",
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold),
            ForeColor = OkGreen,
            BackColor = OkGreenSoft,
            AutoSize = false,
            Size = new Size(92, 26),
            TextAlign = ContentAlignment.MiddleCenter,
            Location = new Point(Width - 122, 15),
        };
        header.Controls.Add(title);
        header.Controls.Add(_runningBadge);

        // 主体容器
        var body = new Panel { Dock = DockStyle.Fill, BackColor = Bg, Padding = new Padding(16, 12, 16, 4) };

        // ---- 左列：当前课程 ----
        var leftCard = MakeCard(16, 8, 210, 320);
        body.Controls.Add(leftCard);
        AddCardTitle(leftCard, "当前课程", 12, 10);
        _ring = new RingControl { Location = new Point(16, 40), Size = new Size(178, 178) };
        leftCard.Controls.Add(_ring);
        _subjectLabel = MakeLabel("数学", new Font("Microsoft YaHei UI", 26F, FontStyle.Bold), Primary,
            new Point(0, 236), new Size(210, 44), ContentAlignment.MiddleCenter);
        _classLine = MakeLabel("高一(3)班·09:35-10:15", 9F, Sub, new Point(0, 280), new Size(210, 22), ContentAlignment.MiddleCenter);
        _leftLabel = MakeLabel("距下课12分钟", 10.5F, Ink, new Point(0, 306), new Size(210, 24), ContentAlignment.MiddleCenter);
        leftCard.Controls.Add(_subjectLabel);
        leftCard.Controls.Add(_classLine);
        leftCard.Controls.Add(_leftLabel);

        // ---- 中列：课堂记录 ----
        var midCard = MakeCard(234, 8, 296, 320);
        body.Controls.Add(midCard);
        AddCardTitle(midCard, "课堂记录中", 12, 10);
        _modelLabel = MakeLabel("模型: qwen3:1.7b", 9F, Sub, new Point(12, 40), new Size(270, 22), ContentAlignment.MiddleLeft);
        midCard.Controls.Add(_modelLabel);
        _wave = new WaveControl { Location = new Point(12, 68), Size = new Size(272, 170) };
        midCard.Controls.Add(_wave);
        var genBtn = MakeButton("生成总结", 0, 256, 130, Primary, Color.White);
        genBtn.Click += (_, _) => PostApi("api/summarize");
        var expBtn = MakeButton("导出课堂", 142, 256, 130, Color.White, Ink);
        expBtn.Click += (_, _) => PostApi("api/export");
        midCard.Controls.Add(genBtn);
        midCard.Controls.Add(expBtn);

        // ---- 右列：课表 / 拖堂 / 快捷 ----
        var rightCard = MakeCard(538, 8, 206, 320);
        body.Controls.Add(rightCard);
        AddCardTitle(rightCard, "今日课表", 12, 10);
        _schedList = MakeLabel("", 9F, Ink, new Point(14, 40), new Size(180, 130), ContentAlignment.TopLeft);
        rightCard.Controls.Add(_schedList);
        AddCardTitle(rightCard, "拖堂监测", 12, 172);
        _overtimeState = MakeLabel("● 监测中 · 未拖堂", 9F, OkGreen, new Point(14, 204), new Size(180, 24), ContentAlignment.MiddleLeft);
        rightCard.Controls.Add(_overtimeState);
        AddCardTitle(rightCard, "快捷操作", 12, 240);
        _monitorPill = MakePill(new Point(14, 270), "开启监控：开");
        _tunnelPill = MakePill(new Point(14, 296), "隧道：未连接");
        rightCard.Controls.Add(_monitorPill);
        rightCard.Controls.Add(_tunnelPill);

        // ---- 底部状态栏 ----
        _statusBar = MakeLabel("", 8.5F, Sub, new Point(20, 424), new Size(720, 26), ContentAlignment.MiddleLeft);
        Controls.Add(header);
        Controls.Add(body);
        Controls.Add(_statusBar);

        // 圆角
        Region = RoundedRegion(Width, Height, 14);
        // 顶部可拖动
        header.MouseDown += DragMove;
        header.MouseMove += DragMove;
    }

    // ---------- 布局辅助 ----------
    private static Panel MakeCard(int x, int y, int w, int h)
    {
        var p = new Panel
        {
            Location = new Point(x, y),
            Size = new Size(w, h),
            BackColor = Card,
            Padding = new Padding(0),
        };
        p.Paint += (_, e) =>
        {
            using var pen = new Pen(Border);
            e.Graphics.DrawRectangle(pen, 0, 0, p.Width - 1, p.Height - 1);
        };
        return p;
    }

    private static void AddCardTitle(Panel card, string text, int x, int y)
    {
        card.Controls.Add(MakeLabel(text, 9.5F, Sub, new Point(x, y), new Size(160, 20), ContentAlignment.MiddleLeft));
    }

    private static Label MakeLabel(string text, float size, Color color, Point loc, Size sz, ContentAlignment align)
    {
        return MakeLabel(text, new Font("Microsoft YaHei UI", size), color, loc, sz, align);
    }

    private static Label MakeLabel(string text, Font font, Color color, Point loc, Size sz, ContentAlignment align)
    {
        return new Label
        {
            Text = text,
            Font = font,
            ForeColor = color,
            Location = loc,
            Size = sz,
            TextAlign = align,
        };
    }

    private static Label MakePill(Point loc, string text)
    {
        var l = MakeLabel(text, 8.5F, Primary, loc, new Size(168, 22), ContentAlignment.MiddleCenter);
        l.BackColor = PrimarySoft;
        return l;
    }

    private static Button MakeButton(string text, int x, int y, int w, Color back, Color fore)
    {
        return new Button
        {
            Text = text,
            FlatStyle = FlatStyle.Flat,
            BackColor = back,
            ForeColor = fore,
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold),
            Location = new Point(x, y),
            Size = new Size(w, 34),
            Cursor = Cursors.Hand,
        };
    }

    private static Region RoundedRegion(int w, int h, int r)
    {
        var gp = new GraphicsPath();
        gp.AddArc(0, 0, r * 2, r * 2, 180, 90);
        gp.AddArc(w - r * 2, 0, r * 2, r * 2, 270, 90);
        gp.AddArc(w - r * 2, h - r * 2, r * 2, r * 2, 0, 90);
        gp.AddArc(0, h - r * 2, r * 2, r * 2, 90, 90);
        gp.CloseFigure();
        return new Region(gp);
    }

    private Point _dragStart;
    private bool _dragging;

    private void DragMove(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            if (!_dragging)
            {
                _dragging = true;
                _dragStart = e.Location;
            }
            var p = PointToScreen(e.Location);
            Location = new Point(p.X - _dragStart.X, p.Y - _dragStart.Y);
        }
        else
        {
            _dragging = false;
        }
    }

    // ---------- 数据 ----------
    private static int? _webPort;

    private static int WebPort()
    {
        // 端口一旦探测到就缓存，避免 10s 轮询时反复扫描造成卡顿
        if (_webPort.HasValue) return _webPort.Value;
        // 复用托盘程序的端口探测（读 config + 顺延端口扫描 + /api/health 校验），
        // 比单纯读 config.json 更稳——核心被顺延到 18761-18777 时也能命中
        _webPort = Program.FindWebPort();
        return _webPort.Value;
    }

    private static string GetJson(string path)
    {
        try
        {
            using var hc = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            return hc.GetStringAsync($"http://127.0.0.1:{WebPort()}/{path}").Result;
        }
        catch
        {
            return "";
        }
    }

    private static void PostApi(string path)
    {
        try
        {
            using var hc = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            hc.PostAsync($"http://127.0.0.1:{WebPort()}/{path}", null).Wait();
        }
        catch { }
    }

    private void RefreshData()
    {
        try
        {
            var stateJson = GetJson("api/state");
            if (string.IsNullOrEmpty(stateJson))
            {
                // 缓存端口取不到数据：核心可能还没起、或实际绑定到了别的顺延端口。
                // 清除缓存，下次 GetJson 会重新走 FindWebPort 探测真实端口。
                _webPort = null;
            }
            if (!string.IsNullOrEmpty(stateJson))
            {
                using var doc = JsonDocument.Parse(stateJson);
                var root = doc.RootElement;
                _model = root.TryGetProperty("model", out var m) ? m.GetString() ?? "" : _model;
                _monitor = !root.TryGetProperty("monitor_enabled", out var me) || me.GetBoolean();
                _tunnel = root.TryGetProperty("tunnel", out var t) && t.GetString() == "connected";
                if (root.TryGetProperty("session", out var s) && s.ValueKind == JsonValueKind.Object)
                {
                    _subject = s.TryGetProperty("subject", out var sub) && sub.GetString() is { Length: > 0 } v ? v : _subject;
                    _className = s.TryGetProperty("class_name", out var cn) ? cn.GetString() ?? "" : "";
                    var start = s.TryGetProperty("start", out var st) ? st.GetString() ?? "" : "";
                    var end = s.TryGetProperty("end", out var en) ? en.GetString() ?? "" : "";
                    _startEnd = FormatTime(start, end);
                    _overtime = s.TryGetProperty("overtime", out var ov) && ov.GetBoolean();
                    _leftMin = s.TryGetProperty("duration_min", out var d) ? (int)Math.Round(d.GetDouble()) : 0;
                }
            }

            var dbJson = GetJson("api/db");
            if (!string.IsNullOrEmpty(dbJson))
            {
                using var doc = JsonDocument.Parse(dbJson);
                var root = doc.RootElement;
                if (root.TryGetProperty("samples", out var sa) && sa.ValueKind == JsonValueKind.Array)
                {
                    var list = new List<double>();
                    foreach (var x in sa.EnumerateArray()) list.Add(x.GetDouble());
                    _db = list.ToArray();
                }
                _avgDb = root.TryGetProperty("avg_db", out var av) ? av.GetDouble() : 0;
            }

            Render();
        }
        catch
        {
            // 核心未就绪：保持上次画面
        }
    }

    private string FormatTime(string iso, string isoEnd)
    {
        try
        {
            if (iso.Length >= 16 && isoEnd.Length >= 16)
                return $"{iso.Substring(11, 5)}-{isoEnd.Substring(11, 5)}";
        }
        catch { }
        return _startEnd;
    }

    private void Render()
    {
        _subjectLabel.Text = _subject;
        _classLine.Text = string.IsNullOrEmpty(_className)
            ? (_startEnd.Length > 0 ? _startEnd : "未在上课")
            : $"{_className}·{_startEnd}";
        _leftLabel.Text = _overtime ? "已拖堂，请尽快收尾" : $"距下课{_leftMin}分钟";
        _modelLabel.Text = $"模型: {_model}";
        _ring.Progress = _leftMin > 0 ? Math.Clamp(1.0 - _leftMin / 45.0, 0, 1) : 0.6;
        _ring.Invalidate();
        _wave.Samples = _db;
        _wave.Avg = _avgDb;
        _wave.Invalidate();
        _overtimeState.Text = _overtime
            ? "● 监测中 · 已拖堂"
            : "● 监测中 · 未拖堂";
        _overtimeState.ForeColor = _overtime ? Color.FromArgb(0xDC, 0x26, 0x26) : OkGreen;
        _monitorPill.Text = _monitor ? "开启监控：开" : "开启监控：关";
        _tunnelPill.Text = _tunnel ? "隧道：已连接" : "隧道：未连接";
        _tunnelPill.BackColor = _tunnel ? OkGreenSoft : PrimarySoft;
        _tunnelPill.ForeColor = _tunnel ? OkGreen : Primary;
        _statusBar.Text =
            $"监控 | {(_monitor ? "开启" : "关闭")}    存储: 导出{_today}    隧道 | {(_tunnel ? "已连接" : "未连接")}";
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        // 失焦 800ms 后自动隐藏（避免误点）
        System.Windows.Forms.Timer t = new() { Interval = 800 };
        t.Tick += (_, _) =>
        {
            t.Stop();
            if (!ContainsFocus) Hide();
        };
        t.Start();
    }
}

/// <summary>环形倒计时控件（GDI+）</summary>
internal class RingControl : Control
{
    public double Progress { get; set; } = 0.6;
    private static readonly Color Primary = Color.FromArgb(0x0F, 0x76, 0x6E);
    private static readonly Color Track = Color.FromArgb(0xE2, 0xE8, 0xF0);

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new Rectangle(6, 6, Width - 12, Height - 12);
        using var trackPen = new Pen(Track, 12) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        using var progPen = new Pen(Primary, 12) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawArc(trackPen, rect, -90, 360);
        g.DrawArc(progPen, rect, -90, (float)(360 * Progress));
        // 中心文字
        var pct = $"{(int)(Progress * 100)}%";
        using var f = new Font("Microsoft YaHei UI", 16F, FontStyle.Bold);
        using var b = new SolidBrush(Primary);
        var sz = g.MeasureString(pct, f);
        g.DrawString(pct, f, b, (Width - sz.Width) / 2, (Height - sz.Height) / 2);
    }
}

/// <summary>分贝柱状波形控件（GDI+）</summary>
internal class WaveControl : Control
{
    public double[] Samples { get; set; } = Array.Empty<double>();
    public double Avg { get; set; } = 0;

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.White);
        using var gridPen = new Pen(Color.FromArgb(0xF1, 0xF5, 0xF9));
        for (int y = 0; y < Height; y += 28) g.DrawLine(gridPen, 0, y, Width, y);

        if (Samples.Length == 0) return;
        var n = Samples.Length;
        var barW = (float)Width / n;
        for (int i = 0; i < n; i++)
        {
            var h = (float)(Samples[i] / 140.0 * (Height - 10));
            using var b = new SolidBrush(Color.FromArgb(0x0F, 0x76, 0x6E));
            var alpha = 90 + (int)((Samples[i] / 140.0) * 165);
            b.Color = Color.FromArgb(Math.Clamp(alpha, 60, 255), 0x0F, 0x76, 0x6E);
            g.FillRectangle(b, i * barW + 1, Height - h - 4, Math.Max(barW - 2, 1), h);
        }
        // 平均值线
        if (Avg > 0)
        {
            var ay = Height - (float)(Avg / 140.0 * (Height - 10)) - 4;
            using var linePen = new Pen(Color.FromArgb(0xDC, 0x26, 0x26), 1.5F) { DashStyle = DashStyle.Dash };
            g.DrawLine(linePen, 0, ay, Width, ay);
        }
    }
}
