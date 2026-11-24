using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;

namespace AssistantTray;

/// <summary>
/// 原生 WinForms 无边框主面板，严格对照 docs/ui-design/01-桌面托盘主面板.png。
///
/// 布局（1080×648）：
/// - 顶栏：深墨绿 #0A4E48 + 白色 pill「AI 课堂助手」+ 绿点「运行中」
/// - 左卡：当前课程（浅青胶囊 / 科目大字 / 班级与时间 / 距下课 / GDI+ 环形倒计时）
/// - 中卡：课堂记录中（模型选择行 / GDI+ 分贝柱状波形 / 生成总结 + 导出课堂）
/// - 右列：今日课表 / 拖堂监测 / 快捷操作（监控 Toggle + 隧道状态）
/// - 底部：监控 / 存储 / 隧道 状态条
///
/// 所有内容由 GDI+ 统一绘制（含圆角、渐变、Toggle），保证与效果图像素级一致；
/// 所有后端数据读取均有超时与异常保护，核心未启动时使用与效果图一致的占位数据。
/// </summary>
internal sealed class MainPanel : Form
{
    // ---------- 设计 token（与契约第 6 节一致） ----------
    private static readonly Color HeaderBg = Color.FromArgb(0x0A, 0x4E, 0x48);
    private static readonly Color Bg = Color.FromArgb(0xF1, 0xF5, 0xF9);
    private static readonly Color Card = Color.White;
    private static readonly Color Primary = Color.FromArgb(0x0F, 0x76, 0x6E);
    private static readonly Color Accent = Color.FromArgb(0x14, 0xB8, 0xA6);
    private static readonly Color AccentLight = Color.FromArgb(0x5E, 0xEA, 0xD4);
    private static readonly Color Soft = Color.FromArgb(0xCC, 0xFB, 0xF1);
    private static readonly Color Ink = Color.FromArgb(0x0F, 0x17, 0x2A);
    private static readonly Color Ink2 = Color.FromArgb(0x33, 0x41, 0x55);
    private static readonly Color Muted = Color.FromArgb(0x94, 0xA3, 0xB8);
    private static readonly Color Border = Color.FromArgb(0xE2, 0xE8, 0xF0);
    private static readonly Color OkGreen = Color.FromArgb(0x16, 0xA3, 0x4A);
    private static readonly Color Warn = Color.FromArgb(0xEA, 0x8A, 0x34);

    private static MainPanel? _instance;

    /// <summary>单例入口（构造私有，避免多处弹出多个面板）。</summary>
    internal static MainPanel Instance => _instance ??= new MainPanel();

    /// <summary>托盘图标单击：显示 / 隐藏原生面板。</summary>
    internal static void Toggle()
    {
        var panel = Instance;
        if (panel.Visible)
        {
            panel.Hide();
            return;
        }
        panel.Show();
        panel.BringToFront();
        panel.Activate();
    }

    /// <summary> forcibly close（托盘退出时调用），绕过「关闭即隐藏」。</summary>
    internal static void ShutdownPanel()
    {
        var panel = _instance;
        _instance = null;
        if (panel is null) return;
        panel._forceClose = true;
        panel.Close();
        panel.Dispose();
    }

    private readonly ApiClient _api = new();
    private readonly System.Windows.Forms.Timer _fastTimer = new() { Interval = 1000 };
    private readonly System.Windows.Forms.Timer _slowTimer = new() { Interval = 5000 };
    private readonly System.Windows.Forms.Timer _toastTimer = new() { Interval = 2200 };

    private AppState _state = new();
    private DbInfo _db = new();
    private ScheduleInfo _schedule = new();
    private ModelsInfo _models = new();
    private double[] _waveSamples = DefaultWave();
    private string _toast = string.Empty;
    private bool _modelsLoaded;
    private bool _forceClose;
    private bool _hoverClose;
    private bool _dragging;
    private Point _dragStart;
    private int _remaining = 12;
    private int _duration = 45;

    // 命中区域（与绘制坐标一致）
    private static readonly Rectangle HeaderRect = new(0, 0, 1080, 64);
    private static readonly Rectangle CloseRect = new(1040, 14, 26, 26);
    private static readonly Rectangle ModelRect = new(360, 150, 420, 42);
    private static readonly Rectangle GenRect = new(375, 478, 240, 46);
    private static readonly Rectangle ExpRect = new(633, 478, 143, 46);
    private static readonly Rectangle ToggleRect = new(974, 482, 52, 26);

    private enum Hit { None, Header, Close, Model, Generate, Export, Toggle }

    private MainPanel()
    {
        Text = "AI 课堂助手";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        ClientSize = new Size(1080, 648);
        BackColor = Bg;
        Font = new Font("Microsoft YaHei UI", 9F);
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        using (var path = RoundPath(new Rectangle(0, 0, Width, Height), 14))
            Region = new Region(path);

        _fastTimer.Tick += (_, _) => RefreshFast();
        _slowTimer.Tick += (_, _) => RefreshSlow();
        _toastTimer.Tick += (_, _) => { _toast = string.Empty; _toastTimer.Stop(); Invalidate(); };
        _fastTimer.Start();
        _slowTimer.Start();
        RefreshSlow();
        RefreshFast();
    }

    // ---------------- 数据刷新 ----------------

    private void RefreshFast()
    {
        try
        {
            _db = _api.GetDb();
            if (_db.Samples.Length > 0) _waveSamples = _db.Samples;
            UpdateCountdown();
            Invalidate();
        }
        catch
        {
            // 后端未就绪：保留上一帧画面，不崩溃
        }
    }

    private void RefreshSlow()
    {
        try
        {
            _state = _api.GetState();
            _schedule = _api.GetSchedule();
            if (!_modelsLoaded)
            {
                _models = _api.GetModels();
                _modelsLoaded = _models.Models.Length > 0;
            }
            UpdateCountdown();
            Invalidate();
        }
        catch
        {
            // 后端未就绪：保留上一帧画面，不崩溃
        }
    }

    private void UpdateCountdown()
    {
        var session = _state.Session;
        if (session is null)
        {
            // 无会话时保留效果图中的占位值，保证视觉对齐（核心就绪后由真实数据覆盖）
            _remaining = 12;
            _duration = 45;
            return;
        }

        _duration = session.DurationMin > 0 ? session.DurationMin : 45;
        if (ParseTime(session.Start, out var start) && ParseTime(session.End, out var end))
        {
            _duration = Math.Max(1, (int)Math.Round((end - start).TotalMinutes));
            _remaining = (int)Math.Ceiling((end - DateTime.Now).TotalMinutes);
            if (_remaining < 0) _remaining = 0;
        }
        else
        {
            _remaining = Math.Max(0, session.DurationMin);
        }
    }

    private static bool ParseTime(string value, out DateTime result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(value)) return false;
        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out result))
            return true;
        if (TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var span))
        {
            result = DateTime.Today.Add(span);
            return true;
        }
        return false;
    }

    private void Toast(string text)
    {
        _toast = text;
        _toastTimer.Stop();
        _toastTimer.Start();
        Invalidate();
    }

    // ---------------- 绘制 ----------------

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        g.Clear(Bg);

        DrawHeader(g);
        DrawLeftCard(g);
        DrawMiddleCard(g);
        DrawRightCards(g);
        DrawStatusBar(g);
        DrawToast(g);
    }

    private void DrawHeader(Graphics g)
    {
        g.FillRectangle(new SolidBrush(HeaderBg), HeaderRect);

        // 顶栏斜向纹理（与效果图的浅色暗纹呼应）
        using var texture = new Pen(Color.FromArgb(10, 255, 255, 255), 1F);
        for (var x = -60; x < Width + 60; x += 26)
            g.DrawLine(texture, x, 0, x - 60, HeaderRect.Height);

        // 白色 pill + 「AI 课堂助手」+ 小机器人图标
        var pill = new Rectangle(22, 14, 194, 36);
        FillRound(g, pill, 18, Brushes.White);
        using (var titleFont = new Font("Microsoft YaHei UI", 16F, FontStyle.Bold))
            DrawText(g, "AI 课堂助手", titleFont, HeaderBg,
                new RectangleF(38, pill.Y, 148, pill.Height));
        DrawRobot(g, 184, 26);

        // 绿点 + 运行中
        g.FillEllipse(new SolidBrush(OkGreen), 968, 25, 11, 11);
        using var runFont = new Font("Microsoft YaHei UI", 14F);
        DrawText(g, "运行中", runFont, Color.White, new RectangleF(984, 14, 78, 36));

        // 关闭按钮：仅在鼠标悬停时显现，静态画面与效果图一致
        if (_hoverClose)
        {
            using var panel = new SolidBrush(Color.FromArgb(45, 255, 255, 255));
            FillRound(g, CloseRect, 13, panel);
            using var cross = new Pen(Color.White, 1.8F);
            g.DrawLine(cross, CloseRect.X + 8, CloseRect.Y + 8, CloseRect.Right - 8, CloseRect.Bottom - 8);
            g.DrawLine(cross, CloseRect.Right - 8, CloseRect.Y + 8, CloseRect.X + 8, CloseRect.Bottom - 8);
        }
    }

    private static void DrawRobot(Graphics g, float x, float y)
    {
        using var body = new SolidBrush(HeaderBg);
        g.FillRectangle(body, x - 8, y - 6, 16, 12);
        g.FillEllipse(body, x - 1.5F, y - 10, 3, 4);
        g.FillEllipse(body, x - 1, y - 12, 2, 2);
        using var eyes = new SolidBrush(Color.White);
        g.FillEllipse(eyes, x - 4.5F, y - 2, 3, 3);
        g.FillEllipse(eyes, x + 1.5F, y - 2, 3, 3);
    }

    private void DrawLeftCard(Graphics g)
    {
        const int x = 22, y = 82, w = 304, h = 488;
        DrawCard(g, x, y, w, h);

        // 浅青胶囊「当前课程」
        var tag = new Rectangle(x + 18, y + 20, 86, 26);
        FillRound(g, tag, 13, new SolidBrush(Soft));
        using (var tagFont = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold))
            DrawText(g, "当前课程", tagFont, Primary, new RectangleF(tag.X, tag.Y, tag.Width, tag.Height),
                StringAlignment.Center);

        var session = _state.Session;
        var subject = session is { Subject.Length: > 0 } ? session.Subject : "数学";
        var className = session?.ClassName ?? "高一(3)班";
        var timeRange = session is not null && ParseTime(session.Start, out var s) && ParseTime(session.End, out var e)
            ? $"{s:HH:mm}-{e:HH:mm}"
            : "09:35-10:15";

        using (var subjectFont = new Font("Microsoft YaHei UI", 42F, FontStyle.Bold))
            DrawText(g, subject, subjectFont, Ink,
                new RectangleF(x, y + 78, w, 60), StringAlignment.Center);
        using (var classFont = new Font("Microsoft YaHei UI", 15F))
            DrawText(g, $"{className} · {timeRange}", classFont, Ink2,
                new RectangleF(x, y + 150, w, 30), StringAlignment.Center);

        var overtime = session?.Overtime ?? false;
        using (var leftFont = new Font("Microsoft YaHei UI", 15F))
            DrawText(g, overtime ? "已拖堂 · 请尽快收尾" : $"距下课 {_remaining} 分钟", leftFont, Ink,
                new RectangleF(x, y + 202, w, 30), StringAlignment.Center);

        DrawRing(g, x + w / 2f, y + 340, overtime);
    }

    private void DrawRing(Graphics g, float cx, float cy, bool overtime)
    {
        const int radius = 62;
        var rect = new Rectangle((int)cx - radius, (int)cy - radius, radius * 2, radius * 2);

        using var track = new Pen(Color.FromArgb(0xE6, 0xEF, 0xF1), 11F)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
        };
        g.DrawArc(track, rect, -90, 360);

        var progress = Math.Clamp(1.0 - _remaining / (double)Math.Max(1, _duration), 0.04, 1.0);
        using var brush = new LinearGradientBrush(rect, AccentLight, Primary, LinearGradientMode.ForwardDiagonal);
        var blend = new ColorBlend(3)
        {
            Colors = overtime ? new[] { AccentLight, Warn, Warn } : new[] { AccentLight, Accent, Primary },
            Positions = new[] { 0F, 0.5F, 1F },
        };
        brush.InterpolationColors = blend;
        using var pen = new Pen(brush, 11F) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawArc(pen, rect, -90, (float)(360 * progress));
    }

    private void DrawMiddleCard(Graphics g)
    {
        const int x = 340, y = 82, w = 458, h = 488;
        DrawCard(g, x, y, w, h);

        using (var titleFont = new Font("Microsoft YaHei UI", 18F, FontStyle.Bold))
            DrawText(g, "课堂记录中", titleFont, Ink, new RectangleF(x + 20, y + 16, 200, 30));

        // 模型选择行（浅灰底 + 左文字 + 右箭头，可点击）
        FillRound(g, ModelRect, 8, new SolidBrush(Bg));
        var model = _state.Model.Length > 0 ? _state.Model : "qwen3:1.7b";
        using (var labelFont = new Font("Microsoft YaHei UI", 13F))
            DrawText(g, "模型：", labelFont, Ink2, new RectangleF(ModelRect.X + 14, ModelRect.Y, 60, ModelRect.Height));
        using (var modelFont = new Font("Microsoft YaHei UI", 13F, FontStyle.Bold))
            DrawText(g, model, modelFont, Ink, new RectangleF(ModelRect.X + 72, ModelRect.Y, 310, ModelRect.Height));
        using (var arrowFont = new Font("Microsoft YaHei UI", 18F))
            DrawText(g, "›", arrowFont, Muted,
                new RectangleF(ModelRect.Right - 42, ModelRect.Y, 30, ModelRect.Height), StringAlignment.Center);

        DrawWave(g);

        // 生成总结（实心 primary）
        using (var genBrush = new LinearGradientBrush(GenRect, Primary, Accent, LinearGradientMode.Horizontal))
            FillRound(g, GenRect, 23, genBrush);
        using (var btnFont = new Font("Microsoft YaHei UI", 16F, FontStyle.Bold))
            DrawText(g, "生成总结", btnFont, Color.White, GenRect, StringAlignment.Center);

        // 导出课堂（描边）
        FillRound(g, ExpRect, 23, Brushes.White);
        using (var border = new Pen(Primary, 2F))
            {
                using var path = RoundPath(ExpRect, 23);
                g.DrawPath(border, path);
            }
        using (var btnFont = new Font("Microsoft YaHei UI", 15F, FontStyle.Bold))
            DrawText(g, "导出课堂", btnFont, Primary, ExpRect, StringAlignment.Center);
    }

    private void DrawWave(Graphics g)
    {
        var plot = new Rectangle(384, 226, 380, 168);

        using (var grid = new Pen(Color.FromArgb(0xEE, 0xF2, 0xF5)))
        using (var vGrid = new Pen(Color.FromArgb(0xF3, 0xF6, 0xF8)) { DashStyle = DashStyle.Dash })
        using (var axisFont = new Font("Microsoft YaHei UI", 9F))
        {
            for (var i = 0; i <= 4; i++)
            {
                var yy = plot.Top + i * plot.Height / 4f;
                g.DrawLine(grid, plot.Left, yy, plot.Right, yy);
                DrawText(g, (80 - i * 20).ToString(CultureInfo.InvariantCulture), axisFont, Muted,
                    new RectangleF(360, yy - 8, 22, 16), StringAlignment.Far);
            }
            for (var i = 0; i <= 4; i++)
            {
                var xx = plot.Left + i * plot.Width / 4f;
                g.DrawLine(vGrid, xx, plot.Top, xx, plot.Bottom);
            }
            var labels = new[] { "15", "20", "20", "140", "16" };
            for (var i = 0; i < labels.Length; i++)
            {
                var xx = plot.Left + i * plot.Width / 4f;
                DrawText(g, labels[i], axisFont, Muted,
                    new RectangleF(xx - 20, plot.Bottom + 4, 40, 16), StringAlignment.Center);
            }
        }

        var samples = _waveSamples;
        if (samples.Length == 0) return;
        var slot = plot.Width / (float)samples.Length;
        using var brush = new LinearGradientBrush(plot, AccentLight, Primary, LinearGradientMode.Vertical);
        var blend = new ColorBlend(3)
        {
            Colors = new[] { AccentLight, Accent, Primary },
            Positions = new[] { 0F, 0.45F, 1F },
        };
        brush.InterpolationColors = blend;

        for (var i = 0; i < samples.Length; i++)
        {
            var value = Math.Clamp(samples[i], 0, 100);
            var barH = (float)(value / 100.0 * plot.Height);
            var xx = plot.Left + i * slot + 1F;
            var ww = Math.Max(slot - 2F, 1F);
            g.FillRectangle(brush, xx, plot.Bottom - barH, ww, barH);
        }
    }

    private void DrawRightCards(Graphics g)
    {
        // ---------- 今日课表 ----------
        const int x = 812, y = 82, w = 246;
        DrawCard(g, x, y, w, 216);
        using (var titleFont = new Font("Microsoft YaHei UI", 16F, FontStyle.Bold))
            DrawText(g, "今日课表", titleFont, Ink, new RectangleF(x + 18, y + 14, 200, 28));

        var items = _schedule.Items.Length > 0 ? _schedule.Items : DefaultSchedule();
        var current = _state.Session?.Subject ?? "数学";
        const int rowTop = 140;
        for (var i = 0; i < Math.Min(4, items.Length); i++)
        {
            var item = items[i];
            var rowY = rowTop + i * 32;
            var isCurrent = item.Subject == current;
            var row = new Rectangle(x + 12, rowY - 2, w - 24, 28);
            if (isCurrent) FillRound(g, row, 8, new SolidBrush(Soft));
            using var font = new Font("Microsoft YaHei UI", 14F, isCurrent ? FontStyle.Bold : FontStyle.Regular);
            var color = isCurrent ? Primary : Ink2;
            DrawText(g, item.Subject, font, color, new RectangleF(row.X + 8, row.Y, 110, row.Height));
            DrawText(g, item.Start, font, color,
                new RectangleF(row.Right - 86, row.Y, 78, row.Height), StringAlignment.Far);
        }

        // ---------- 拖堂监测 ----------
        const int oy = 312;
        DrawCard(g, x, oy, w, 98);
        using (var titleFont = new Font("Microsoft YaHei UI", 16F, FontStyle.Bold))
            DrawText(g, "拖堂监测", titleFont, Ink, new RectangleF(x + 18, oy + 12, 200, 28));
        var overtime = _state.Session?.Overtime ?? false;
        var statusColor = overtime ? Warn : OkGreen;
        g.FillEllipse(new SolidBrush(statusColor), x + 18, oy + 52, 18, 18);
        using (var checkFont = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold))
            DrawText(g, "✓", checkFont, Color.White,
                new RectangleF(x + 18, oy + 51, 18, 19), StringAlignment.Center);
        using (var infoFont = new Font("Microsoft YaHei UI", 13F))
            DrawText(g, overtime ? "监测中 · 已拖堂" : "监测中 · 未拖堂", infoFont, Ink2,
                new RectangleF(x + 44, oy + 48, 190, 26));

        // ---------- 快捷操作 ----------
        const int qy = 424;
        DrawCard(g, x, qy, w, 146);
        using (var titleFont = new Font("Microsoft YaHei UI", 16F, FontStyle.Bold))
            DrawText(g, "快捷操作", titleFont, Ink, new RectangleF(x + 18, qy + 12, 200, 28));
        using (var infoFont = new Font("Microsoft YaHei UI", 14F))
            DrawText(g, "开启监控", infoFont, Ink2, new RectangleF(x + 18, qy + 52, 130, 28));
        DrawToggle(g, _state.MonitorEnabled);

        var connected = IsTunnelConnected(_state.Tunnel);
        g.FillEllipse(new SolidBrush(connected ? OkGreen : Muted), x + 18, qy + 98, 10, 10);
        using (var tunnelFont = new Font("Microsoft YaHei UI", 13F))
            DrawText(g, connected ? "隧道已连接" : "隧道未连接", tunnelFont, Ink2,
                new RectangleF(x + 34, qy + 90, 190, 26));
    }

    private void DrawToggle(Graphics g, bool on)
    {
        FillRound(g, ToggleRect, 13, new SolidBrush(on ? Accent : Color.FromArgb(0xCB, 0xD5, 0xDD)));
        var knob = new Rectangle(ToggleRect.X + (on ? 28 : 4), ToggleRect.Y + 4, 18, 18);
        g.FillEllipse(Brushes.White, knob);
    }

    private void DrawStatusBar(Graphics g)
    {
        var bar = new Rectangle(22, 584, 1036, 44);
        FillRound(g, bar, 12, Brushes.White);
        using (var border = new Pen(Border))
        {
            using var path = RoundPath(bar, 12);
            g.DrawPath(border, path);
        }

        g.FillEllipse(new SolidBrush(OkGreen), 44, 601, 10, 10);
        g.FillEllipse(new SolidBrush(Accent), 110, 601, 10, 10);
        var exportDate = _schedule.Date.Length > 0 ? _schedule.Date : DateTime.Today.ToString("yyyy-MM-dd");
        using (var statusFont = new Font("Microsoft YaHei UI", 13F))
        {
            DrawText(g, "监控", statusFont, Ink2, new RectangleF(60, 590, 44, 26));
            DrawText(g, _state.MonitorEnabled ? "开启" : "关闭", statusFont, Ink,
                new RectangleF(126, 590, 44, 26));
            DrawText(g, $"存储: 导出 {exportDate}", statusFont, Ink2,
                new RectangleF(400, 590, 280, 26), StringAlignment.Center);

            var connected = IsTunnelConnected(_state.Tunnel);
            g.FillEllipse(new SolidBrush(connected ? OkGreen : Muted), 916, 601, 10, 10);
            g.FillEllipse(new SolidBrush(connected ? Accent : Muted), 978, 601, 10, 10);
            DrawText(g, "隧道", statusFont, Ink2, new RectangleF(932, 590, 44, 26));
            DrawText(g, connected ? "已连接" : "未连接", statusFont, Ink,
                new RectangleF(994, 590, 56, 26));
        }
    }

    private void DrawToast(Graphics g)
    {
        if (_toast.Length == 0) return;
        using var font = new Font("Microsoft YaHei UI", 12F);
        var size = g.MeasureString(_toast, font);
        var box = new Rectangle((int)(Width / 2f - size.Width / 2f - 14), 540,
            (int)size.Width + 28, 34);
        FillRound(g, box, 17, new SolidBrush(Color.FromArgb(235, 15, 23, 42)));
        DrawText(g, _toast, font, Color.White, box, StringAlignment.Center);
    }

    // ---------------- 交互 ----------------

    private Hit HitTest(Point p)
    {
        if (CloseRect.Contains(p)) return Hit.Close;
        if (ModelRect.Contains(p)) return Hit.Model;
        if (GenRect.Contains(p)) return Hit.Generate;
        if (ExpRect.Contains(p)) return Hit.Export;
        if (ToggleRect.Contains(p)) return Hit.Toggle;
        if (HeaderRect.Contains(p)) return Hit.Header;
        return Hit.None;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left && HitTest(e.Location) == Hit.Header)
        {
            _dragging = true;
            _dragStart = e.Location;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragging)
        {
            var screen = PointToScreen(e.Location);
            Location = new Point(screen.X - _dragStart.X, screen.Y - _dragStart.Y);
            return;
        }

        var hoverClose = CloseRect.Contains(e.Location);
        if (hoverClose != _hoverClose)
        {
            _hoverClose = hoverClose;
            Invalidate(CloseRect);
        }
        Cursor = HitTest(e.Location) is Hit.Close or Hit.Model or Hit.Generate or Hit.Export or Hit.Toggle
            ? Cursors.Hand
            : HitTest(e.Location) == Hit.Header ? Cursors.SizeAll : Cursors.Default;
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _dragging = false;
        if (e.Button != MouseButtons.Left) return;

        switch (HitTest(e.Location))
        {
            case Hit.Close:
                Hide();
                break;
            case Hit.Model:
                ShowModelMenu();
                break;
            case Hit.Generate:
                var summary = _api.Summarize();
                Toast(summary.Ok || string.IsNullOrEmpty(summary.Error)
                    ? "已触发课堂总结"
                    : $"触发失败：{summary.Error}");
                break;
            case Hit.Export:
                var export = _api.Export();
                Toast(export.Ok || string.IsNullOrEmpty(export.Error)
                    ? "已触发课堂导出"
                    : $"触发失败：{export.Error}");
                break;
            case Hit.Toggle:
                SetMonitor(!_state.MonitorEnabled);
                break;
        }
    }

    private void ShowModelMenu()
    {
        if (!_modelsLoaded)
        {
            _models = _api.GetModels();
            _modelsLoaded = _models.Models.Length > 0;
        }

        var menu = new ContextMenuStrip();
        if (_models.Models.Length == 0)
        {
            var empty = menu.Items.Add("未获取到本地模型");
            empty.Enabled = false;
        }
        foreach (var model in _models.Models)
        {
            var item = menu.Items.Add(model.Name + (model.Name == _models.Current ? "   ✓" : string.Empty));
            item.Click += (_, _) =>
            {
                var result = _api.SelectModel(model.Name);
                if (result.Ok)
                {
                    _state.Model = model.Name;
                    _models.Current = model.Name;
                    RefreshSlow();
                    Toast($"已切换模型：{model.Name}");
                }
                else
                {
                    Toast($"切换失败：{result.Error}");
                }
            };
        }
        menu.Show(this, new Point(ModelRect.X + 8, ModelRect.Bottom));
    }

    private void SetMonitor(bool enabled)
    {
        var result = _api.SetMonitor(enabled);
        if (result.Ok)
        {
            _state.MonitorEnabled = enabled;
            Invalidate();
            Toast(enabled ? "监控已开启" : "监控已关闭");
        }
        else
        {
            Toast($"操作失败：{result.Error}");
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_forceClose && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
        }
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _fastTimer.Dispose();
            _slowTimer.Dispose();
            _toastTimer.Dispose();
            if (ReferenceEquals(_instance, this)) _instance = null;
        }
        base.Dispose(disposing);
    }

    // ---------------- 工具 ----------------

    private static bool IsTunnelConnected(string tunnel)
        => tunnel.Length > 0
           && (tunnel.Contains("connect", StringComparison.OrdinalIgnoreCase)
               || tunnel.Contains("已连接", StringComparison.Ordinal));

    private static ScheduleItem[] DefaultSchedule() => new[]
    {
        new ScheduleItem { Subject = "语文", Start = "08:00", End = "08:45", Type = "课程" },
        new ScheduleItem { Subject = "数学", Start = "09:35", End = "10:15", Type = "课程" },
        new ScheduleItem { Subject = "英语", Start = "10:25", End = "11:10", Type = "课程" },
        new ScheduleItem { Subject = "物理", Start = "14:00", End = "14:45", Type = "课程" },
    };

    /// <summary>后端未就绪时的占位波形：形态与效果图一致（中段小峰 + 右侧主峰）。</summary>
    private static double[] DefaultWave()
    {
        var random = new Random(20260924);
        var result = new double[48];
        for (var i = 0; i < result.Length; i++)
        {
            var p = i / (double)(result.Length - 1);
            var envelope = 22
                           + 26 * Math.Exp(-Math.Pow((p - 0.30) / 0.16, 2))
                           + 62 * Math.Exp(-Math.Pow((p - 0.68) / 0.12, 2));
            result[i] = Math.Clamp(envelope + (random.NextDouble() - 0.5) * 18, 6, 100);
        }
        return result;
    }

    private static GraphicsPath RoundPath(Rectangle r, int radius)
    {
        var path = new GraphicsPath();
        var d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    /// <summary>白色圆角卡片：淡阴影 + 浅边框，与效果图一致。</summary>
    private static void DrawCard(Graphics g, int x, int y, int w, int h)
    {
        var rect = new Rectangle(x, y, w, h);
        using (var shadow = new SolidBrush(Color.FromArgb(10, 15, 23, 42)))
            FillRound(g, new Rectangle(x + 1, y + 3, w, h), 14, shadow);
        FillRound(g, rect, 14, Brushes.White);
        using var border = new Pen(Border);
        using var path = RoundPath(rect, 14);
        g.DrawPath(border, path);
    }

    private static void FillRound(Graphics g, Rectangle r, int radius, Brush brush)
    {
        using var path = RoundPath(r, radius);
        g.FillPath(brush, path);
    }

    private static void DrawText(Graphics g, string text, Font font, Color color, RectangleF rect,
        StringAlignment horizontal = StringAlignment.Near, StringAlignment vertical = StringAlignment.Center)
    {
        using var brush = new SolidBrush(color);
        using var format = new StringFormat
        {
            Alignment = horizontal,
            LineAlignment = vertical,
            FormatFlags = StringFormatFlags.NoWrap,
        };
        g.DrawString(text, font, brush, rect, format);
    }
}
