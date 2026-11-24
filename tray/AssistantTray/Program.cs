using System.Diagnostics;
using System.Drawing;
using System.Net.Http;
using System.Windows.Forms;
using Microsoft.Win32;

namespace AssistantTray;

/// <summary>
/// AI 课堂助手托盘入口：
/// - NotifyIcon 单击/双击弹出原生主面板（效果图 01），右键菜单提供网页版入口
/// - 首次运行注册开机自启（HKCU Run）
/// - 守护 Python 核心与 ClassIsland 桥，每 20 秒自愈
/// - 退出时先停自己的自愈定时器与面板，再请求核心优雅停机，最后结束子进程
/// </summary>
internal static class Program
{
    private const string MutexName = "AIClassroomAssistant.Tray.v2";
    private const string RunKeyName = "AIClassroomAssistant";
    private const int BridgeHealthPort = 18761;

    private static readonly string ExeDir = AppContext.BaseDirectory;
    internal static readonly string ProjectRoot = PortFinder.ProjectRoot;

    private static Process? _coreProc;
    private static Process? _bridgeProc;
    private static volatile bool _exiting;

    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(true, MutexName, out var createdNew);
        if (!createdNew) return; // 已有实例在运行：静默退出

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new TrayContext());
    }

    // ---------------- 子进程守护 ----------------

    /// <summary>启动 Python 核心：优先打包版 assistant-core.exe，否则 python -m assistant_core.cli。</summary>
    internal static void StartCore()
    {
        if (_exiting || _coreProc is { HasExited: false }) return;
        try
        {
            var packed = new[]
            {
                Path.Combine(ProjectRoot, "dist", "assistant-core", "assistant-core.exe"),
                Path.Combine(ProjectRoot, "assistant-core.exe"),
            }.FirstOrDefault(File.Exists);

            ProcessStartInfo psi;
            if (packed is not null)
            {
                psi = new ProcessStartInfo(packed)
                {
                    WorkingDirectory = ProjectRoot,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
            }
            else
            {
                psi = new ProcessStartInfo(FindPython(), "-m assistant_core.cli")
                {
                    WorkingDirectory = ProjectRoot,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
            }
            _coreProc = Process.Start(psi);
            ShowTip("AI 课堂助手核心已启动");
        }
        catch (Exception e)
        {
            ShowTip($"核心启动失败：{e.Message}");
        }
    }

    /// <summary>启动 ClassIsland 桥；若 18761 已有健康服务则不重复拉起。</summary>
    internal static void StartBridge()
    {
        if (_exiting || _bridgeProc is { HasExited: false }) return;
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(1) };
            var health = http.GetStringAsync($"http://127.0.0.1:{BridgeHealthPort}/api/health")
                .GetAwaiter().GetResult();
            if (health.Contains("ok", StringComparison.OrdinalIgnoreCase)) return;
        }
        catch
        {
            // 桥尚未启动，继续拉起
        }

        var candidates = new[]
        {
            Path.Combine(ProjectRoot, "tray", "ClassIslandBridge", "bin", "Release",
                "net8.0-windows", "ClassIslandBridge.exe"),
            Path.Combine(ExeDir, "ClassIslandBridge.exe"),
        };
        var exe = candidates.FirstOrDefault(File.Exists);
        if (exe is null) return;

        try
        {
            _bridgeProc = Process.Start(new ProcessStartInfo(exe)
            {
                WorkingDirectory = Path.GetDirectoryName(exe)!,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
        }
        catch (Exception e)
        {
            ShowTip($"桥启动失败：{e.Message}");
        }
    }

    /// <summary>优雅关闭子进程：先核心（通知其停感知/导出），再桥。</summary>
    private static void StopChildren()
    {
        try
        {
            if (_coreProc is { HasExited: false })
            {
                var api = new ApiClient();
                api.Shutdown();
                if (!_coreProc.WaitForExit(3000)) _coreProc.Kill();
            }
        }
        catch
        {
            try { if (_coreProc is { HasExited: false }) _coreProc.Kill(); } catch { }
        }

        // 桥自身有 20 秒自愈定时器，必须在托盘的自愈定时器停止后再关闭，
        // 否则会出现「刚杀掉又被拉活」。
        try { if (_bridgeProc is { HasExited: false }) _bridgeProc.Kill(); } catch { }
    }

    private static string FindPython()
    {
        var env = Environment.GetEnvironmentVariable("AI_ASSISTANT_PYTHON");
        if (!string.IsNullOrEmpty(env) && File.Exists(env)) return env;

        foreach (var name in new[] { "pythonw", "python" })
        {
            var found = FindOnPath(name);
            if (found is not null) return found;
        }
        return "python";
    }

    private static string? FindOnPath(string exe)
    {
        var paths = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        string? first = null;
        foreach (var dir in paths.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var full = Path.Combine(dir.Trim(), exe + ".exe");
            if (!File.Exists(full)) continue;
            if (full.Contains("sandbox_runtime", StringComparison.OrdinalIgnoreCase)) return full;
            first ??= full;
        }
        return first;
    }

    // ---------------- 开机自启 ----------------

    internal static bool IsAutostartEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        return key?.GetValue(RunKeyName) is not null;
    }

    private static void EnsureAutostart()
    {
        if (IsAutostartEnabled()) return; // 首次运行才注册；退出时保留用户设置
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run", true);
            key?.SetValue(RunKeyName, $"\"{Application.ExecutablePath}\"");
        }
        catch
        {
            // 注册表不可写时静默跳过
        }
    }

    // ---------------- 浏览器入口 ----------------

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception e)
        {
            ShowTip($"打开网页失败：{e.Message}");
        }
    }

    internal static void OpenPanelWeb() => OpenUrl($"http://127.0.0.1:{PortFinder.Find()}/panel.html");

    internal static void OpenConsole() => OpenUrl($"http://127.0.0.1:{PortFinder.Find()}/");

    internal static void ShowTip(string text) => System.Diagnostics.Debug.WriteLine(text);

    // ---------------- 托盘上下文 ----------------

    private sealed class TrayContext : ApplicationContext
    {
        private readonly NotifyIcon _icon;
        private readonly System.Windows.Forms.Timer _healTimer = new() { Interval = 20000 };

        internal TrayContext()
        {
            _icon = new NotifyIcon
            {
                Icon = LoadIcon(),
                Text = "AI 课堂助手 · 运行中",
                Visible = true,
                ContextMenuStrip = BuildMenu(),
            };
            _icon.Click += (_, _) => MainPanel.Toggle();
            _icon.DoubleClick += (_, _) => MainPanel.Toggle();

            EnsureAutostart();
            StartBridge();
            StartCore();

            // 每 20 秒自愈：子进程意外退出后拉起。退出时第一步就是停掉它。
            _healTimer.Tick += (_, _) =>
            {
                if (_exiting) return;
                StartBridge();
                StartCore();
            };
            _healTimer.Start();
        }

        private static Icon LoadIcon()
        {
            try
            {
                var path = Path.Combine(ProjectRoot, "icons", "app.ico");
                if (File.Exists(path)) return new Icon(path);
                return Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
            }
            catch
            {
                return SystemIcons.Application;
            }
        }

        private ContextMenuStrip BuildMenu()
        {
            var menu = new ContextMenuStrip();
            menu.Items.Add("打开主面板", null, (_, _) => MainPanel.Toggle());
            menu.Items.Add("打开网页面板", null, (_, _) => OpenPanelWeb());
            menu.Items.Add("打开控制台", null, (_, _) => OpenConsole());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("退出", null, (_, _) => ExitGracefully());
            return menu;
        }

        private void ExitGracefully()
        {
            if (_exiting) return;
            _exiting = true;

            // 顺序至关重要：先停托盘自身（自愈定时器 + 面板），避免子进程被重新拉活
            _healTimer.Stop();
            _healTimer.Dispose();
            MainPanel.ShutdownPanel();

            // 再请求核心优雅停机并结束子进程
            StopChildren();

            _icon.Visible = false;
            _icon.Dispose();
            ExitThread();
        }
    }
}
