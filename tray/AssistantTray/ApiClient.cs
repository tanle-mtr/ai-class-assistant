using System.Text;
using System.Text.Json;

namespace AssistantTray;

// ---------------- 轻量 DTO（与契约第 4 节字段对齐） ----------------

internal sealed class SessionInfo
{
    public string Subject { get; set; } = string.Empty;
    public string ClassName { get; set; } = string.Empty;
    public string Start { get; set; } = string.Empty;
    public string End { get; set; } = string.Empty;
    public int DurationMin { get; set; }
    public bool Overtime { get; set; }
}

internal sealed class AppState
{
    public SessionInfo? Session { get; set; }
    public string Model { get; set; } = string.Empty;
    public string VisionModel { get; set; } = string.Empty;
    public bool MonitorEnabled { get; set; } = true;
    public bool AlwaysRecord { get; set; }
    public string Tunnel { get; set; } = string.Empty;
    public string PublicIp { get; set; } = string.Empty;
}

internal sealed class DbInfo
{
    public double[] Samples { get; set; } = Array.Empty<double>();
    public double AvgDb { get; set; }
    public double PeakDb { get; set; }
    public int Count { get; set; }
}

internal sealed class ScheduleItem
{
    public string Subject { get; set; } = string.Empty;
    public string Start { get; set; } = string.Empty;
    public string End { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
}

internal sealed class ScheduleInfo
{
    public string Date { get; set; } = string.Empty;
    public ScheduleItem[] Items { get; set; } = Array.Empty<ScheduleItem>();
    public string Source { get; set; } = string.Empty;
}

internal sealed class ModelOption
{
    public string Name { get; set; } = string.Empty;
    public long Size { get; set; }
}

internal sealed class ModelsInfo
{
    public ModelOption[] Models { get; set; } = Array.Empty<ModelOption>();
    public string Current { get; set; } = string.Empty;
}

internal sealed class ApiResult
{
    public bool Ok { get; set; }
    public string Error { get; set; } = string.Empty;
}

/// <summary>
///  Python 核心 Web API 的 HttpClient 封装。
/// 所有请求都有超时和异常保护：后端未启动、端口漂移、JSON 损坏都不会让托盘崩溃。
/// </summary>
internal sealed class ApiClient
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(3) };

    private static string Url(string path) => $"http://127.0.0.1:{PortFinder.Find()}/{path}";

    /// <summary>连续失败时清掉端口缓存，下一次请求重新探测真实端口。</summary>
    private static void Invalidate()
    {
        // 仅在确认失败（拿到空响应）时才 Reset；健康探测本身已带缓存
        PortFinder.Reset();
    }

    private string? GetJson(string path)
    {
        try
        {
            var json = _http.GetStringAsync(Url(path)).GetAwaiter().GetResult();
            return string.IsNullOrWhiteSpace(json) ? null : json;
        }
        catch
        {
            return null;
        }
    }

    private ApiResult PostJson(string path, string? body = null, int timeoutSeconds = 5)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(timeoutSeconds) };
            using var content = body is null
                ? new StringContent(string.Empty, Encoding.UTF8, "application/json")
                : new StringContent(body, Encoding.UTF8, "application/json");
            var resp = client.PostAsync(Url(path), content).GetAwaiter().GetResult();
            var json = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            if (!resp.IsSuccessStatusCode || string.IsNullOrWhiteSpace(json))
                return new ApiResult { Error = $"HTTP {(int)resp.StatusCode}" };
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            return new ApiResult
            {
                Ok = root.ValueKind == JsonValueKind.Object
                     && root.TryGetProperty("ok", out var ok)
                     && ok.ValueKind == JsonValueKind.True,
                Error = GetString(root, "error"),
            };
        }
        catch (Exception e)
        {
            return new ApiResult { Error = e.Message };
        }
    }

    // ---------------- GET ----------------

    internal bool Health()
    {
        var json = GetJson("api/health");
        if (json is null) return false;
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                   && doc.RootElement.TryGetProperty("ok", out var ok)
                   && ok.ValueKind == JsonValueKind.True;
        }
        catch { return false; }
    }

    internal AppState GetState()
    {
        var result = new AppState();
        var json = GetJson("api/state");
        if (json is null)
        {
            Invalidate();
            return result;
        }
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            result.Model = GetString(root, "model");
            result.VisionModel = GetString(root, "vision_model");
            result.MonitorEnabled = !root.TryGetProperty("monitor_enabled", out var me)
                                    || (me.ValueKind == JsonValueKind.True);
            result.AlwaysRecord = root.TryGetProperty("monitor_always_record", out var ar)
                                  && ar.ValueKind == JsonValueKind.True;
            result.Tunnel = GetString(root, "tunnel");
            result.PublicIp = GetString(root, "public_ip");

            if (root.TryGetProperty("session", out var s) && s.ValueKind == JsonValueKind.Object)
            {
                result.Session = new SessionInfo
                {
                    Subject = GetString(s, "subject"),
                    ClassName = GetString(s, "class_name"),
                    Start = GetString(s, "start"),
                    End = GetString(s, "end"),
                    DurationMin = GetInt(s, "duration_min"),
                    Overtime = s.TryGetProperty("overtime", out var ov)
                               && ov.ValueKind == JsonValueKind.True,
                };
            }
        }
        catch
        {
            // 响应格式异常时返回已解析的默认状态
        }
        return result;
    }

    internal DbInfo GetDb()
    {
        var result = new DbInfo();
        var json = GetJson("api/db");
        if (json is null) return result;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("samples", out var samples)
                && samples.ValueKind == JsonValueKind.Array)
            {
                var list = new List<double>();
                foreach (var v in samples.EnumerateArray())
                {
                    if (v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d))
                        list.Add(d);
                }
                result.Samples = list.ToArray();
            }
            result.AvgDb = GetDouble(root, "avg_db");
            result.PeakDb = GetDouble(root, "peak_db");
            result.Count = GetInt(root, "count");
        }
        catch
        {
            // 保留默认空数组
        }
        return result;
    }

    internal ScheduleInfo GetSchedule()
    {
        var result = new ScheduleInfo();
        var json = GetJson("api/schedule");
        if (json is null) return result;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            result.Date = GetString(root, "date");
            result.Source = GetString(root, "source");
            if (root.TryGetProperty("items", out var items)
                && items.ValueKind == JsonValueKind.Array)
            {
                var list = new List<ScheduleItem>();
                foreach (var it in items.EnumerateArray())
                {
                    if (it.ValueKind != JsonValueKind.Object) continue;
                    list.Add(new ScheduleItem
                    {
                        Subject = GetString(it, "subject"),
                        Start = GetString(it, "start"),
                        End = GetString(it, "end"),
                        Type = GetString(it, "type"),
                    });
                }
                result.Items = list.ToArray();
            }
        }
        catch
        {
            // 保留默认空课表
        }
        return result;
    }

    internal ModelsInfo GetModels()
    {
        var result = new ModelsInfo();
        var json = GetJson("api/models");
        if (json is null) return result;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            result.Current = GetString(root, "current");
            if (root.TryGetProperty("models", out var models)
                && models.ValueKind == JsonValueKind.Array)
            {
                var list = new List<ModelOption>();
                foreach (var m in models.EnumerateArray())
                {
                    if (m.ValueKind != JsonValueKind.Object) continue;
                    list.Add(new ModelOption
                    {
                        Name = GetString(m, "name"),
                        Size = GetInt64(m, "size"),
                    });
                }
                result.Models = list.ToArray();
            }
        }
        catch
        {
            // 保留默认空模型列表
        }
        return result;
    }

    // ---------------- POST ----------------

    internal ApiResult SelectModel(string model)
        => PostJson("api/models/select", $"{{\"model\":{JsonSerializer.Serialize(model)}}}");

    internal ApiResult SetMonitor(bool enabled)
        => PostJson("api/monitor", $"{{\"enabled\":{(enabled ? "true" : "false")}}}");

    internal ApiResult Summarize() => PostJson("api/summarize");

    internal ApiResult Export() => PostJson("api/export");

    /// <summary>优雅停机：给核心更长的超时以完成停感知/导出。</summary>
    internal ApiResult Shutdown() => PostJson("api/shutdown", timeoutSeconds: 10);

    // ---------------- JsonElement 安全读取 ----------------

    private static string GetString(JsonElement obj, string name)
        => obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? string.Empty
            : string.Empty;

    private static int GetInt(JsonElement obj, string name)
        => obj.TryGetProperty(name, out var v)
           && v.ValueKind == JsonValueKind.Number
           && v.TryGetInt32(out var n)
            ? n
            : 0;

    private static long GetInt64(JsonElement obj, string name)
        => obj.TryGetProperty(name, out var v)
           && v.ValueKind == JsonValueKind.Number
           && v.TryGetInt64(out var n)
            ? n
            : 0;

    private static double GetDouble(JsonElement obj, string name)
        => obj.TryGetProperty(name, out var v)
           && v.ValueKind == JsonValueKind.Number
           && v.TryGetDouble(out var d)
            ? d
            : 0;
}
