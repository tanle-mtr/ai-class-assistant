using System.Runtime.InteropServices;
using AiClassAssistant.Ui.Views;

namespace AiClassAssistant.App;

/// <summary>
/// 承载 GDI+ 主面板视图的无边框原生窗口（效果图 01）。
/// 主面板本身是 ViewBase/Control，需要一个 Form 容器才能独立弹窗。
/// </summary>
public sealed class PanelWindow : Form
{
    private readonly MainPanelView _view;

    public PanelWindow(MainPanelView view)
    {
        _view = view;
        Text = "AI 课堂助手";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Size = new Size(1080, 648);
        BackColor = Color.White;
        ShowInTaskbar = false;
        TopMost = true;
        Controls.Add(_view);
        _view.Dock = DockStyle.Fill;
        _view.Visible = true;
    }

    /// <summary>定位到指定屏幕工作区右下角。</summary>
    public void PlaceAtBottomRight(Screen screen)
    {
        var area = screen.WorkingArea;
        Location = new Point(area.Right - Width - 24, area.Bottom - Height - 48);
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        // 主面板失焦即隐藏（托盘常驻风格）
        Hide();
    }

    protected override void WndProc(ref Message m)
    {
        // 允许通过顶栏拖动窗口：Canvas 命中顶栏时会发 WM_NCLBUTTONDOWN
        if (m.Msg == 0x00A1 && m.WParam.ToInt32() == 0x0002) // WM_NCLBUTTONDOWN / HTCAPTION
        {
            ReleaseCapture();
            SendMessage(Handle, 0x00A1, new IntPtr(0x0002), IntPtr.Zero);
            return;
        }

        base.WndProc(ref m);
    }

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
}
