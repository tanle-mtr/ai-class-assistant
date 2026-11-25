using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using AiClassAssistant.Core.Ai;
using AiClassAssistant.Core.Configuration;

namespace AiClassAssistant.Core.Services;

/// <summary>教材条目（纯数据目录，不做网络下载）。</summary>
public sealed class TextbookInfo
{
    public string Title { get; set; } = "";
    public string Grade { get; set; } = "";
    public string Region { get; set; } = "";
    public string Publisher { get; set; } = "";
    public string FilePath { get; set; } = "";
}

/// <summary>教材检索命中。</summary>
public sealed class TextbookHit
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Text { get; set; } = "";
    public double Score { get; set; }
}

/// <summary>
/// PDF 文本提取扩展点：BCL 内置实现只覆盖常见文本型 PDF；
/// 字体子集/CID/加密 PDF 请由接入方实现此接口并注入（本工程不引入新 NuGet）。
/// </summary>
public interface IPdfTextExtractor
{
    string Extract(string path);
}

/// <summary>
/// 教材服务：年级/地区 → 书单（纯数据）；PDF 文本提取（轻量内置实现 + 可注入）；
/// 纯 C# 词频哈希向量 + 余弦相似检索；问答调 OllamaClient。
/// </summary>
public sealed class TextbookService
{
    private const int VectorDimension = 256;

    private sealed class IndexEntry
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string Text { get; set; } = "";
        public double[] Vector { get; set; } = Array.Empty<double>();
    }

    private readonly AppConfig _config;
    private readonly IOllamaClient _ollama;
    private readonly SkillLibrary? _skills;
    private readonly object _gate = new();
    private readonly Dictionary<string, IndexEntry> _index = new(StringComparer.OrdinalIgnoreCase);
    private IPdfTextExtractor? _extractor;

    public TextbookService(AppConfig config, IOllamaClient ollama) : this(config, ollama, null)
    {
    }

    public TextbookService(AppConfig config, IOllamaClient ollama, SkillLibrary? skills)
    {
        _config = config;
        _ollama = ollama;
        _skills = skills;
        LoadIndex();
    }

    public string IndexFile => Path.Combine(_config.Dir("textbooks"), "index.json");

    /// <summary>注入自定义 PDF 提取器；null 恢复内置轻量实现。</summary>
    public void SetExtractor(IPdfTextExtractor? extractor)
    {
        _extractor = extractor;
    }

    /// <summary>支持的年级列表。</summary>
    public static IReadOnlyList<string> Grades => new[]
    {
        "一年级", "二年级", "三年级", "四年级", "五年级", "六年级",
        "七年级", "八年级", "九年级", "高一", "高二", "高三"
    };

    /// <summary>支持的地区列表（教材版本由地区决定）。</summary>
    public static IReadOnlyList<string> Regions => new[] { "全国", "广东", "江苏", "北京", "浙江", "上海" };

    /// <summary>按年级 + 地区匹配教材清单（纯数据，无网络）。</summary>
    public List<TextbookInfo> GetCatalog(string grade, string region)
    {
        string publisher = region switch
        {
            "广东" => "粤教版/人教版",
            "江苏" => "苏教版",
            "北京" => "北师大版",
            "浙江" => "浙教版",
            "上海" => "沪教版",
            _ => "人教版"
        };

        string[] subjects = grade switch
        {
            "一年级" or "二年级" or "三年级" or "四年级" or "五年级" or "六年级" =>
                new[] { "语文", "数学", "英语", "科学", "道德与法治" },
            "七年级" or "八年级" or "九年级" =>
                new[] { "语文", "数学", "英语", "物理", "化学", "生物", "历史", "地理", "道德与法治" },
            _ => new[] { "语文", "数学", "英语", "物理", "化学", "生物", "历史", "地理", "思想政治" }
        };

        return subjects.Select(subject => new TextbookInfo
        {
            Grade = grade,
            Region = string.IsNullOrWhiteSpace(region) ? "全国" : region,
            Publisher = publisher,
            Title = $"{grade}{subject}（{publisher}）"
        }).ToList();
    }

    /// <summary>按配置中的 grade/region 自动匹配（兼容旧配置「初中/高中/广州」等粗粒度值）。</summary>
    public List<TextbookInfo> MatchConfigured()
    {
        string grade = _config.Get("grade", "");
        string region = NormalizeRegion(_config.Get("region", ""));
        if (string.IsNullOrWhiteSpace(grade))
        {
            return new List<TextbookInfo>();
        }

        string[] grades = grade switch
        {
            "小学" => new[] { "一年级", "二年级", "三年级", "四年级", "五年级", "六年级" },
            "初中" => new[] { "七年级", "八年级", "九年级" },
            "高中" => new[] { "高一", "高二", "高三" },
            _ => new[] { grade }
        };

        return grades.SelectMany(g => GetCatalog(g, region)).ToList();
    }

    /// <summary>把旧配置中的城市名/省份名归一到教材版本地区。</summary>
    private static string NormalizeRegion(string region)
    {
        string value = (region ?? "").Trim();
        if (value.Contains("广州", StringComparison.Ordinal)
            || value.Contains("深圳", StringComparison.Ordinal)
            || value.Contains("广东", StringComparison.Ordinal)
            || value.Contains("粤", StringComparison.Ordinal))
        {
            return "广东";
        }

        if (value.Contains("江苏", StringComparison.Ordinal) || value.Contains("南京", StringComparison.Ordinal))
        {
            return "江苏";
        }

        if (value.Contains("北京", StringComparison.Ordinal))
        {
            return "北京";
        }

        if (value.Contains("浙江", StringComparison.Ordinal) || value.Contains("杭州", StringComparison.Ordinal))
        {
            return "浙江";
        }

        if (value.Contains("上海", StringComparison.Ordinal))
        {
            return "上海";
        }

        return string.IsNullOrEmpty(value) ? "全国" : value;
    }

    /// <summary>提取 PDF 文本；失败返回空串。</summary>
    public string ExtractPdf(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return "";
        }

        try
        {
            return _extractor?.Extract(path) ?? SimplePdfTextExtractor.Extract(path);
        }
        catch
        {
            return "";
        }
    }

    /// <summary>建立/更新本地知识库索引。</summary>
    public void Index(string id, string title, string text)
    {
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        lock (_gate)
        {
            _index[id] = new IndexEntry
            {
                Id = id,
                Title = title ?? "",
                Text = text,
                Vector = Vectorize(text)
            };
        }

        SaveIndex();
    }

    /// <summary>纯 C# 余弦相似检索。</summary>
    public List<TextbookHit> Search(string query, int topK = 4)
    {
        var result = new List<TextbookHit>();
        if (string.IsNullOrWhiteSpace(query))
        {
            return result;
        }

        double[] q = Vectorize(query);
        lock (_gate)
        {
            foreach (IndexEntry entry in _index.Values)
            {
                double score = Cosine(q, entry.Vector);
                if (score <= 0)
                {
                    continue;
                }

                result.Add(new TextbookHit
                {
                    Id = entry.Id,
                    Title = entry.Title,
                    Text = entry.Text,
                    Score = Math.Round(score, 4)
                });
            }
        }

        return result.OrderByDescending(h => h.Score).Take(Math.Max(1, topK)).ToList();
    }

    /// <summary>教材问答：先检索本地知识库，再调技能提示词 + Ollama。</summary>
    public async Task<string> AnswerAsync(string question, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(question))
        {
            return "请输入问题。";
        }

        List<TextbookHit> hits = Search(question, 4);
        if (hits.Count == 0)
        {
            return "本地教材知识库为空，请先导入教材并建立索引（或检查教材匹配是否正确）。";
        }

        var context = new StringBuilder();
        foreach (TextbookHit hit in hits)
        {
            context.AppendLine($"【教材：{hit.Title}】");
            context.AppendLine(hit.Text.Length > 1500 ? hit.Text[..1500] : hit.Text);
            context.AppendLine();
        }

        string model = _config.Get("model", "");
        if (string.IsNullOrWhiteSpace(model))
        {
            return OllamaClient.UnavailableText;
        }

        try
        {
            string prompt = _skills is not null
                ? _skills.BuildSystemPrompt("textbook-qa", context.ToString())
                : "你是教材问答助手，优先依据提供的教材原文回答，不要编造。\n\n" + context;
            prompt += "\n\n问题：" + question;
            return await _ollama.GenerateAsync(model, prompt, ct).ConfigureAwait(false);
        }
        catch
        {
            return OllamaClient.UnavailableText;
        }
    }

    /// <summary>词频哈希向量（FNV-1a + L2 归一化），无 ML 依赖。</summary>
    internal static double[] Vectorize(string text)
    {
        var vector = new double[VectorDimension];
        if (string.IsNullOrWhiteSpace(text))
        {
            return vector;
        }

        foreach (string token in Tokens(text))
        {
            uint hash = 2166136261;
            foreach (char c in token)
            {
                hash ^= c;
                hash *= 16777619;
            }

            vector[hash % VectorDimension] += 1.0;
        }

        double norm = Math.Sqrt(vector.Sum(v => v * v));
        if (norm > 0)
        {
            for (int i = 0; i < vector.Length; i++)
            {
                vector[i] /= norm;
            }
        }

        return vector;
    }

    internal static double Cosine(double[] a, double[] b)
    {
        if (a.Length == 0 || b.Length == 0 || a.Length != b.Length)
        {
            return 0;
        }

        double dot = 0;
        for (int i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
        }

        return dot;
    }

    private static IEnumerable<string> Tokens(string text)
    {
        var word = new StringBuilder();
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (char.IsLetterOrDigit(c))
            {
                if (IsCjk(c))
                {
                    if (word.Length > 0)
                    {
                        yield return word.ToString().ToLowerInvariant();
                        word.Clear();
                    }

                    yield return c.ToString();
                    if (i + 1 < text.Length && IsCjk(text[i + 1]))
                    {
                        yield return new string(new[] { c, text[i + 1] });
                    }
                }
                else
                {
                    word.Append(c);
                }
            }
            else if (word.Length > 0)
            {
                yield return word.ToString().ToLowerInvariant();
                word.Clear();
            }
        }

        if (word.Length > 0)
        {
            yield return word.ToString().ToLowerInvariant();
        }
    }

    private static bool IsCjk(char c) => c is >= '\u4e00' and <= '\u9fff';

    private void LoadIndex()
    {
        if (!File.Exists(IndexFile))
        {
            return;
        }

        try
        {
            JsonNode? root = JsonNode.Parse(File.ReadAllText(IndexFile, Encoding.UTF8));
            if (root?["docs"] is not JsonArray docs)
            {
                return;
            }

            lock (_gate)
            {
                _index.Clear();
                foreach (JsonNode? node in docs)
                {
                    if (node is not JsonObject obj)
                    {
                        continue;
                    }

                    string id = obj["id"]?.ToString() ?? "";
                    if (string.IsNullOrWhiteSpace(id))
                    {
                        continue;
                    }

                    var vector = new List<double>();
                    if (obj["vector"] is JsonArray arr)
                    {
                        foreach (JsonNode? v in arr)
                        {
                            if (v is not null
                                && double.TryParse(v.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
                            {
                                vector.Add(d);
                            }
                        }
                    }

                    _index[id] = new IndexEntry
                    {
                        Id = id,
                        Title = obj["title"]?.ToString() ?? "",
                        Text = obj["text"]?.ToString() ?? "",
                        Vector = vector.ToArray()
                    };
                }
            }
        }
        catch
        {
            // 索引损坏时按空库处理，不删除用户文件。
        }
    }

    private void SaveIndex()
    {
        try
        {
            var root = new JsonObject();
            var docs = new JsonArray();
            lock (_gate)
            {
                foreach (IndexEntry entry in _index.Values)
                {
                    var vec = new JsonArray();
                    foreach (double d in entry.Vector)
                    {
                        vec.Add(d);
                    }

                    docs.Add(new JsonObject
                    {
                        ["id"] = entry.Id,
                        ["title"] = entry.Title,
                        ["text"] = entry.Text,
                        ["vector"] = vec
                    });
                }
            }

            root["docs"] = docs;
            string? parent = Path.GetDirectoryName(IndexFile);
            if (!string.IsNullOrEmpty(parent))
            {
                Directory.CreateDirectory(parent);
            }

            File.WriteAllText(IndexFile, root.ToJsonString(), Encoding.UTF8);
        }
        catch
        {
            // 忽略。
        }
    }
}

/// <summary>
/// 纯 BCL 的轻量 PDF 文本提取：扫描 stream/endstream，尝试 ZLib 解压，
/// 再匹配 Tj / TJ 文本操作符。仅覆盖常见文本型 PDF；复杂 PDF 由 IPdfTextExtractor 注入实现。
/// </summary>
internal static class SimplePdfTextExtractor
{
    public static string Extract(string path)
    {
        try
        {
            byte[] bytes = File.ReadAllBytes(path);
            if (bytes.Length < 8 || Encoding.ASCII.GetString(bytes, 0, 5) != "%PDF-")
            {
                return "";
            }

            var sb = new StringBuilder();
            int pos = 0;
            while (pos < bytes.Length)
            {
                int stream = IndexOf(bytes, "stream", pos, Encoding.ASCII);
                if (stream < 0)
                {
                    break;
                }

                int start = stream + 6;
                if (start < bytes.Length && bytes[start] == '\r')
                {
                    start++;
                }

                if (start < bytes.Length && bytes[start] == '\n')
                {
                    start++;
                }

                int end = IndexOf(bytes, "endstream", start, Encoding.ASCII);
                if (end < 0)
                {
                    break;
                }

                byte[] chunk = bytes[start..end];
                string decoded = TryInflate(chunk) ?? Encoding.Latin1.GetString(chunk);
                sb.Append(ExtractOperators(decoded));
                sb.Append(' ');
                pos = end + 9;
            }

            return sb.ToString().Trim();
        }
        catch
        {
            return "";
        }
    }

    private static string? TryInflate(byte[] chunk)
    {
        try
        {
            using var input = new MemoryStream(chunk);
            using var zlib = new ZLibStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            zlib.CopyTo(output);
            return Encoding.Latin1.GetString(output.ToArray());
        }
        catch
        {
            return null;
        }
    }

    private static string ExtractOperators(string decoded)
    {
        var sb = new StringBuilder();
        const string pattern = @"\((?<s>(?:\\.|[^\\()])*)\)\s*Tj|\[(?<a>[^\]]*)\]\s*TJ";
        foreach (Match match in Regex.Matches(decoded, pattern))
        {
            if (match.Groups["s"].Success)
            {
                sb.Append(Unescape(match.Groups["s"].Value));
            }
            else if (match.Groups["a"].Success)
            {
                foreach (Match inner in Regex.Matches(match.Groups["a"].Value, @"\((?<t>(?:\\.|[^\\()])*)\)"))
                {
                    sb.Append(Unescape(inner.Groups["t"].Value));
                }
            }
        }

        return sb.ToString();
    }

    private static string Unescape(string text)
    {
        return Regex.Replace(text, @"\\([nrtbf()\\])", m => m.Groups[1].Value switch
        {
            "n" => "\n",
            "r" => "\r",
            "t" => "\t",
            "b" => "\b",
            "f" => "\f",
            _ => m.Groups[1].Value
        });
    }

    private static int IndexOf(byte[] bytes, string pattern, int from, Encoding encoding)
    {
        byte[] needle = encoding.GetBytes(pattern);
        if (needle.Length == 0 || from > bytes.Length - needle.Length)
        {
            return -1;
        }

        for (int i = from; i <= bytes.Length - needle.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < needle.Length; j++)
            {
                if (bytes[i + j] != needle[j])
                {
                    match = false;
                    break;
                }
            }

            if (match)
            {
                return i;
            }
        }

        return -1;
    }
}
