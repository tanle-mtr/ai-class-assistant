using System.Net;
using System.Text.Json;
using ClassIsland.Shared.IPC;
using ClassIsland.Shared.IPC.Abstractions.Services;
using dotnetCampus.Ipc.CompilerServices.GeneratedProxies;
using dotnetCampus.Ipc.Pipes;

namespace ClassIslandBridge;

/// <summary>
/// ClassIsland IPC 桥：
/// 通过官方 IPC（dotnetCampus.Ipc 命名管道）连接 ClassIsland，
/// 读取当前课程状态（科目/状态/下课剩余时间）并订阅上下课事件，
/// 以本地 HTTP 服务（127.0.0.1:18761）提供给 Python 核心。
/// </summary>
public static class Program
{
    private static IpcClient? _client;
    private static readonly object Lock = new();
    private static string _state = "Unknown";
    private static string _subject = "";
    private static string _className = "";
    private static int _leftTime;
    private static string _currentTime = "";

    public static void Main(string[] args)
    {
        try { MainInner(args); }
        catch (Exception e)
        {
            try { System.IO.File.AppendAllText(
                System.IO.Path.Combine(AppContext.BaseDirectory, "bridge_error.log"),
                $"[{DateTime.Now:HH:mm:ss}] {e}\n"); } catch { }
            Console.WriteLine($"[ClassIslandBridge] 致命异常: {e}");
        }
    }

    private static void MainInner(string[] args)
    {
        Console.WriteLine("[ClassIslandBridge] 启动，连接 ClassIsland IPC…");

        // 1. 连接 ClassIsland IPC（命名管道，若 ClassIsland 未运行则自动等待重连）
        _client = new IpcClient();
        _client.JsonIpcProvider.AddNotifyHandler(IpcRoutedNotifyIds.CurrentTimeStateChangedNotifyId, Refresh);
        _client.JsonIpcProvider.AddNotifyHandler(IpcRoutedNotifyIds.OnClassNotifyId, Refresh);
        _client.JsonIpcProvider.AddNotifyHandler(IpcRoutedNotifyIds.OnBreakingTimeNotifyId, Refresh);
        _client.JsonIpcProvider.AddNotifyHandler(IpcRoutedNotifyIds.OnAfterSchoolNotifyId, Refresh);
        _ = _client.Connect();

        // 2. 每秒轮询刷新状态
        using var timer = new System.Threading.Timer(_ => Refresh(), null, 0, 1000);

        // 3. 本地 HTTP 服务
        var port = Environment.GetEnvironmentVariable("BRIDGE_PORT") ?? "18761";
        var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();
        Console.WriteLine($"[ClassIslandBridge] HTTP 服务就绪: http://127.0.0.1:{port}/state");

        while (true)
        {
            try
            {
                var ctx = listener.GetContext();
                _ = Task.Run(() => Handle(ctx));
            }
            catch (Exception e)
            {
                Console.WriteLine($"[ClassIslandBridge] HTTP 异常: {e.Message}");
            }
        }
    }

    private static void Refresh()
    {
        if (_client?.PeerProxy == null) return;
        try
        {
            var lessons = _client.Provider.CreateIpcProxy<IPublicLessonsService>(_client.PeerProxy!);
            if (lessons == null) return;
            lock (Lock)
            {
                _subject = lessons.CurrentSubject?.Name ?? "";
                _state = lessons.CurrentState.ToString() ?? "Unknown";
                _className = Safe(() => lessons.CurrentClassPlan?.Name ?? "");
                _leftTime = (int)(SafeTimeSpan(lessons.OnClassLeftTime).TotalSeconds);
                _currentTime = DateTime.Now.ToString("HH:mm:ss");
            }
        }
        catch (Exception e)
        {
            // ClassIsland 未运行/断开：保持上次状态
            Console.WriteLine($"[ClassIslandBridge] IPC 读取失败: {e.Message}");
        }
    }

    private static string Safe(Func<string> getter)
    {
        try { return getter(); } catch { return ""; }
    }

    private static TimeSpan SafeTimeSpan(TimeSpan value)
    {
        try { return value; } catch { return TimeSpan.Zero; }
    }

    private static void Handle(HttpListenerContext ctx)
    {
        try
        {
            var path = ctx.Request.Url?.AbsolutePath ?? "/";
            if (path == "/ping")
            {
                WriteJson(ctx, new { ok = true });
                return;
            }
            if (path == "/state")
            {
                lock (Lock)
                {
                    WriteJson(ctx, new
                    {
                        state = _state,
                        subject = _subject,
                        class_name = _className,
                        on_class_left_time = _leftTime,
                        current_time = _currentTime,
                    });
                }
                return;
            }
            WriteJson(ctx, new { error = "not found" }, 404);
        }
        catch
        {
            // 忽略单请求异常
        }
        finally
        {
            try { ctx.Response.Close(); } catch { }
        }
    }

    private static void WriteJson(HttpListenerContext ctx, object obj, int code = 200)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(obj));
        ctx.Response.StatusCode = code;
        ctx.Response.ContentType = "application/json; charset=utf-8";
        ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
    }
}
