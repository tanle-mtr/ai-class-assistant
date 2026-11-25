using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AiClassAssistant.Core.Ai;

/// <summary>/api/tags 中的一个模型条目。</summary>
public sealed class ModelInfo
{
    public string Name { get; set; } = "";
    public string Id { get; set; } = "";
    public string Size { get; set; } = "";
    public string Modified { get; set; } = "";

    /// <summary>去 tag、去域名后的归一化名（qwen3:1.7b → qwen3）。</summary>
    public string BaseName => NormalizeName(Name);

    /// <summary>模型名归一化：忽略 host/前缀、tag 与大小写。</summary>
    public static string NormalizeName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "";
        }

        string value = name.Trim();
        int slash = value.LastIndexOf('/');
        if (slash >= 0)
        {
            value = value[(slash + 1)..];
        }

        int colon = value.IndexOf(':');
        if (colon >= 0)
        {
            value = value[..colon];
        }

        return value.Trim().ToLowerInvariant();
    }

    /// <summary>比较两个模型名（去 tag 后）是否指向同一模型。</summary>
    public static bool SameModel(string? a, string? b)
    {
        string na = NormalizeName(a);
        string nb = NormalizeName(b);
        return na.Length > 0 && na == nb;
    }

    /// <summary>粗略判断是否多模态（视觉）模型。</summary>
    public static bool IsVision(string? name)
    {
        string low = (name ?? "").ToLowerInvariant();
        return new[] { "vl", "vision", "llava", "gemma3", "minicpm", "moondream", "bakllava" }
            .Any(k => low.Contains(k, StringComparison.Ordinal));
    }
}

/// <summary>
/// Ollama HTTP 客户端：所有外部失败都返回空值/错误文本，绝不向调用方抛异常。
/// 超时 8 秒（不启动 Ollama 时界面能快速降级）。
/// </summary>
public sealed class OllamaClient : IOllamaClient
{
    /// <summary>AI 不可用时的统一提示文本（界面据此显示「AI 服务不可用」）。</summary>
    public const string UnavailableText = "AI 服务不可用：无法连接 Ollama，请先启动 Ollama 服务。";

    public const string DefaultBaseUrl = "http://127.0.0.1:11434";

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(8);

    private readonly HttpClient _http;
    private readonly string _baseUrl;

    public OllamaClient() : this(DefaultBaseUrl)
    {
    }

    public OllamaClient(string? baseUrl)
    {
        _baseUrl = (string.IsNullOrWhiteSpace(baseUrl) ? DefaultBaseUrl : baseUrl!).TrimEnd('/');
        _http = new HttpClient { Timeout = RequestTimeout };
    }

    /// <summary>列出已安装模型；Ollama 未启动时返回空列表。</summary>
    public async Task<List<ModelInfo>> ListModelsAsync()
    {
        try
        {
            using HttpResponseMessage resp = await _http.GetAsync(_baseUrl + "/api/tags").ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                return new List<ModelInfo>();
            }

            string text = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            JsonNode? root = JsonNode.Parse(text);
            if (root?["models"] is not JsonArray array)
            {
                return new List<ModelInfo>();
            }

            var result = new List<ModelInfo>();
            foreach (JsonNode? item in array)
            {
                if (item is not JsonObject obj)
                {
                    continue;
                }

                result.Add(new ModelInfo
                {
                    Name = obj["name"]?.ToString() ?? "",
                    Id = obj["id"]?.ToString() ?? "",
                    Size = obj["size"]?.ToString() ?? "",
                    Modified = obj["modified"]?.ToString() ?? ""
                });
            }

            return result;
        }
        catch
        {
            return new List<ModelInfo>();
        }
    }

    /// <summary>文本生成；失败时返回明确的错误文本（不是异常）。</summary>
    public async Task<string> GenerateAsync(string model, string prompt, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(model) || string.IsNullOrWhiteSpace(prompt))
        {
            return UnavailableText;
        }

        JsonNode? root = await PostAsync(
            "/api/generate",
            new { model, prompt, stream = false },
            ct).ConfigureAwait(false);

        try
        {
            string? text = root?["response"]?.ToString();
            return string.IsNullOrWhiteSpace(text) ? UnavailableText : text!;
        }
        catch
        {
            return UnavailableText;
        }
    }

    /// <summary>文本向量化；失败返回空向量。</summary>
    public async Task<List<double>> EmbedAsync(string model, string text, CancellationToken ct = default)
    {
        var result = new List<double>();
        if (string.IsNullOrWhiteSpace(model) || string.IsNullOrWhiteSpace(text))
        {
            return result;
        }

        JsonNode? root = await PostAsync(
            "/api/embed",
            new { model, input = text },
            ct).ConfigureAwait(false);

        try
        {
            if (root?["embeddings"] is JsonArray array && array.Count > 0 && array[0] is JsonArray vector)
            {
                foreach (JsonNode? node in vector)
                {
                    if (node is null)
                    {
                        continue;
                    }

                    if (double.TryParse(node.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
                    {
                        result.Add(v);
                    }
                }
            }
        }
        catch
        {
            return new List<double>();
        }

        return result;
    }

    /// <summary>模型是否已安装（去 tag 比较）；Ollama 不可用时返回 false。</summary>
    public async Task<bool> EnsureModelAsync(string model)
    {
        if (string.IsNullOrWhiteSpace(model))
        {
            return false;
        }

        List<ModelInfo> models = await ListModelsAsync().ConfigureAwait(false);
        if (models.Count == 0)
        {
            return false;
        }

        return models.Any(m => ModelInfo.SameModel(m.Name, model) || m.Name == model);
    }

    /// <summary>用 Modelfile 方式（from + system）创建定制模型。</summary>
    public async Task<bool> CreateModelAsync(string name, string baseModel, string systemPrompt)
    {
        if (string.IsNullOrWhiteSpace(name)
            || string.IsNullOrWhiteSpace(baseModel)
            || string.IsNullOrWhiteSpace(systemPrompt))
        {
            return false;
        }

        JsonNode? root = await PostAsync(
            "/api/create",
            new { name, from = baseModel, system = systemPrompt, stream = false },
            CancellationToken.None).ConfigureAwait(false);

        if (root is null)
        {
            return false;
        }

        try
        {
            string? error = root["error"]?.ToString();
            return string.IsNullOrWhiteSpace(error);
        }
        catch
        {
            return false;
        }
    }

    private async Task<JsonNode?> PostAsync(string path, object payload, CancellationToken ct)
    {
        try
        {
            string json = JsonSerializer.Serialize(payload);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using HttpResponseMessage resp = await _http.PostAsync(_baseUrl + path, content, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                return null;
            }

            string text = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text);
        }
        catch
        {
            // 连接失败/超时/取消/JSON 异常统一降级。
            return null;
        }
    }

    public void Dispose()
    {
        _http.Dispose();
    }
}
