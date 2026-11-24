using System.Collections;
using System.Net;
using System.Text;
using System.Text.Json;
using ClassIsland.Shared.IPC;
using ClassIsland.Shared.IPC.Abstractions.Services;
using dotnetCampus.Ipc.CompilerServices.GeneratedProxies;

namespace ClassIslandBridge;

/// <summary>
/// ClassIsland IPC 桥：
/// 通过 ClassIsland.Shared.IPC（dotnetCampus.Ipc 命名管道）订阅上课/下课/放学事件，
/// 把当前课程状态与今日课表暴露为本地 HTTP 服务 127.0.0.1:18761，供 Python 核心轮询。
///
/// 契约端点：GET /api/health、GET /api/state、GET /api/schedule。
/// </summary>
internal static class Program
{
    private const int BridgePort = 18761;
    private static readonly object Gate = new();

    private static IpcClient? _client;
    private static string _state = "None";
    private static string _subject = string.Empty;
    private static string _start = string.Empty;
    private static string _end = string.Empty;
    private static string _className = string.Empty;
    private static long _ts;
    private static string _scheduleDate = string.Empty;
    private static ScheduleItem[] _schedule = Array.Empty<ScheduleItem>();
    private static bool _ipcReady;

    private sealed record ScheduleItem(string Subject, string Start, string End, string Type);

    private static void Main()
    {
        try
        {
            Run();
        }
        catch (Exception e)
        {
            try
            {
                File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "bridge_error.log"),
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {e}\n");
            }
            catch
            {
                // 日志不可写时忽略
            }
            Console.WriteLine($"[ClassIslandBridge] 致命异常：{e}");
        }
    }

    private static void Run()
    {
        Console.WriteLine("[ClassIslandBridge] 启动，连接 ClassIsland IPC…");

        _client = new IpcClient();
        RegisterHandlers(_client);
        _ = _client.Connect();

        // 每秒刷新课程状态，每 30 秒刷新当天课表
        using var refreshTimer = new Timer(_ => Refresh(), null, 0, 1000);
        using var scheduleTimer = new Timer(_ => RefreshSchedule(), null, 0, 30000);

        // 20 秒自愈保活：IPC 断线时 dotnetCampus.Ipc 会自动重连，
        // 这里额外检查 PeerProxy 并补一次 Connect；进程级拉活由托盘的 20 秒守护定时器负责。
        using var healTimer = new Timer(_ => Heal(), null, 20000, 20000);

        var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{BridgePort}/");
        listener.Start();
        Console.WriteLine($"[ClassIslandBridge] HTTP 就绪：http://127.0.0.1:{BridgePort}/api/state");

        while (true)
        {
            try
            {
                var context = listener.GetContext();
                _ = Task.Run(() => Handle(context));
            }
            catch (Exception e)
            {
                Console.WriteLine($"[ClassIslandBridge] HTTP 异常：{e.Message}");
                LoadConfigFallback(); // IPC 不可用时进入配置兜底状态
            }
        }
    }

    private static void RegisterHandlers(IpcClient client)
    {
        client.JsonIpcProvider.AddNotifyHandler(IpcRoutedNotifyIds.CurrentTimeStateChangedNotifyId, Refresh);
        client.JsonIpcProvider.AddNotifyHandler(IpcRoutedNotifyIds.OnClassNotifyId, Refresh);
        client.JsonIpcProvider.AddNotifyHandler(IpcRoutedNotifyIds.OnBreakingTimeNotifyId, Refresh);
        client.JsonIpcProvider.AddNotifyHandler(IpcRoutedNotifyIds.OnAfterSchoolNotifyId, Refresh);
    }

    private static void Heal()
    {
        try
        {
            if (_client is not null && _client.PeerProxy is null)
            {
                Console.WriteLine("[ClassIslandBridge] IPC 断线，尝试重连…");
                _ = _client.Connect();
            }
        }
        catch
        {
            // 重连失败时保持兜底状态，等待下一个 20 秒周期
        }
    }

    // ---------------- 数据采集 ----------------

    private static void Refresh()
    {
        var lessons = CreateLessonsProxy();
        if (lessons is null)
        {
            LoadConfigFallback();
            return;
        }

        try
        {
            var rawState = Safe(() => lessons.CurrentState.ToString() ?? string.Empty);
            var state = MapState(rawState);
            var subject = Safe(() => lessons.CurrentSubject?.Name ?? string.Empty);
            var className = Safe(() => lessons.CurrentClassPlan?.Name ?? string.Empty);
            var item = SafeObject(() => lessons.CurrentTimeLayoutItem);
            var start = SecondsToClock(ReadInt(item, "StartSecond", "Start"));
            var end = SecondsToClock(ReadInt(item, "EndSecond", "End"));

            lock (Gate)
            {
                _state = state;
                _subject = subject;
                _className = className;
                _start = start;
                _end = end;
                _ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                _ipcReady = true;
            }
        }
        catch
        {
            // IPC 读取失败：保持上一帧状态，避免抖动
        }
    }

    private static void RefreshSchedule()
    {
        var lessons = CreateLessonsProxy();
        if (lessons is null) return;
        try
        {
            var plan = lessons.GetClassPlanByDate(DateTime.Today);
            if (plan is null) return;
            var classes = plan.GetType().GetProperty("Classes")?.GetValue(plan) as IEnumerable;
            if (classes is null) return;

            var list = new List<ScheduleItem>();
            foreach (var classInfo in classes)
            {
                if (classInfo is null) continue;
                var itemObj = ReadObject(classInfo, "CurrentTimeLayoutItem", "TimeLayoutItem");
                list.Add(new ScheduleItem(
                    ResolveSubject(classInfo),
                    SecondsToClock(ReadInt(itemObj, "StartSecond", "Start")),
                    SecondsToClock(ReadInt(itemObj, "EndSecond", "End")),
                    ReadString(itemObj, "TimeType", "Type")));
            }

            lock (Gate)
            {
                _schedule = list.ToArray();
                _scheduleDate = DateTime.Today.ToString("yyyy-MM-dd");
            }
        }
        catch
        {
            // 课表读取失败时保留上次结果
        }
    }

    private static IPublicLessonsService? CreateLessonsProxy()
    {
        try
        {
            var client = _client;
            if (client?.PeerProxy is null) return null;
            return client.Provider.CreateIpcProxy<IPublicLessonsService>(client.PeerProxy);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// IPC 不可用时的兜底。
    /// 说明：ClassIsland 配置文件（档案 JSON）的完整解析较复杂，且 Python 核心的
    /// assistant_core/classisland/watcher.py 已实现直读配置文件的成熟逻辑；
    /// 因此这里仅把状态码置为 None、课表置空并在 source 中标注不可用，
    /// 由 Python 核心在桥失联时接管配置文件解析，避免两层重复实现。
    /// </summary>
    private static void LoadConfigFallback()
    {
        lock (Gate)
        {
            _ipcReady = false;
            _state = "None";
            _ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (_schedule.Length == 0)
                _scheduleDate = DateTime.Today.ToString("yyyy-MM-dd");
        }
    }

    /// <summary>把 ClassIsland 的 TimeState 映射为契约要求的四种状态字符串。</summary>
    private static string MapState(string raw)
    {
        if (raw.Contains("OnClass", StringComparison.OrdinalIgnoreCase)) return "OnClass";
        if (raw.Contains("Breaking", StringComparison.OrdinalIgnoreCase)) return "Breaking";
        if (raw.Contains("AfterSchool", StringComparison.OrdinalIgnoreCase)) return "AfterSchool";
        return "None";
    }

    private static string ResolveSubject(object classInfo)
    {
        var direct = ReadString(classInfo, "SubjectName", "Name");
        if (direct.Length > 0) return direct;
        var id = ReadString(classInfo, "SubjectId");
        if (id.Length == 0) return "课程";

        try
        {
            var client = _client;
            if (client?.PeerProxy is null) return "课程";
            var profileService = client.Provider.CreateIpcProxy<IPublicProfileService>(client.PeerProxy);
            var profileObj = profileService?.Profile;
            var subjects = profileObj?.GetType().GetProperty("Subjects")?.GetValue(profileObj);
            if (subjects is IDictionary dict && dict.Contains(id))
            {
                var name = ReadString(dict[id], "Name");
                if (name.Length > 0) return name;
            }
            if (subjects is IEnumerable list)
            {
                foreach (var subject in list)
                {
                    if (subject is null) continue;
                    if (ReadString(subject, "Id", "ID") != id) continue;
                    var name = ReadString(subject, "Name");
                    if (name.Length > 0) return name;
                }
            }
        }
        catch
        {
            // 科目名解析失败时返回通用名称，不影响其它字段
        }
        return "课程";
    }

    // ---------------- HTTP 服务 ----------------

    private static void Handle(HttpListenerContext context)
    {
        try
        {
            var path = context.Request.Url?.AbsolutePath ?? "/";
            switch (path)
            {
                case "/api/health":
                case "/ping":
                    WriteJson(context, new { ok = true });
                    break;
                case "/api/state":
                    lock (Gate)
                    {
                        WriteJson(context, new
                        {
                            state = _state,
                            lesson = new { subject = _subject, start = _start, end = _end, class_name = _className },
                            ts = _ts,
                        });
                    }
                    break;
                case "/api/schedule":
                    lock (Gate)
                    {
                        WriteJson(context, new
                        {
                            date = _scheduleDate.Length > 0 ? _scheduleDate : DateTime.Today.ToString("yyyy-MM-dd"),
                            items = _schedule.Select(i => new
                            {
                                subject = i.Subject,
                                start = i.Start,
                                end = i.End,
                                type = i.Type,
                            }).ToArray(),
                            source = _ipcReady ? "ClassIsland" : "ClassIsland-unavailable",
                        });
                    }
                    break;
                default:
                    WriteJson(context, new { ok = false, error = "not found" }, 404);
                    break;
            }
        }
        catch
        {
            // 单个请求异常不影响服务主循环
        }
        finally
        {
            try { context.Response.Close(); } catch { }
        }
    }

    private static void WriteJson(HttpListenerContext context, object value, int status = 200)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value));
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.ContentLength64 = bytes.Length;
        context.Response.OutputStream.Write(bytes, 0, bytes.Length);
    }

    // ---------------- 反射安全读取（兼容 ClassIsland 不同版本的模型属性） ----------------

    private static string Safe(Func<string> getter)
    {
        try { return getter(); } catch { return string.Empty; }
    }

    private static object? SafeObject(Func<object?> getter)
    {
        try { return getter(); } catch { return null; }
    }

    private static object? ReadObject(object source, params string[] names)
    {
        foreach (var name in names)
        {
            try
            {
                var value = source.GetType().GetProperty(name)?.GetValue(source);
                if (value is not null) return value;
            }
            catch
            {
                // 属性不可读时尝试下一个候选名
            }
        }
        return null;
    }

    private static string ReadString(object? source, params string[] names)
    {
        if (source is null) return string.Empty;
        foreach (var name in names)
        {
            try
            {
                var value = source.GetType().GetProperty(name)?.GetValue(source);
                if (value is not null) return value.ToString() ?? string.Empty;
            }
            catch
            {
                // 属性不可读时尝试下一个候选名
            }
        }
        return string.Empty;
    }

    private static int ReadInt(object? source, params string[] names)
    {
        if (source is null) return 0;
        foreach (var name in names)
        {
            try
            {
                var value = source.GetType().GetProperty(name)?.GetValue(source);
                if (value is int i) return i;
                if (value is TimeSpan span) return (int)span.TotalSeconds;
                if (value is not null && int.TryParse(value.ToString(), out var n)) return n;
            }
            catch
            {
                // 属性不可读时尝试下一个候选名
            }
        }
        return 0;
    }

    private static string SecondsToClock(int seconds)
    {
        if (seconds <= 0) return string.Empty;
        var time = TimeSpan.FromSeconds(seconds);
        return $"{time.Hours:00}:{time.Minutes:00}";
    }
}
