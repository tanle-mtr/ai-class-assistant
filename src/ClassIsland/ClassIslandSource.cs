using System.Collections;
using System.Net.Http;
using System.Text.Json;
using AiClassAssistant.Core.Configuration;
using AiClassAssistant.Core.Contracts;
using AiClassAssistant.Core.Models;
using ClassIsland.Shared.IPC;
using ClassIsland.Shared.IPC.Abstractions.Services;
using dotnetCampus.Ipc.CompilerServices.GeneratedProxies;
using Timer = System.Threading.Timer;

namespace AiClassAssistant.ClassIsland;

/// <summary>
/// ClassIsland 课表来源：
/// 1) 订阅 ClassIsland.Shared.IPC 通知并读取 IPublicLessonsService；
/// 2) 轮询本地桥 http://127.0.0.1:18761/api/state 与 /api/schedule（3 秒超时）；
/// 3) 两者都失败时直读 profiles.json 配置兜底。
///
/// 未安装 ClassIsland 时返回 LessonState.None + 空课表，绝不抛异常。
/// </summary>
public sealed class ClassIslandSource : IClassIslandSource, IDisposable
{
    private static readonly string[] Weekdays =
        { "Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday" };

    private readonly object _gate = new();
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(3) };
    private readonly string _bridgeBase;
    private IpcClient? _ipcClient;
    private Timer? _timer;
    private LessonState _state = LessonState.None;
    private LessonInfo? _currentLesson;
    private List<LessonInfo> _schedule = new();
    private string _source = "none";
    private bool _running;
    private bool _disposed;

    public ClassIslandSource()
    {
        int port = AppConfig.Instance.Get("classisland_bridge_port", 18761);
        _bridgeBase = $"http://127.0.0.1:{port}";
    }

    public event Action<LessonState, LessonInfo?>? StateChanged;

    public LessonState State
    {
        get
        {
            lock (_gate)
            {
                return _state;
            }
        }
    }

    /// <summary>当前数据来源：ipc / bridge / file / none（诊断用，接口外附加）。</summary>
    public string Source
    {
        get
        {
            lock (_gate)
            {
                return _source;
            }
        }
    }

    public List<LessonInfo> TodaySchedule()
    {
        lock (_gate)
        {
            return new List<LessonInfo>(_schedule);
        }
    }

    public void Start()
    {
        if (_running || _disposed)
        {
            return;
        }

        _running = true;
        InitIpc();
        _timer = new Timer(_ => RefreshSafely(), null, 0, 3000);
    }

    public void Stop()
    {
        if (!_running)
        {
            return;
        }

        _running = false;
        Timer? timer = Interlocked.Exchange(ref _timer, null);
        timer?.Dispose();
        CloseIpc();
    }

    private void RefreshSafely()
    {
        if (!_running || _disposed)
        {
            return;
        }

        try
        {
            Refresh();
        }
        catch
        {
            // 单轮刷新失败不中断常驻轮询
        }
    }

    /// <summary>按优先级刷新：IPC → 本地桥 → 配置文件兜底。</summary>
    private void Refresh()
    {
        if (TryRefreshFromIpc())
        {
            return;
        }

        if (TryRefreshFromBridge())
        {
            return;
        }

        TryRefreshFromFile();
    }

    // ---------------- IPC ----------------

    private void InitIpc()
    {
        try
        {
            var client = new IpcClient();
            client.JsonIpcProvider.AddNotifyHandler(IpcRoutedNotifyIds.CurrentTimeStateChangedNotifyId, OnIpcNotify);
            client.JsonIpcProvider.AddNotifyHandler(IpcRoutedNotifyIds.OnClassNotifyId, OnIpcNotify);
            client.JsonIpcProvider.AddNotifyHandler(IpcRoutedNotifyIds.OnBreakingTimeNotifyId, OnIpcNotify);
            client.JsonIpcProvider.AddNotifyHandler(IpcRoutedNotifyIds.OnAfterSchoolNotifyId, OnIpcNotify);
            _ = client.Connect();
            _ipcClient = client;
        }
        catch
        {
            // 未安装 ClassIsland / IPC 版本差异：静默降级到桥或配置文件
            _ipcClient = null;
        }
    }

    private void CloseIpc()
    {
        // ClassIsland.Shared.IPC 1.6.0 的 IpcClient 不提供 Dispose / RemoveNotifyHandler；
        // 这里仅丢弃引用，由 GC 回收，IPC 断线由 dotnetCampus.Ipc 自身处理。
        _ipcClient = null;
    }

    private void OnIpcNotify()
    {
        if (_running)
        {
            RefreshSafely();
        }
    }

    private IPublicLessonsService? CreateLessonsProxy()
    {
        try
        {
            IpcClient? client = _ipcClient;
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

    private bool TryRefreshFromIpc()
    {
        IPublicLessonsService? lessons = CreateLessonsProxy();
        if (lessons is null)
        {
            return false;
        }

        try
        {
            string raw = Safe(() => lessons.CurrentState.ToString() ?? string.Empty);
            LessonState state = MapState(raw);
            string subject = Safe(() => lessons.CurrentSubject?.Name ?? string.Empty);
            string className = Safe(() => lessons.CurrentClassPlan?.Name ?? string.Empty);
            object? item = SafeObject(() => lessons.CurrentTimeLayoutItem);
            string start = SecondsToClock(ReadInt(item, "StartSecond", "startSecond", "Start"));
            string end = SecondsToClock(ReadInt(item, "EndSecond", "endSecond", "End"));

            LessonInfo? lesson = null;
            if (state == LessonState.OnClass || state == LessonState.Breaking)
            {
                lesson = new LessonInfo
                {
                    Subject = subject,
                    ClassName = className,
                    Start = start,
                    End = end,
                    Type = LessonTypeUtil.Of(subject)
                };
            }

            Update(state, lesson, "ipc");
            RefreshScheduleFromIpc(lessons);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void RefreshScheduleFromIpc(IPublicLessonsService lessons)
    {
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

            var list = new List<LessonInfo>();
            foreach (object? classInfo in enumerable)
            {
                if (classInfo is null)
                {
                    continue;
                }

                object? item = ReadObject(classInfo, "CurrentTimeLayoutItem", "TimeLayoutItem");
                string subject = ResolveSubject(classInfo);
                list.Add(new LessonInfo
                {
                    Subject = subject,
                    Start = SecondsToClock(ReadInt(item, "StartSecond", "startSecond", "Start")),
                    End = SecondsToClock(ReadInt(item, "EndSecond", "endSecond", "End")),
                    Type = ParseType(ReadString(item, "TimeType", "Type"), subject),
                    Index = list.Count + 1
                });
            }

            if (list.Count > 0)
            {
                lock (_gate)
                {
                    _schedule = list;
                }
            }
        }
        catch
        {
            // 课表读取失败时保留上次结果
        }
    }

    private string ResolveSubject(object classInfo)
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
            IpcClient? client = _ipcClient;
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
            // 科目名解析失败时返回通用名称
        }

        return "课程";
    }

    // ---------------- 本地桥 HTTP ----------------

    private bool TryRefreshFromBridge()
    {
        try
        {
            string? json = _http.GetStringAsync($"{_bridgeBase}/api/state").GetAwaiter().GetResult();
            if (string.IsNullOrWhiteSpace(json))
            {
                return false;
            }

            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            LessonState state = MapState(GetString(root, "state"));

            LessonInfo? lesson = null;
            if (TryGetProp(root, out JsonElement lessonElement, "lesson")
                && lessonElement.ValueKind == JsonValueKind.Object)
            {
                string subject = GetString(lessonElement, "subject");
                lesson = new LessonInfo
                {
                    Subject = subject,
                    ClassName = GetString(lessonElement, "class_name"),
                    Start = GetString(lessonElement, "start"),
                    End = GetString(lessonElement, "end"),
                    Type = LessonTypeUtil.Of(subject)
                };
            }

            // 桥在运行但 ClassIsland IPC 不可用时，桥会返回 source="ClassIsland-unavailable"，
            // 此时应让配置文件兜底接管，而不是把 None 当作有效状态。
            bool ipcReady = RefreshScheduleFromBridge();
            if (state == LessonState.None && !ipcReady)
            {
                return false;
            }

            Update(state, lesson, "bridge");
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>刷新桥侧课表；返回桥是否报告 ClassIsland IPC 可用。</summary>
    private bool RefreshScheduleFromBridge()
    {
        try
        {
            string? json = _http.GetStringAsync($"{_bridgeBase}/api/schedule").GetAwaiter().GetResult();
            if (string.IsNullOrWhiteSpace(json))
            {
                return false;
            }

            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            string source = GetString(root, "source");
            bool ipcReady = !source.Contains("unavailable", StringComparison.OrdinalIgnoreCase);
            if (!TryGetProp(root, out JsonElement items, "items") || items.ValueKind != JsonValueKind.Array)
            {
                return ipcReady;
            }

            var list = new List<LessonInfo>();
            foreach (JsonElement item in items.EnumerateArray())
            {
                string subject = GetString(item, "subject");
                list.Add(new LessonInfo
                {
                    Subject = subject,
                    Start = GetString(item, "start"),
                    End = GetString(item, "end"),
                    Type = ParseType(GetString(item, "type"), subject),
                    Index = list.Count + 1
                });
            }

            lock (_gate)
            {
                _schedule = list;
            }

            return ipcReady;
        }
        catch
        {
            // 桥课表不可用时保留上次结果
            return false;
        }
    }

    // ---------------- 配置文件兜底 ----------------

    private bool TryRefreshFromFile()
    {
        try
        {
            string? directory = FindConfigDir();
            if (directory is null)
            {
                return false;
            }

            string path = Path.Combine(directory, "profiles.json");
            if (!File.Exists(path))
            {
                return false;
            }

            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
            JsonElement root = document.RootElement;
            List<LessonInfo> items = ParseScheduleFromFile(root, DateTime.Now);
            if (items.Count == 0)
            {
                return false;
            }

            int nowSeconds = DateTime.Now.Hour * 3600 + DateTime.Now.Minute * 60 + DateTime.Now.Second;
            LessonState state = LessonState.None;
            LessonInfo? current = null;
            foreach (LessonInfo lesson in items)
            {
                int start = ParseClock(lesson.Start);
                int end = ParseClock(lesson.End);
                if (nowSeconds >= start && nowSeconds < end)
                {
                    state = LessonState.OnClass;
                    current = lesson;
                    break;
                }

                if (nowSeconds >= end && nowSeconds < end + 60)
                {
                    state = LessonState.Breaking;
                    current = lesson;
                    break;
                }
            }

            if (state == LessonState.None && items.Count > 0 && nowSeconds > ParseClock(items[^1].End))
            {
                state = LessonState.AfterSchool;
            }

            lock (_gate)
            {
                _schedule = items;
            }

            Update(state, current, "file");
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string? FindConfigDir()
    {
        try
        {
            string configured = AppConfig.Instance.Get("classisland_conf_dir", "");
            if (!string.IsNullOrWhiteSpace(configured)
                && File.Exists(Path.Combine(configured, "profiles.json")))
            {
                return configured;
            }
        }
        catch
        {
            // 配置读取失败时继续探测默认位置
        }

        foreach (string env in new[] { "APPDATA", "LOCALAPPDATA" })
        {
            string? baseDir = Environment.GetEnvironmentVariable(env);
            if (string.IsNullOrWhiteSpace(baseDir))
            {
                continue;
            }

            string candidate = Path.Combine(baseDir, "ClassIsland");
            if (File.Exists(Path.Combine(candidate, "profiles.json")))
            {
                return candidate;
            }
        }

        return null;
    }

    private static List<LessonInfo> ParseScheduleFromFile(JsonElement root, DateTime now)
    {
        var result = new List<LessonInfo>();
        if (!TryGetProp(root, out JsonElement plans, "ClassPlans", "classPlans", "ClassPlan"))
        {
            return result;
        }

        JsonElement? activePlan = null;
        if (plans.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement plan in plans.EnumerateArray())
            {
                if (GetBool(plan, "IsCurrent", "isCurrent", "IsEnabled"))
                {
                    activePlan = plan;
                    break;
                }
            }

            activePlan ??= plans.EnumerateArray().FirstOrDefault();
        }
        else if (plans.ValueKind == JsonValueKind.Object)
        {
            activePlan = plans;
        }

        if (activePlan is null)
        {
            return result;
        }

        JsonElement day = FindToday(activePlan.Value, now);
        if (day.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        if (!TryGetProp(root, out JsonElement layouts, "TimeLayouts", "timeLayouts"))
        {
            return result;
        }

        JsonElement points = default;
        if (layouts.ValueKind == JsonValueKind.Array)
        {
            string layoutName = GetString(day, "TimeLayout", "timeLayout", "TimeLayoutName");
            foreach (JsonElement layout in layouts.EnumerateArray())
            {
                string name = GetString(layout, "Name", "name");
                if (name == layoutName || layoutName.Length == 0)
                {
                    TryGetProp(layout, out points, "TimePoints", "timePoints");
                    break;
                }
            }
        }

        if (points.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (JsonElement entry in day.EnumerateArray())
        {
            if (!TryGetProp(entry, out JsonElement classes, "Classes", "classes")
                || classes.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            int classIndex = 0;
            foreach (JsonElement course in classes.EnumerateArray())
            {
                string subject = GetString(course, "Subject", "subject");
                if (subject.Length == 0 || classIndex >= points.GetArrayLength())
                {
                    classIndex++;
                    continue;
                }

                JsonElement point = points[classIndex++];
                int start = GetInt(point, "StartSecond", "startSecond", "Start");
                int end = GetInt(point, "EndSecond", "endSecond", "End");
                result.Add(new LessonInfo
                {
                    Subject = subject,
                    Start = SecondsToClock(start),
                    End = SecondsToClock(end == 0 ? start : end),
                    Type = LessonTypeUtil.Of(subject),
                    Index = result.Count + 1
                });
            }
        }

        return result.OrderBy(item => item.Start).ToList();
    }

    private static JsonElement FindToday(JsonElement plan, DateTime now)
    {
        string weekday = Weekdays[(int)now.DayOfWeek];
        if (!TryGetProp(plan, out JsonElement week, "Week", "week"))
        {
            return default;
        }

        if (week.ValueKind == JsonValueKind.Object && week.TryGetProperty(weekday, out JsonElement direct))
        {
            return direct;
        }

        if (week.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in week.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty(weekday, out JsonElement found))
                {
                    return found;
                }
            }
        }

        return default;
    }

    // ---------------- 状态更新与工具 ----------------

    private void Update(LessonState state, LessonInfo? lesson, string source)
    {
        bool changed;
        lock (_gate)
        {
            changed = _state != state || !LessonEquals(_currentLesson, lesson);
            _state = state;
            _currentLesson = lesson;
            _source = source;
        }

        if (changed)
        {
            StateChanged?.Invoke(state, lesson);
        }
    }

    private static bool LessonEquals(LessonInfo? a, LessonInfo? b)
    {
        if (ReferenceEquals(a, b))
        {
            return true;
        }

        if (a is null || b is null)
        {
            return false;
        }

        return a.Subject == b.Subject && a.ClassName == b.ClassName
               && a.Start == b.Start && a.End == b.End && a.Type == b.Type && a.Index == b.Index;
    }

    private static LessonState MapState(string raw)
    {
        if (raw.Contains("OnClass", StringComparison.OrdinalIgnoreCase))
        {
            return LessonState.OnClass;
        }

        if (raw.Contains("Breaking", StringComparison.OrdinalIgnoreCase))
        {
            return LessonState.Breaking;
        }

        if (raw.Contains("AfterSchool", StringComparison.OrdinalIgnoreCase))
        {
            return LessonState.AfterSchool;
        }

        return LessonState.None;
    }

    private static LessonType ParseType(string type, string subject)
    {
        if (type.Contains("Exam", StringComparison.OrdinalIgnoreCase)
            || type.Contains("考试", StringComparison.Ordinal)
            || type.Contains("自习", StringComparison.Ordinal))
        {
            return LessonType.Exam;
        }

        if (type.Contains("Review", StringComparison.OrdinalIgnoreCase)
            || type.Contains("评讲", StringComparison.Ordinal)
            || type.Contains("讲评", StringComparison.Ordinal))
        {
            return LessonType.Review;
        }

        return LessonTypeUtil.Of(subject);
    }

    private static string SecondsToClock(int seconds)
    {
        if (seconds <= 0)
        {
            return "";
        }

        var time = TimeSpan.FromSeconds(seconds);
        return $"{time.Hours:00}:{time.Minutes:00}";
    }

    private static int ParseClock(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        string[] parts = text.Split(':');
        return parts.Length >= 2
               && int.TryParse(parts[0], out int hour)
               && int.TryParse(parts[1], out int minute)
            ? hour * 3600 + minute * 60
            : 0;
    }

    // ---------------- 反射安全读取（兼容 ClassIsland 模型版本差异） ----------------

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

    private static bool TryGetProp(JsonElement element, out JsonElement value, params string[] names)
    {
        value = default;
        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        foreach (string name in names)
        {
            if (element.TryGetProperty(name, out value))
            {
                return true;
            }
        }

        foreach (JsonProperty property in element.EnumerateObject())
        {
            foreach (string name in names)
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        return false;
    }

    private static string GetString(JsonElement element, params string[] names)
    {
        if (!TryGetProp(element, out JsonElement value, names))
        {
            return string.Empty;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.Number => value.GetRawText(),
            _ => string.Empty
        };
    }

    private static int GetInt(JsonElement element, params string[] names)
    {
        if (!TryGetProp(element, out JsonElement value, names))
        {
            return 0;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number))
        {
            return number;
        }

        if (value.ValueKind == JsonValueKind.String
            && int.TryParse(value.GetString(), out int parsed))
        {
            return parsed;
        }

        return 0;
    }

    private static bool GetBool(JsonElement element, params string[] names)
    {
        if (!TryGetProp(element, out JsonElement value, names))
        {
            return false;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => value.GetInt32() != 0,
            _ => false
        };
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();
        _http.Dispose();
        GC.SuppressFinalize(this);
    }
}
