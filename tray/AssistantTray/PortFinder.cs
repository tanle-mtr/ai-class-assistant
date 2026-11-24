using System.Text.Json;

namespace AssistantTray;

/// <summary>
/// 探测 Python 核心真实 Web 端口。
///
/// 重要：旧版有两个 bug —— (1) 项目根目录少拼一级 ".."，读不到 config.json；
/// (2) 没有健康校验就静默回退 18760，导致顺延端口时网页打不开。
/// 本实现：config.json 的 web_port 与 18762-18777 顺延端口都必须经过 /api/health
/// 校验（响应 {"ok":true}），全部失败时返回配置端口并标记未验证，绝不伪装成可用。
/// </summary>
internal static class PortFinder
{
    private const int DefaultPort = 18760;
    private const int ScanStart = 18762;
    private const int ScanEnd = 18777;

    private static readonly object Gate = new();
    private static int? _cached;
    private static bool _verified;
    private static DateTime _lastReset = DateTime.MinValue;

    /// <summary>
    /// 项目根目录。exe 位于 tray/AssistantTray/bin/Release/net8.0-windows/，
    /// 向上 5 级：net8.0-windows → Release → bin → AssistantTray → tray → 项目根。
    /// </summary>
    internal static string ProjectRoot { get; } = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    /// <summary>是否探测到了通过健康校验的真实端口。</summary>
    internal static bool IsVerified
    {
        get { lock (Gate) return _verified; }
    }

    /// <summary>探测端口；结果缓存，调用方在连续失败时可 Reset 重新探测。</summary>
    internal static int Find()
    {
        lock (Gate)
        {
            if (_cached.HasValue) return _cached.Value;
        }

        var configPort = ReadConfigPort();
        if (Healthy(configPort)) return Cache(configPort, true);

        // 分批并发扫描顺延端口，单批最多 6 个、总等待 4 秒，避免长时间卡顿
        for (var start = ScanStart; start <= ScanEnd; start += 6)
        {
            var count = Math.Min(6, ScanEnd - start + 1);
            var tasks = Enumerable.Range(start, count)
                .Select(p => Task.Run(() => Healthy(p) ? p : 0))
                .ToArray();
            Task.WaitAll(tasks, 4000);
            foreach (var task in tasks)
            {
                if (task.Status == TaskStatus.RanToCompletion && task.Result > 0)
                    return Cache(task.Result, true);
            }
        }

        // 没有任何健康服务：返回配置端口供调用方降级（不再假装 18760 一定可用）
        return Cache(configPort, false);
    }

    /// <summary>
    /// 清除缓存（核心刚启动或端口变化时调用）。
    /// 做 30 秒节流：后端未启动时 API 轮询会连续失败，若每次都重扫会反复卡顿 UI。
    /// </summary>
    internal static void Reset()
    {
        lock (Gate)
        {
            if ((DateTime.Now - _lastReset).TotalSeconds < 30) return;
            _lastReset = DateTime.Now;
            _cached = null;
            _verified = false;
        }
    }

    private static int Cache(int port, bool verified)
    {
        lock (Gate)
        {
            _cached = port;
            _verified = verified;
        }
        return port;
    }

    private static int ReadConfigPort()
    {
        try
        {
            var cfg = Path.Combine(ProjectRoot, "data", "config", "config.json");
            if (!File.Exists(cfg)) return DefaultPort;
            using var doc = JsonDocument.Parse(File.ReadAllText(cfg));
            if (doc.RootElement.TryGetProperty("web_port", out var p)
                && p.TryGetInt32(out var port)
                && port is >= 1 and <= 65535)
                return port;
        }
        catch
        {
            // 配置文件损坏时回落到默认端口，仍会经过健康校验
        }
        return DefaultPort;
    }

    private static bool Healthy(int port)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromMilliseconds(1200) };
            var json = http.GetStringAsync($"http://127.0.0.1:{port}/api/health")
                .GetAwaiter().GetResult();
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                   && doc.RootElement.TryGetProperty("ok", out var ok)
                   && ok.ValueKind == JsonValueKind.True;
        }
        catch
        {
            return false;
        }
    }
}
