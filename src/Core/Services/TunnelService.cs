using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AiClassAssistant.Core.Configuration;

namespace AiClassAssistant.Core.Services;

/// <summary>一条 Cloudflare Tunnel 规则（name → 本机端口）。</summary>
public sealed class TunnelRule
{
    public string Name { get; set; } = "";
    public int Port { get; set; }
    public string Scheme { get; set; } = "http";

    public TunnelRule Clone() => new() { Name = Name, Port = Port, Scheme = Scheme };
}

/// <summary>
/// cloudflared / openlist 命令行进程封装：token 与 rules 存 data/config/tunnel.json，
/// 规则真正参与启动参数（无 token 时用 --url，有 token 时写入 yml 并 --config）。
/// exe 不存在返回 false，优雅降级。
/// </summary>
public sealed class TunnelService : IDisposable
{
    private readonly AppConfig _config;
    private readonly object _gate = new();
    private readonly List<TunnelRule> _rules = new();
    private Process? _cloudflared;
    private Process? _openlist;
    private string _token = "";
    private bool _enabled;

    public TunnelService(AppConfig config)
    {
        _config = config;
        Load();
    }

    public string ConfigFile => Path.Combine(_config.Dir("config"), "tunnel.json");
    public string YamlFile => Path.Combine(_config.Dir("config"), "cloudflared.yml");

    /// <summary>可执行文件：默认从 PATH 查找，可由调用方覆盖为绝对路径。</summary>
    public string CloudflaredExe { get; set; } = "cloudflared.exe";
    public string OpenListExe { get; set; } = "openlist.exe";

    public IReadOnlyList<TunnelRule> Rules
    {
        get { lock (_gate) { return _rules.Select(r => r.Clone()).ToList(); } }
    }

    public string Token
    {
        get { lock (_gate) { return _token; } }
    }

    public bool Enabled
    {
        get { lock (_gate) { return _enabled; } }
    }

    public bool IsRunning
    {
        get
        {
            try
            {
                return _cloudflared is not null && !_cloudflared.HasExited;
            }
            catch
            {
                return false;
            }
        }
    }

    public void Load()
    {
        if (!File.Exists(ConfigFile))
        {
            // 首次启动：兼容旧 config.json 中的 tunnel_token / tunnel_enabled。
            lock (_gate)
            {
                _token = _config.Get("tunnel_token", "");
                _enabled = _config.Get("tunnel_enabled", false);
            }

            return;
        }

        try
        {
            JsonNode? root = JsonNode.Parse(File.ReadAllText(ConfigFile, Encoding.UTF8));
            if (root is not JsonObject obj)
            {
                return;
            }

            var parsed = new List<TunnelRule>();
            if (obj["rules"] is JsonArray rules)
            {
                foreach (JsonNode? node in rules)
                {
                    if (node is not JsonObject item)
                    {
                        continue;
                    }

                    parsed.Add(new TunnelRule
                    {
                        Name = item["name"]?.ToString() ?? "",
                        Port = int.TryParse(item["port"]?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int p) ? p : 0,
                        Scheme = item["scheme"]?.ToString() ?? "http"
                    });
                }
            }

            lock (_gate)
            {
                _rules.Clear();
                _rules.AddRange(parsed.Where(r => r.Port > 0 && r.Port <= 65535));
                _token = obj["token"]?.ToString() ?? "";
                _enabled = obj["enabled"]?.GetValue<bool>() ?? false;
            }
        }
        catch
        {
            // 配置损坏：保留内存默认值。
        }
    }

    public void Save()
    {
        try
        {
            var root = new JsonObject();
            var array = new JsonArray();
            lock (_gate)
            {
                root["token"] = _token;
                root["enabled"] = _enabled;
                foreach (TunnelRule rule in _rules)
                {
                    array.Add(new JsonObject
                    {
                        ["name"] = rule.Name,
                        ["port"] = rule.Port,
                        ["scheme"] = rule.Scheme
                    });
                }
            }

            root["rules"] = array;
            File.WriteAllText(ConfigFile, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
        }
        catch
        {
            // 忽略。
        }
    }

    public void SetToken(string token, bool enabled = true)
    {
        lock (_gate)
        {
            _token = token ?? "";
            _enabled = enabled;
        }

        Save();
    }

    public void SetEnabled(bool enabled)
    {
        lock (_gate)
        {
            _enabled = enabled;
        }

        Save();
    }

    public void AddRule(string name, int port, string scheme = "http")
    {
        if (port <= 0 || port > 65535)
        {
            return;
        }

        lock (_gate)
        {
            TunnelRule? existing = _rules.FirstOrDefault(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                _rules.Add(new TunnelRule { Name = PathSafety.Sanitize(name, "service"), Port = port, Scheme = scheme });
            }
            else
            {
                existing.Port = port;
                existing.Scheme = scheme;
            }
        }

        Save();
    }

    public void RemoveRule(string name)
    {
        lock (_gate)
        {
            _rules.RemoveAll(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        Save();
    }

    /// <summary>生成 cloudflared.yml；ingress 由当前规则生成，末尾必须有 404 兜底。</summary>
    public string GenerateConfigFile()
    {
        var sb = new StringBuilder();
        sb.AppendLine("# 由 AI 课堂助手生成，规则来自 data/config/tunnel.json");
        sb.AppendLine("ingress:");
        IReadOnlyList<TunnelRule> snapshot = Rules;
        if (snapshot.Count == 0)
        {
            sb.AppendLine("  - service: http_status:404");
        }
        else
        {
            foreach (TunnelRule rule in snapshot)
            {
                string host = PathSafety.Sanitize(rule.Name, "service");
                sb.AppendLine($"  - hostname: {host}.trycloudflare.com");
                sb.AppendLine($"    service: {rule.Scheme}://127.0.0.1:{rule.Port.ToString(CultureInfo.InvariantCulture)}");
            }

            sb.AppendLine("  - service: http_status:404");
        }

        try
        {
            File.WriteAllText(YamlFile, sb.ToString(), Encoding.UTF8);
        }
        catch
        {
            // 忽略。
        }

        return YamlFile;
    }

    /// <summary>构造启动参数（供日志/诊断）。</summary>
    public string BuildArguments()
    {
        TunnelRule[] snapshot = Rules.ToArray();
        var parts = new List<string> { "tunnel" };
        lock (_gate)
        {
            if (!string.IsNullOrWhiteSpace(_token))
            {
                parts.Add("--no-autoupdate");
                parts.Add("--token");
                parts.Add("<token>");
                parts.Add("--config");
                parts.Add(YamlFile);
                parts.Add("run");
            }
            else
            {
                foreach (TunnelRule rule in snapshot)
                {
                    parts.Add("--url");
                    parts.Add($"{rule.Scheme}://127.0.0.1:{rule.Port.ToString(CultureInfo.InvariantCulture)}");
                }
            }
        }

        return string.Join(" ", parts);
    }

    /// <summary>启动 cloudflared；exe 不存在或未启用返回 false。</summary>
    public bool Start()
    {
        if (!Enabled || IsRunning)
        {
            return false;
        }

        string? exe = ResolveExe(CloudflaredExe);
        if (exe is null)
        {
            return false;
        }

        string configPath = GenerateConfigFile();
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = false,
            RedirectStandardError = false
        };
        psi.ArgumentList.Add("tunnel");
        lock (_gate)
        {
            if (!string.IsNullOrWhiteSpace(_token))
            {
                psi.ArgumentList.Add("--no-autoupdate");
                psi.ArgumentList.Add("--token");
                psi.ArgumentList.Add(_token);
                psi.ArgumentList.Add("--config");
                psi.ArgumentList.Add(configPath);
                psi.ArgumentList.Add("run");
            }
            else
            {
                foreach (TunnelRule rule in _rules)
                {
                    psi.ArgumentList.Add("--url");
                    psi.ArgumentList.Add($"{rule.Scheme}://127.0.0.1:{rule.Port.ToString(CultureInfo.InvariantCulture)}");
                }
            }
        }

        try
        {
            _cloudflared = Process.Start(psi);
            return _cloudflared is not null;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>启动 OpenList 文件服务；exe 不存在返回 false。</summary>
    public bool StartOpenList()
    {
        string? exe = ResolveExe(OpenListExe);
        if (exe is null)
        {
            return false;
        }

        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = _config.DataRoot
        };
        psi.ArgumentList.Add("server");
        psi.ArgumentList.Add("--port");
        psi.ArgumentList.Add(_config.Get("openlist_port", 5244).ToString(CultureInfo.InvariantCulture));
        try
        {
            _openlist = Process.Start(psi);
            return _openlist is not null;
        }
        catch
        {
            return false;
        }
    }

    public void Stop()
    {
        Kill(_cloudflared);
        _cloudflared = null;
        Kill(_openlist);
        _openlist = null;
    }

    public void Dispose()
    {
        Stop();
    }

    private static void Kill(Process? process)
    {
        if (process is null)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill(true);
            }

            process.Dispose();
        }
        catch
        {
            // 忽略。
        }
    }

    private static string? ResolveExe(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        try
        {
            if (Path.IsPathRooted(name) && File.Exists(name))
            {
                return name;
            }

            string? path = Environment.GetEnvironmentVariable("PATH");
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }

            foreach (string dir in path.Split(Path.PathSeparator))
            {
                string candidate = Path.Combine(dir, name);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }
        catch
        {
            // 忽略。
        }

        return null;
    }
}
