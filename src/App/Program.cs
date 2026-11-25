using System.Diagnostics;
using System.Reflection;
using AiClassAssistant.Core.Configuration;
using AiClassAssistant.Core.Services;
using AiClassAssistant.Ui.Views;

namespace AiClassAssistant.App;

/// <summary>
/// v3 全原生单程序入口：托盘常驻 + 原生 GDI+ 主面板 + 控制台。
/// 无 HTTP 服务、无浏览器、无 HTML。
/// </summary>
internal static class Program
{
    private static Mutex? _mutex;
    private static NotifyIcon? _tray;
    private static AppController? _app;
    private static Process? _bridge;

    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        // 单实例：已有进程则直接唤起主面板
        _mutex = new Mutex(true, "AiClassAssistant.v3.SingleInstance", out var createdNew);
        if (!createdNew)
        {
            NativeMethods.BringExistingToFront();
            return;
        }

        // 离线导出模式：只归档未归类数据，不起设备与界面
        if (Environment.GetCommandLineArgs().Any(a => a.Equals("--export-only", StringComparison.OrdinalIgnoreCase)))
        {
            new Exporter(AppConfig.Instance).ExportUnclassified();
            return;
        }

        try
        {
            StartBridge();
            _app = new AppController();
            CreateTray();
            _app.Start();

            var args = Environment.GetCommandLineArgs();
            if (args.Any(a => a.Equals("--show-console", StringComparison.OrdinalIgnoreCase)))
            {
                _app.ShowConsole();
            }
            else if (args.Any(a => a.Equals("--show-panel", StringComparison.OrdinalIgnoreCase)))
            {
                _app.ShowPanel();
            }

            Application.Run();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"启动失败：{ex.Message}", "AI 课堂助手", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Cleanup();
            _mutex.ReleaseMutex();
            _mutex.Dispose();
        }
    }

    private static void CreateTray()
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "app.ico");
        Icon? icon = File.Exists(iconPath)
            ? new Icon(iconPath)
            : SystemIcons.Application;

        var menu = new ContextMenuStrip();
        menu.Items.Add("打开主面板", null, (_, _) => _app?.ShowPanel());
        menu.Items.Add("打开控制台", null, (_, _) => _app?.ShowConsole());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("生成总结", null, (_, _) => _app?.ShowConsole("summaries"));
        menu.Items.Add("设置", null, (_, _) => _app?.ShowConsole("settings"));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => Application.ExitThread());

        _tray = new NotifyIcon
        {
            Icon = icon!,
            Text = "AI 课堂助手",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _tray.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                _app?.ShowPanel();
            }
        };
        _tray.DoubleClick += (_, _) => _app?.ShowConsole();
    }

    private static void StartBridge()
    {
        try
        {
            var exe = Path.Combine(AppContext.BaseDirectory, "AiClassAssistant.Bridge.exe");
            if (!File.Exists(exe))
            {
                return;
            }

            _bridge = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = exe,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = AppContext.BaseDirectory,
                },
            };
            _bridge.Start();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"桥启动失败（不影响主程序）: {ex.Message}");
        }
    }

    private static void Cleanup()
    {
        if (_tray is not null)
        {
            _tray.Visible = false;
            _tray.Dispose();
        }

        _app?.Shutdown();
        _app?.Dispose();

        // 先停托盘再停桥：托盘自愈定时器会拉活桥
        if (_bridge is not null && !_bridge.HasExited)
        {
            try
            {
                _bridge.Kill(entireProcessTree: true);
            }
            catch
            {
                // 已退出
            }
        }
    }
}

/// <summary>原生窗口消息，用于唤起已有实例。</summary>
internal static class NativeMethods
{
    private const int WM_USER = 0x0400;

    public static void BringExistingToFront()
    {
        try
        {
            var current = Process.GetCurrentProcess();
            foreach (var p in Process.GetProcessesByName(current.ProcessName))
            {
                if (p.Id == current.Id)
                {
                    continue;
                }

                var handle = p.MainWindowHandle;
                if (handle != IntPtr.Zero)
                {
                    ShowWindow(handle, 9); // SW_RESTORE
                    SetForegroundWindow(handle);
                }
            }
        }
        catch
        {
            // 忽略
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
}
