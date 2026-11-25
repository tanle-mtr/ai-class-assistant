using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Win32;

namespace AiClassAssistant.Core.Configuration;

/// <summary>硬件摘要（对应旧 config.py 的 HardwareInfo，字段名保持兼容）。</summary>
public sealed class HardwareInfo
{
    public string Cpu { get; set; } = "";
    public int Cores { get; set; }
    public double RamGb { get; set; }
    public string Gpu { get; set; } = "";
    public double VramGb { get; set; }

    /// <summary>旧字段名兼容。</summary>
    public string GpuName => Gpu;

    /// <summary>旧字段名兼容。</summary>
    public bool HasNvidiaGpu
    {
        get
        {
            string name = (Gpu ?? "").ToLowerInvariant();
            return name.Contains("nvidia", StringComparison.Ordinal)
                   || name.Contains("geforce", StringComparison.Ordinal)
                   || name.Contains("rtx", StringComparison.Ordinal)
                   || name.Contains("quadro", StringComparison.Ordinal);
        }
    }

    /// <summary>按硬件给出模型规格建议。</summary>
    public string ModelAdvice()
    {
        if (HasNvidiaGpu && VramGb >= 8)
        {
            return "建议 7b~14b 级模型（如 qwen2.5:7b / 14b）";
        }

        if (RamGb >= 16)
        {
            return "无独显/NVIDIA 显存较小，建议 1.5b~3b 级模型（CPU 推理）";
        }

        return "内存偏小，建议 0.6b~1.5b 级轻量模型";
    }
}

/// <summary>
/// 配置单例：读取/写入 data/config/config.json，缺键用 DEFAULTS 补齐；
/// 未知键保留在内存中并在保存时原样写回（不覆盖、不删除用户数据）。
/// </summary>
public sealed class AppConfig
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private static readonly string[] DataDirs =
    {
        "config", "exports", "recordings", "seats", "courseware", "logs", "textbooks", "faces"
    };

    private const string DefaultJson = """
        {
          "model": "",
          "vision_model": "",
          "asr_model": "small",
          "grade": "",
          "region": "",
          "school": "",
          "class_name": "",
          "classisland_bridge_port": 18761,
          "classisland_conf_dir": "",
          "web_port": 18760,
          "monitor_enabled": true,
          "record_camera": true,
          "record_audio": true,
          "monitor_always_record": false,
          "retention_days": 14,
          "screen_analysis_interval": 30,
          "overtime_check_seconds": 120,
          "overtime_screen_threshold": 0.6,
          "overtime_seated_ratio": 0.7,
          "db_sample_interval": 1,
          "db_windows": 60,
          "openlist_port": 5244,
          "tunnel_enabled": false,
          "tunnel_token": "",
          "public_ip_auto": true,
          "courseware_subjects": [],
          "teacher_name": "",
          "output_language": "zh-CN"
        }
        """;

    private readonly JsonObject _data;
    private readonly object _gate = new();
    private string? _configFile;

    /// <summary>全局唯一实例。</summary>
    public static AppConfig Instance { get; } = new();

    /// <summary>数据根目录（优先定位已有 data/，兼容旧版用户数据）。</summary>
    public string DataRoot { get; }

    /// <summary>旧版 web 端口记录文件；v3 不再监听，仅保留兼容读取。</summary>
    public string WebPortFile => Path.Combine(Dir("config"), "web_port");

    /// <summary>硬件探测结果。</summary>
    public HardwareInfo Hardware { get; }

    private AppConfig()
    {
        DataRoot = FindDataRoot();
        foreach (string dir in DataDirs)
        {
            TryCreateDirectory(Path.Combine(DataRoot, dir));
        }

        JsonNode? parsed = null;
        try
        {
            parsed = JsonNode.Parse(DefaultJson);
        }
        catch (JsonException)
        {
            // 常量本身必然合法，这里仅为防御。
        }

        _data = parsed as JsonObject ?? new JsonObject();
        Hardware = DetectHardware();
        Load();
    }

    /// <summary>读取配置：已有键（含未知键）覆盖默认值，缺键保持默认；不抛异常。</summary>
    public void Load()
    {
        string file = ConfigFile;
        if (!File.Exists(file))
        {
            return;
        }

        try
        {
            JsonNode? node = JsonNode.Parse(File.ReadAllText(file, Encoding.UTF8));
            if (node is not JsonObject saved)
            {
                return;
            }

            lock (_gate)
            {
                foreach ((string key, JsonNode? value) in saved)
                {
                    if (value is null)
                    {
                        _data[key] = null;
                    }
                    else
                    {
                        _data[key] = value.DeepClone();
                    }
                }
            }
        }
        catch
        {
            // 配置损坏时使用默认值，绝不阻断启动。
        }
    }

    /// <summary>保存配置（含未知键原样回写）；失败静默降级。</summary>
    public void Save()
    {
        try
        {
            string file = ConfigFile;
            string? parent = Path.GetDirectoryName(file);
            if (!string.IsNullOrEmpty(parent))
            {
                TryCreateDirectory(parent);
            }

            string json;
            lock (_gate)
            {
                json = _data.ToJsonString(JsonOptions);
            }

            File.WriteAllText(file, json, new UTF8Encoding(false));
        }
        catch
        {
            // 无权限/只读盘：保持内存态可用。
        }
    }

    /// <summary>泛型读取；类型不匹配或键不存在时返回 fallback。</summary>
    public T Get<T>(string key, T fallback)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return fallback;
        }

        lock (_gate)
        {
            if (!_data.TryGetPropertyValue(key, out JsonNode? node) || node is null)
            {
                return fallback;
            }

            try
            {
                T? value = node.Deserialize<T>(JsonOptions);
                return value is null ? fallback : value;
            }
            catch
            {
                return fallback;
            }
        }
    }

    /// <summary>设置并立即持久化；序列化失败时仅更新内存。</summary>
    public void Set<T>(string key, T value)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        JsonNode? node;
        try
        {
            node = value is null ? null : JsonSerializer.SerializeToNode(value, JsonOptions);
        }
        catch
        {
            return;
        }

        lock (_gate)
        {
            _data[key] = node;
        }

        Save();
    }

    /// <summary>获取 data/{name} 子目录并自动创建；非法名字回退为 logs。</summary>
    public string Dir(string name)
    {
        string clean = (name ?? "").Replace('\\', '/').Split('/').LastOrDefault() ?? "";
        if (clean.Length == 0 || clean == "." || clean == "..")
        {
            clean = "logs";
        }

        string path = Path.Combine(DataRoot, clean);
        TryCreateDirectory(path);
        return path;
    }

    private string ConfigFile => _configFile ??= Path.Combine(Dir("config"), "config.json");

    private static void TryCreateDirectory(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
        }
        catch
        {
            // 只读环境：忽略。
        }
    }

    /// <summary>
    /// 从程序目录/当前目录向上寻找已有的 data/ 目录；找不到时退化为程序目录下的 data/。
    /// </summary>
    private static string FindDataRoot()
    {
        var candidates = new List<string>();
        string? dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && !string.IsNullOrEmpty(dir); i++, dir = Path.GetDirectoryName(dir))
        {
            candidates.Add(dir);
        }

        dir = Directory.GetCurrentDirectory();
        for (int i = 0; i < 6 && !string.IsNullOrEmpty(dir); i++, dir = Path.GetDirectoryName(dir))
        {
            candidates.Add(dir);
        }

        foreach (string candidate in candidates)
        {
            string data = Path.Combine(candidate, "data");
            try
            {
                if (Directory.Exists(data))
                {
                    return data;
                }
            }
            catch
            {
                // 忽略无效路径。
            }
        }

        return Path.Combine(AppContext.BaseDirectory, "data");
    }

    // ---------- 硬件探测（纯 BCL，无新增 NuGet） ----------

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx status);

    private static HardwareInfo DetectHardware()
    {
        var info = new HardwareInfo();
        try
        {
            info.Cpu = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER")
                       ?? Environment.GetEnvironmentVariable("PROCESSOR_NAME")
                       ?? "";
            info.Cores = Environment.ProcessorCount;
        }
        catch
        {
            info.Cores = 1;
        }

        try
        {
            var status = new MemoryStatusEx { dwLength = (uint)Marshal.SizeOf<MemoryStatusEx>() };
            if (GlobalMemoryStatusEx(ref status))
            {
                info.RamGb = Math.Round(status.ullTotalPhys / 1024.0 / 1024.0 / 1024.0, 1);
            }
        }
        catch
        {
            // 非 Windows 或权限不足：保持 0。
        }

        if (OperatingSystem.IsWindows())
        {
            try
            {
                const string displayGuid = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";
                using RegistryKey? root = Registry.LocalMachine.OpenSubKey(displayGuid);
                if (root is not null)
                {
                    foreach (string sub in root.GetSubKeyNames())
                    {
                        if (!int.TryParse(sub, NumberStyles.None, CultureInfo.InvariantCulture, out _))
                        {
                            continue;
                        }

                        using RegistryKey? key = root.OpenSubKey(sub);
                        if (key is null)
                        {
                            continue;
                        }

                        string name = key.GetValue("DriverDesc")?.ToString() ?? "";
                        if (name.Length == 0)
                        {
                            continue;
                        }

                        info.Gpu = name;
                        object? vram = key.GetValue("HardwareInformation.qwMemorySize") ?? key.GetValue("AdapterRAM");
                        if (vram is not null
                            && double.TryParse(
                                Convert.ToString(vram, CultureInfo.InvariantCulture),
                                NumberStyles.Any, CultureInfo.InvariantCulture, out double bytes))
                        {
                            info.VramGb = Math.Round(bytes / 1024.0 / 1024.0 / 1024.0, 1);
                        }

                        break;
                    }
                }
            }
            catch
            {
                // 注册表不可读时保持默认。
            }
        }

        return info;
    }
}
