using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using AiClassAssistant.Ui.Rendering;
using AiClassAssistant.Ui.Theming;

namespace AiClassAssistant.Ui.Shell;

/// <summary>
/// 无边框控制台主窗口：深青顶栏 + 下划线 tab + 视图容器 + 底部状态条。
/// 顶栏可拖动；ESC / 关闭按钮仅隐藏窗口，不退出进程（由 App 层控制生命周期）。
/// </summary>
public sealed class ConsoleShell : Form
{
    private const uint WmNcLButtonDown = 0x00A1;
    private const int HtCaption = 2;

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    private sealed class Entry
    {
        public string Key = "";
        public string TabText = "";
        public ViewBase View = null!;
    }

    private readonly List<Entry> _entries = new();
    private readonly Chrome _chrome;
    private readonly ShellStatusBar _statusBar;
    private readonly Panel _host;
    private Entry? _current;

    public ConsoleShell()
    {
        Text = "AI 课堂助手";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        Size = Theme.ConsoleSize;
        BackColor = Theme.Bg;
        KeyPreview = true;
        ShowInTaskbar = true;

        _chrome = new Chrome(this) { Dock = DockStyle.Top, Height = Theme.HeaderHeight + 40 };
        _statusBar = new ShellStatusBar { Dock = DockStyle.Bottom, Height = Theme.StatusHeight };
        _host = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg };

        // WinForms Dock 按逆序处理：最后添加的先停靠。
        // 必须让 host(Fill) 最先添加、chrome(Top) 最后添加，
        // 否则 host 会先占满客户区，顶栏/状态条浮在视图之上遮挡内容。
        Controls.Add(_host);
        Controls.Add(_statusBar);
        Controls.Add(_chrome);
    }

    /// <summary>视图切换事件（供 App 层联动托盘菜单）。</summary>
    public event Action<string>? ViewChanged;

    /// <summary>注册视图；首个注册的视图默认显示。</summary>
    public void RegisterView(string key, string tabText, ViewBase view)
    {
        var entry = new Entry { Key = key, TabText = tabText, View = view };
        _entries.Add(entry);
        view.Visible = false;
        _host.Controls.Add(view);
        if (_current is null)
        {
            ShowView(key);
        }
        else
        {
            _chrome.Invalidate();
        }
    }

    /// <summary>切换到指定视图。</summary>
    public void ShowView(string key)
    {
        Entry? found = _entries.FirstOrDefault(e => e.Key == key);
        if (found is null)
        {
            return;
        }

        _current = found;
        foreach (Entry e in _entries)
        {
            e.View.Visible = ReferenceEquals(e, found);
        }

        found.View.OnShow();
        _chrome.Invalidate();
        Text = $"AI 课堂助手 · {found.View.Title}";
        ViewChanged?.Invoke(key);
    }

    public string CurrentKey => _current?.Key ?? "";

    /// <summary>更新底部状态条三段文字。</summary>
    public void SetStatus(string left, string center, string right)
    {
        _statusBar.LeftText = left;
        _statusBar.CenterText = center;
        _statusBar.RightText = right;
        _statusBar.Invalidate();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape)
        {
            Hide();
            e.SuppressKeyPress = true;
            return;
        }

        base.OnKeyDown(e);
    }

    // ---------- 顶栏 + tab 自绘 ----------
    private sealed class Chrome : Canvas
    {
        private readonly ConsoleShell _shell;
        private Rectangle _minBtn;
        private Rectangle _closeBtn;
        private Rectangle _badge;

        public Chrome(ConsoleShell shell)
        {
            _shell = shell;
            BackColor = Theme.HeaderBg;
        }

        protected override void Draw(Graphics g, Rectangle rect)
        {
            ClearHits();
            int headerH = Math.Min(Theme.HeaderHeight, rect.Height);

            // 顶栏
            using (var brush = new SolidBrush(Theme.HeaderBg))
            {
                g.FillRectangle(brush, new Rectangle(rect.X, rect.Y, rect.Width, headerH));
            }

            // 白色 pill logo
            Rectangle logo = new(16, 12, 190, 32);
            Painter.RoundRect(g, logo, 16, Color.White);
            IconSet.Robot(g, new Rectangle(logo.X + 7, logo.Y + 5, 22, 22), Theme.Primary);
            using (var font = Theme.Font(14f, FontStyle.Bold))
            {
                Painter.Text(g, "AI 课堂助手", new RectangleF(logo.X + 32, logo.Y, logo.Width - 36, logo.Height),
                    font, Theme.Primary, ContentAlignment.MiddleLeft);
            }

            // 动态标题
            string title = _shell._current is null ? "" : $"· {_shell._current.View.Title}";
            using (var font = Theme.Font(15f, FontStyle.Bold))
            {
                Painter.Text(g, title, new RectangleF(logo.Right + 12, 12, 420, 32),
                    font, Color.White, ContentAlignment.MiddleLeft);
            }

            // 右上状态徽章
            _badge = new Rectangle(rect.Right - 250, 18, 96, 20);
            using (var font = Theme.Font(12f))
            {
                Painter.Text(g, "就绪", new RectangleF(_badge.X + 16, _badge.Y, _badge.Width - 16, _badge.Height),
                    font, Color.FromArgb(0xD8, 0xF3, 0xEE), ContentAlignment.MiddleLeft);
            }
            Painter.Dot(g, _badge.X + 6, _badge.Y + _badge.Height / 2f, 4f, Theme.Accent);

            // 最小化 / 关闭
            _minBtn = new Rectangle(rect.Right - 100, 13, 34, 30);
            _closeBtn = new Rectangle(rect.Right - 54, 13, 34, 30);
            DrawChromeButton(g, _minBtn, IconSet.Minimize, false);
            DrawChromeButton(g, _closeBtn, IconSet.Close, true);
            AddHit(_minBtn, () => _shell.WindowState = FormWindowState.Minimized,
                () => Invalidate());
            AddHit(_closeBtn, () => _shell.Hide(), () => Invalidate());

            // tab 栏
            Rectangle tabArea = new(rect.X, headerH, rect.Width, rect.Height - headerH);
            using (var brush = new SolidBrush(Theme.Card))
            {
                g.FillRectangle(brush, tabArea);
            }

            int x = 22;
            foreach (Entry e in _shell._entries)
            {
                int w = (int)Math.Max(64, Painter.MeasureText(g, e.TabText, Theme.SubFont) + 22);
                Rectangle tabRect = new(x, headerH, w, tabArea.Height);
                bool active = ReferenceEquals(e, _shell._current);
                bool hover = IsHovered(tabRect);
                Rectangle captured = tabRect;
                AddHit(captured, () => _shell.ShowView(e.Key), () => Invalidate());

                using var font = Theme.Font(13f, active || hover ? FontStyle.Bold : FontStyle.Regular);
                Painter.Text(g, e.TabText, new RectangleF(tabRect.X, tabRect.Y, tabRect.Width, tabRect.Height - 2),
                    font, active ? Theme.Primary : Theme.Text2, ContentAlignment.MiddleCenter);

                if (active)
                {
                    using var underline = new SolidBrush(Theme.Primary);
                    g.FillRectangle(underline, tabRect.X + 10, tabRect.Bottom - 3, tabRect.Width - 20, 3);
                }

                x += w + 6;
            }

            using var divider = new Pen(Theme.Border, 1f);
            g.DrawLine(divider, rect.X, rect.Bottom - 1, rect.Right, rect.Bottom - 1);
        }

        private void DrawChromeButton(Graphics g, Rectangle r, Action<Graphics, Rectangle, Color, float> icon, bool danger)
        {
            bool hover = IsHovered(r);
            if (hover)
            {
                Painter.RoundRect(g, r, 6, danger ? Theme.Danger : Color.FromArgb(30, 255, 255, 255));
            }

            icon(g, new Rectangle(r.X + 9, r.Y + 9, r.Width - 18, r.Height - 18),
                hover && danger ? Color.White : Color.FromArgb(0xD8, 0xF3, 0xEE), 1.7f);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            bool onButton = _minBtn.Contains(e.Location) || _closeBtn.Contains(e.Location);
            if (e.Location.Y < Theme.HeaderHeight && !onButton)
            {
                ReleaseCapture();
                SendMessage(_shell.Handle, WmNcLButtonDown, (IntPtr)HtCaption, IntPtr.Zero);
                return;
            }

            base.OnMouseDown(e);
        }
    }

    // ---------- 底部状态条 ----------
    private sealed class ShellStatusBar : Canvas
    {
        public string LeftText { get; set; } = "监控  开启";
        public string CenterText { get; set; } = "存储：导出目录就绪";
        public string RightText { get; set; } = "隧道  未连接";

        protected override void Draw(Graphics g, Rectangle rect)
        {
            ClearHits();
            Painter.Text(g, LeftText, new RectangleF(rect.X + 18, rect.Y, 260, rect.Height),
                Theme.SubFont, Theme.Text2, ContentAlignment.MiddleLeft);
            Painter.Text(g, CenterText, new RectangleF(rect.X + 320, rect.Y, rect.Width - 640, rect.Height),
                Theme.SubFont, Theme.Text2, ContentAlignment.MiddleCenter);
            Painter.Text(g, RightText, new RectangleF(rect.Right - 260, rect.Y, 242, rect.Height),
                Theme.SubFont, Theme.Text2, ContentAlignment.MiddleRight);
            Painter.Dot(g, rect.X + 12, rect.Y + rect.Height / 2f, 4f, Theme.Green);
            Painter.Dot(g, rect.Right - 272, rect.Y + rect.Height / 2f, 4f, Theme.Green);
        }
    }
}
