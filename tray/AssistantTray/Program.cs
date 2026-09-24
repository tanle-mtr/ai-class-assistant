using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using Microsoft.Win32;
using System.Net.Http;

namespace AssistantTray;

/// <summary>
/// AI 课堂助手 - 托盘常驻程序
/// - 开机自启（注册表 HKCU Run）
/// - 启动/守护 Python 核心与 ClassIsland 桥
/// - 托盘菜单：打开控制台 / 导出文件夹 / 自启开关 / 退出
/// </summary>
internal static class Program
{
    private const string AppName = "AI课堂助手";
    private const string MutexName = "AIClassroomAssistant.Tray";
    private const string RunKeyName = "AIClassroomAssistant";

    private static readonly string ExeDir = AppContext.BaseDirectory;
    private static readonly string ProjectRoot = Path.GetFullPath(Path.Combine(ExeDir, "..", "..", "..", "..", ".."));

    private static Process? _coreProc;
    private static Process? _bridgeProc;

    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(true, MutexName, out bool createdNew);
        if (!createdNew)
        {
            // 已有实例在跑：静默退出（旧实例可能正被用户使用）
            return;
        }
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new TrayContext());
    }

    /// <summary>启动 Python 核心（未运行时）</summary>
    internal static void StartCore()
    {
        if (_coreProc is { HasExited: false }) return;
        try
        {
            if (!File.Exists(Path.Combine(ProjectRoot, "assistant_core", "main.py")))
            {
                TrayContext.ShowTip("未找到 assistant_core/main.py");
                return;
            }
            // 优先使用打包版核心（无 Python 依赖、无控制台窗口）；否则用 python -m
            var packedExe = Path.Combine(ProjectRoot, "dist", "assistant-core", "assistant-core.exe");
            ProcessStartInfo psi;
            if (File.Exists(packedExe))
            {
                psi = new ProcessStartInfo(packedExe)
                {
                    WorkingDirectory = ProjectRoot,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
            }
            else
            {
                var python = FindPython();
                psi = new ProcessStartInfo(python, "-m assistant_core.main")
                {
                    WorkingDirectory = ProjectRoot,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
            }
            _coreProc = Process.Start(psi);
            if (_coreProc != null)
                TrayContext.ShowTip("AI 课堂助手核心已启动");
        }
        catch (Exception e)
        {
            TrayContext.ShowTip($"核心启动失败: {e.Message}");
        }
    }

    /// <summary>启动 ClassIsland 桥（未运行时）</summary>
    internal static void StartBridge()
    {
        if (_bridgeProc is { HasExited: false }) return;
        // 桥端口已有实例在服务（/ping 响应）则跳过，避免反复拉起冲突实例
        try
        {
            using var hc = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            var resp = hc.GetStringAsync("http://127.0.0.1:18761/ping").GetAwaiter().GetResult();
            if (resp.Contains("ok", StringComparison.OrdinalIgnoreCase)) return;
        }
        catch { }
        var bridgeExe = Path.Combine(ProjectRoot, "tray", "ClassIslandBridge", "bin", "Release", "net8.0-windows", "ClassIslandBridge.exe");
        var full = Path.GetFullPath(bridgeExe);
        if (!File.Exists(full))
        {
            // 也检查发布目录
            var alt = Path.Combine(ExeDir, "ClassIslandBridge.exe");
            if (!File.Exists(alt)) return;
            full = alt;
        }
        try
        {
            var psi = new ProcessStartInfo(full)
            {
                WorkingDirectory = Path.GetDirectoryName(full)!,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            _bridgeProc = Process.Start(psi);
        }
        catch
        {
            // 桥不可用时核心会自动退回文件监听
        }
    }

    internal static void StopCore()
    {
        try { if (_coreProc is { HasExited: false }) _coreProc.Kill(); } catch { }
        try { if (_bridgeProc is { HasExited: false }) _bridgeProc.Kill(); } catch { }
    }

    private static string FindPython()
    {
        var env = Environment.GetEnvironmentVariable("AI_ASSISTANT_PYTHON");
        if (!string.IsNullOrEmpty(env) && File.Exists(env)) return env;
        foreach (var name in new[] { "pythonw", "python" })
        {
            var found = FindOnPath(name);
            if (found != null) return found;
        }
        return "python"; // 交给系统解析
    }

    private static string? FindOnPath(string exe)
    {
        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? "";
        string? first = null;
        foreach (var dir in pathVar.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var full = Path.Combine(dir.Trim(), exe + ".exe");
            if (!File.Exists(full)) continue;
            // 优先使用 Doubao sandbox 解释器：自带全部依赖（fastapi/uvicorn/opencv/faster-whisper）
            if (full.Contains("sandbox_runtime", StringComparison.OrdinalIgnoreCase)) return full;
            first ??= full;
        }
        return first;
    }

    /// <summary>开机自启开关</summary>
    internal static bool IsAutostartEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        return key?.GetValue(RunKeyName) != null;
    }

    internal static void SetAutostart(bool enable)
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
        if (key == null) return;
        if (enable)
            key.SetValue(RunKeyName, $"\"{Application.ExecutablePath}\"");
        else
            key.DeleteValue(RunKeyName, false);
    }

    internal static void OpenConsole()
    {
        var port = FindWebPort();
        try
        {
            Process.Start(new ProcessStartInfo($"http://127.0.0.1:{port}") { UseShellExecute = true });
        }
        catch
        {
            TrayContext.ShowTip("控制台可能尚未就绪，请稍后再试");
        }
    }

    /// <summary>从 data/config/config.json 读取实际 web 端口（可能被顺延）</summary>
    private static int ReadWebPort()
    {
        try
        {
            var cfg = Path.Combine(ProjectRoot, "data", "config", "config.json");
            if (File.Exists(cfg))
            {
                using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(cfg));
                if (doc.RootElement.TryGetProperty("web_port", out var p) && p.TryGetInt32(out var port))
                    return port;
            }
        }
        catch { }
        return 18760;
    }

    internal static void OpenExports()
    {
        var dir = Path.Combine(ProjectRoot, "data", "exports");
        Directory.CreateDirectory(dir);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
    }

    /// <summary>探测核心 Web 端口：先验证 config 端口，再并发扫 18762-18777，最快响应，绝不长时间卡顿</summary>
    internal static int FindWebPort()
    {
        var cfgPort = ReadWebPort();
        if (cfgPort != 18760 && Probe(cfgPort)) return cfgPort;

        var ports = new List<int>();
        for (int p = 18762; p <= 18777; p++) ports.Add(p);
        for (int i = 0; i < ports.Count; i += 6)
        {
            var batch = ports.Skip(i).Take(6)
                .Select(p => Task.Run(() => Probe(p) ? p : -1)).ToArray();
            Task.WaitAll(batch, 4000);
            var hit = batch.Select(t => t.Result).FirstOrDefault(x => x > 0);
            if (hit > 0) return hit;
        }
        return cfgPort != 18760 ? cfgPort : 18765;
    }

    private static bool Probe(int port)
    {
        try
        {
            using var hc = new HttpClient { Timeout = TimeSpan.FromMilliseconds(1500) };
            var resp = hc.GetStringAsync($"http://127.0.0.1:{port}/api/health").GetAwaiter().GetResult();
            return resp.Contains("ok", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }
}

internal class TrayContext : ApplicationContext
{
    private readonly NotifyIcon _icon;
    private MainPanel? _panel;

    public TrayContext()
    {
        _icon = new NotifyIcon
        {
            Icon = ExtractAppIcon(),
            Text = "AI 课堂助手 · 运行中",
            Visible = true,
            ContextMenuStrip = BuildMenu(),
        };
        // 单击 / 双击托盘图标：弹出原生桌面主面板（效果图 01，不依赖浏览器）
        _icon.Click += (_, _) => OpenNativePanel();
        _icon.DoubleClick += (_, _) => OpenNativePanel();

        // 开机自启（第一次运行自动注册）
        if (!Program.IsAutostartEnabled())
            Program.SetAutostart(true);

        Program.StartBridge();
        Program.StartCore();

        // 周期自愈：桥/核心意外退出后自动重启（每 20 秒检查一次）
        var healTimer = new System.Windows.Forms.Timer { Interval = 20000 };
        healTimer.Tick += (_, _) =>
        {
            Program.StartBridge();
            Program.StartCore();
        };
        healTimer.Start();
    }

    /// <summary>打开原生桌面主面板（效果图 01，无边框浮层，不依赖浏览器）</summary>
    private void OpenNativePanel()
    {
        if (_panel is { IsDisposed: true }) _panel = null;
        if (_panel == null)
        {
            _panel = new MainPanel { TopMost = true, StartPosition = FormStartPosition.Manual };
            if (Screen.PrimaryScreen is { } primary)
            {
                var wa = primary.WorkingArea;
                _panel.Location = new System.Drawing.Point(
                    wa.Right - _panel.Width - 24, wa.Bottom - _panel.Height - 48);
            }
        }
        _panel.Show();
        _panel.BringToFront();
        _panel.Activate();
    }

    /// <summary>打开网页版主面板（panel.html，经核心端口伺服）</summary>
    private void OpenPanelWeb()
    {
        var port = Program.FindWebPort();
        var url = $"http://127.0.0.1:{port}/panel.html";
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();

        menu.Items.Add("打开主面板", null, (_, _) => OpenNativePanel());
        menu.Items.Add("打开网页面板", null, (_, _) => OpenPanelWeb());
        menu.Items.Add("打开控制台", null, (_, _) => Program.OpenConsole());
        menu.Items.Add("打开导出文件夹", null, (_, _) => Program.OpenExports());
        menu.Items.Add("重启核心", null, (_, _) =>
        {
            Program.StopCore();
            Program.StartCore();
        });
        menu.Items.Add(new ToolStripSeparator());

        var autostart = Program.IsAutostartEnabled();
        menu.Items.Add(autostart ? "开机自启：已开启 ✓" : "开机自启：已关闭");
        var toggle = menu.Items.Add(autostart ? "关闭开机自启" : "开启开机自启");
        toggle.Click += (_, _) =>
        {
            Program.SetAutostart(!Program.IsAutostartEnabled());
            ShowTip(Program.IsAutostartEnabled() ? "开机自启已开启" : "开机自启已关闭");
            _icon.ContextMenuStrip = BuildMenu(); // 刷新菜单
        };

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) =>
        {
            Program.StopCore();
            if (_panel is { IsDisposed: false }) { _panel.Close(); _panel.Dispose(); }
            _icon.Visible = false;
            Application.Exit();
        });
        return menu;
    }

    /// <summary>从程序集资源提取应用图标（嵌入的 app.ico）</summary>
    internal static Icon ExtractAppIcon()
    {
        try
        {
            return Icon.ExtractAssociatedIcon(Application.ExecutablePath)!;
        }
        catch
        {
            return SystemIcons.Application;
        }
    }

    public static void ShowTip(string text)
    {
        // 静默提示：仅写日志由核心处理；此处不做弹窗打扰
        System.Diagnostics.Debug.WriteLine(text);
    }
}
