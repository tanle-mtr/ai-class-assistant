using System.Collections;
using System.Net;
using System.Text;
using System.Text.Json;
using ClassIsland.Shared.IPC;
using ClassIsland.Shared.IPC.Abstractions.Services;
using dotnetCampus.Ipc.CompilerServices.GeneratedProxies;
using Timer = System.Threading.Timer;

namespace AiClassAssistant.Bridge;

/// <summary>
/// 独立 ClassIsland 桥进程：
/// 通过 ClassIsland.Shared.IPC 订阅上课 / 下课 / 放学事件，
/// 把当前课程状态与今日课表暴露为本地 HTTP 服务 127.0.0.1:18761，
/// 供主程序 ClassIslandSource 轮询。
///
/// 契约端点：GET /api/health、GET /api/state、GET /api/schedule。
/// </summary>
internal static class Program
{
    private const int BridgePort = 18761;
    private static readonly object Gate = new();
    private static readonly string LogFile = Path.Combine(AppContext.BaseDirectory, "bridge.log");

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
            Log($"致命异常：{e}");
            try
            {
                File.AppendAllText(
                    Path.Combine(AppContext.BaseDirectory, "bridge_error.log"),
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {e}\n");
            }
            catch
            {
                // 日志不可写时忽略
            }
        }
    }

    private static void Run()
    {
        Log("启动，连接 ClassIsland IPC…");

        _client = new IpcClient();
        RegisterHandlers(_client);
        _ = _client.Connect();

        // 每秒刷新课程状态，每 30 秒刷新当天课表
        using var refreshTimer = new Timer(_ => Refresh(), null, 0, 1000);
        using var scheduleTimer = new Timer(_ => RefreshSchedule(), null, 0, 30000);

        // 20 秒自愈保活：IPC 断线时 dotnetCampus.Ipc 会自动重连，
        // 这里额外检查 PeerProxy 并补一次 Connect；进程级拉活由主程序的 ProcessGuard 负责。
        using var healTimer = new Timer(_ => Heal(), null, 20000, 20000);

        var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{BridgePort}/");
        listener.Start();
        Log($"HTTP 就绪：http://127.0.0.1:{BridgePort}/api/health");

        while (true)
        {
            try
            {
                HttpListenerContext context = listener.GetContext();
                _ = Task.Run(() => Handle(context));
            }
            catch (Exception e)
            {
                Log($"HTTP 异常：{e.Message}");
                LoadConfigFallback();
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
                Log("IPC 断线，尝试重连…");
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
        IPublicLessonsService? lessons = CreateLessonsProxy();
        if (lessons is null)
        {
            LoadConfigFallback();
            return;
        }

        try
        {
            string rawState = Safe(() => lessons.CurrentState.ToString() ?? string.Empty);
            string state = MapState(rawState);
            string subject = Safe(() => lessons.CurrentSubject?.Name ?? string.Empty);
            string className = Safe(() => lessons.CurrentClassPlan?.Name ?? string.Empty);
            object? item = SafeObject(() => lessons.CurrentTimeLayoutItem);
            string start = SecondsToClock(ReadInt(item, "StartSecond", "startSecond", "Start"));
            string end = SecondsToClock(ReadInt(item, "EndSecond", "endSecond", "End"));

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
        IPublicLessonsService? lessons = CreateLessonsProxy();
        if (lessons is null)
        {
            return;
        }

        try
        {
            object? plan = SafeObject(() => lessons.GetClassPlanByDate(DateTime.Today));
            if (plan is null)
            {
                return;
            }

            object? classes = ReadObject(plan, "Classes", "classes");
            if (classes is not IEnumerable enumerable)
            {
                return;
            }

            var list = new List<ScheduleItem>();
            foreach (object? classInfo in enumerable)
            {
                if (classInfo is null)
                {
                    continue;
                }

                object? item = ReadObject(classInfo, "CurrentTimeLayoutItem", "TimeLayoutItem");
                list.Add(new ScheduleItem(
                    ResolveSubject(classInfo),
                    SecondsToClock(ReadInt(item, "StartSecond", "startSecond", "Start")),
                    SecondsToClock(ReadInt(item, "EndSecond", "endSecond", "End")),
                    ReadString(item, "TimeType", "Type")));
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
            IpcClient? client = _client;
            if (client?.PeerProxy is null)
            {
                return null;
            }

            return client.Provider.CreateIpcProxy<IPublicLessonsService>(client.PeerProxy);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// IPC 不可用时的兜底：状态置 None、课表置空并在 source 中标注不可用。
    /// 配置文件直读由主程序 ClassIslandSource 负责，避免两层重复实现。
    /// </summary>
    private static void LoadConfigFallback()
    {
        lock (Gate)
        {
            _ipcReady = false;
            _state = "None";
            _ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (_schedule.Length == 0)
            {
                _scheduleDate = DateTime.Today.ToString("yyyy-MM-dd");
            }
        }
    }

    /// <summary>把 ClassIsland 的 TimeState 映射为契约要求的四种状态字符串。</summary>
    private static string MapState(string raw)
    {
        if (raw.Contains("OnClass", StringComparison.OrdinalIgnoreCase))
        {
            return "OnClass";
        }

        if (raw.Contains("Breaking", StringComparison.OrdinalIgnoreCase))
        {
            return "Breaking";
        }

        if (raw.Contains("AfterSchool", StringComparison.OrdinalIgnoreCase))
        {
            return "AfterSchool";
        }

        return "None";
    }

    private static string ResolveSubject(object classInfo)
    {
        string direct = ReadString(classInfo, "SubjectName", "Name", "Subject", "subject");
        if (direct.Length > 0)
        {
            return direct;
        }

        string id = ReadString(classInfo, "SubjectId");
        if (id.Length == 0)
        {
            return "课程";
        }

        try
        {
            IpcClient? client = _client;
            if (client?.PeerProxy is null)
            {
                return "课程";
            }

            IPublicProfileService? profileService = client.Provider.CreateIpcProxy<IPublicProfileService>(client.PeerProxy);
            object? profile = profileService?.Profile;
            object? subjects = ReadObject(profile, "Subjects", "subjects");
            if (subjects is IDictionary dict && dict.Contains(id))
            {
                string name = ReadString(dict[id], "Name");
                if (name.Length > 0)
                {
                    return name;
                }
            }

            if (subjects is IEnumerable list)
            {
                foreach (object? subject in list)
                {
                    if (subject is null || ReadString(subject, "Id", "ID") != id)
                    {
                        continue;
                    }

                    string name = ReadString(subject, "Name");
                    if (name.Length > 0)
                    {
                        return name;
                    }
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
            string path = context.Request.Url?.AbsolutePath ?? "/";
            string method = context.Request.HttpMethod;
            if (string.Equals(method, "OPTIONS", StringComparison.OrdinalIgnoreCase))
            {
                WriteJson(context, new { ok = true });
                return;
            }

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
                            ts = _ts
                        });
                    }
                    break;
                case "/api/schedule":
                    lock (Gate)
                    {
                        WriteJson(context, new
                        {
                            date = _scheduleDate.Length > 0 ? _scheduleDate : DateTime.Today.ToString("yyyy-MM-dd"),
                            items = _schedule.Select(item => new
                            {
                                subject = item.Subject,
                                start = item.Start,
                                end = item.End,
                                type = item.Type
                            }).ToArray(),
                            source = _ipcReady ? "ClassIsland" : "ClassIsland-unavailable"
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
            try
            {
                context.Response.Close();
            }
            catch
            {
                // 客户端已断开时忽略
            }
        }
    }

    private static void WriteJson(HttpListenerContext context, object value, int status = 200)
    {
        try
        {
            context.Response.Headers.Add("Access-Control-Allow-Origin", "*");
            context.Response.Headers.Add("Access-Control-Allow-Methods", "GET, OPTIONS");
            context.Response.Headers.Add("Access-Control-Allow-Headers", "Content-Type");
        }
        catch
        {
            // 响应已发送时忽略
        }

        byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value));
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.ContentLength64 = bytes.Length;
        context.Response.OutputStream.Write(bytes, 0, bytes.Length);
    }

    private static void Log(string message)
    {
        try
        {
            File.AppendAllText(LogFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}\n");
        }
        catch
        {
            // 日志目录不可写时静默跳过
        }
    }

    // ---------------- 反射安全读取（兼容 ClassIsland 不同版本的模型属性） ----------------

    private static string Safe(Func<string> getter)
    {
        try
        {
            return getter();
        }
        catch
        {
            return string.Empty;
        }
    }

    private static object? SafeObject(Func<object?> getter)
    {
        try
        {
            return getter();
        }
        catch
        {
            return null;
        }
    }

    private static object? ReadObject(object? source, params string[] names)
    {
        if (source is null)
        {
            return null;
        }

        foreach (string name in names)
        {
            try
            {
                object? value = source.GetType().GetProperty(name)?.GetValue(source);
                if (value is not null)
                {
                    return value;
                }
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
        if (source is null)
        {
            return string.Empty;
        }

        foreach (string name in names)
        {
            try
            {
                object? value = source.GetType().GetProperty(name)?.GetValue(source);
                if (value is not null)
                {
                    return value.ToString() ?? string.Empty;
                }
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
        if (source is null)
        {
            return 0;
        }

        foreach (string name in names)
        {
            try
            {
                object? value = source.GetType().GetProperty(name)?.GetValue(source);
                if (value is int number)
                {
                    return number;
                }

                if (value is TimeSpan span)
                {
                    return (int)span.TotalSeconds;
                }

                if (value is not null && int.TryParse(value.ToString(), out int parsed))
                {
                    return parsed;
                }
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
        if (seconds <= 0)
        {
            return string.Empty;
        }

        var time = TimeSpan.FromSeconds(seconds);
        return $"{time.Hours:00}:{time.Minutes:00}";
    }
}
