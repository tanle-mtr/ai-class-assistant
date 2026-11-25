using System.Reflection;
using System.Text;

namespace AiClassAssistant.Core.Services;

/// <summary>一个技能（skills/*.md，含 frontmatter）。</summary>
public sealed class SkillInfo
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Body { get; set; } = "";
    public string FilePath { get; set; } = "";
}

/// <summary>
/// 技能库：加载 Assets/skills/*.md，解析 frontmatter，拼装系统提示词。
/// 文件缺失时回退到程序集内嵌资源；两者都没有时返回通用提示词，不抛异常。
/// </summary>
public sealed class SkillLibrary
{
    private const string ResourcePrefix = "AiClassAssistant.Core.Skills.";
    private readonly Dictionary<string, SkillInfo> _skills = new(StringComparer.OrdinalIgnoreCase);

    public SkillLibrary() : this(null)
    {
    }

    /// <param name="skillsDirectory">显式技能目录；为空时自动探测。</param>
    public SkillLibrary(string? skillsDirectory)
    {
        Load(skillsDirectory);
    }

    public IReadOnlyCollection<SkillInfo> Skills => _skills.Values;

    public SkillInfo? Get(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        _skills.TryGetValue(name.Trim(), out SkillInfo? skill);
        return skill;
    }

    /// <summary>拼装指定技能的系统提示词；技能不存在时返回通用提示词。</summary>
    public string BuildSystemPrompt(string skillName)
    {
        return BuildSystemPrompt(skillName, "");
    }

    /// <summary>拼装系统提示词并附加本次任务上下文。</summary>
    public string BuildSystemPrompt(string skillName, string context)
    {
        SkillInfo? skill = Get(skillName);
        var sb = new StringBuilder();

        if (skill is null)
        {
            sb.AppendLine("你是 AI 课堂助手，服务于中小学课堂教学场景。");
            sb.AppendLine("请依据用户提供的课堂素材完成任务，输出结构清晰的中文 Markdown，不要编造不存在的事实。");
            if (!string.IsNullOrWhiteSpace(skillName))
            {
                sb.AppendLine();
                sb.AppendLine($"提示：技能“{skillName}”未找到，已使用通用提示词。");
            }
        }
        else
        {
            sb.AppendLine("你是 AI 课堂助手。请严格遵循以下技能说明完成本次任务。");
            sb.AppendLine();
            sb.AppendLine($"# 技能：{skill.Name}");
            if (!string.IsNullOrWhiteSpace(skill.Description))
            {
                sb.AppendLine($"技能描述：{skill.Description}");
            }

            sb.AppendLine();
            sb.AppendLine(skill.Body);
            sb.AppendLine();
            sb.AppendLine("输出要求：使用中文 Markdown，层次清楚、内容具体；只输出任务要求的文档，不要解释你的工作方式。");
        }

        if (!string.IsNullOrWhiteSpace(context))
        {
            sb.AppendLine();
            sb.AppendLine("## 本次任务上下文");
            sb.AppendLine(context);
        }

        return sb.ToString();
    }

    private void Load(string? explicitDirectory)
    {
        string? dir = FindSkillsDirectory(explicitDirectory);
        if (!string.IsNullOrEmpty(dir))
        {
            try
            {
                foreach (string file in Directory.GetFiles(dir, "*.md", SearchOption.TopDirectoryOnly))
                {
                    Add(Parse(File.ReadAllText(file, Encoding.UTF8), Path.GetFileNameWithoutExtension(file), file));
                }
            }
            catch
            {
                // 文件读取失败：继续尝试内嵌资源。
            }
        }

        if (_skills.Count == 0)
        {
            LoadEmbedded();
        }
    }

    private void Add(SkillInfo skill)
    {
        if (!string.IsNullOrWhiteSpace(skill.Name))
        {
            _skills[skill.Name.Trim()] = skill;
        }
    }

    private static SkillInfo Parse(string text, string fallbackName, string filePath)
    {
        var skill = new SkillInfo { Name = fallbackName, Body = text.Trim(), FilePath = filePath };
        if (!text.StartsWith("---", StringComparison.Ordinal))
        {
            return skill;
        }

        int end = text.IndexOf("\n---", 3, StringComparison.Ordinal);
        if (end <= 0)
        {
            return skill;
        }

        string front = text[3..end];
        string rest = text[(end + 4)..].TrimStart('\r', '\n');
        skill.Body = rest.Trim();

        foreach (string rawLine in front.Split('\n'))
        {
            string line = rawLine.Trim().TrimStart('\r');
            if (line.StartsWith("name:", StringComparison.OrdinalIgnoreCase))
            {
                string name = line[5..].Trim().Trim('"', '\'');
                if (name.Length > 0)
                {
                    skill.Name = name;
                }
            }
            else if (line.StartsWith("description:", StringComparison.OrdinalIgnoreCase))
            {
                skill.Description = line[11..].Trim().Trim('"', '\'');
            }
        }

        return skill;
    }

    private static string? FindSkillsDirectory(string? explicitDirectory)
    {
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(explicitDirectory))
        {
            candidates.Add(explicitDirectory!);
        }

        candidates.Add(Path.Combine(AppContext.BaseDirectory, "skills"));
        candidates.Add(Path.Combine(AppContext.BaseDirectory, "Assets", "skills"));

        string? dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && !string.IsNullOrEmpty(dir); i++, dir = Path.GetDirectoryName(dir))
        {
            candidates.Add(Path.Combine(dir, "src", "Core", "Assets", "skills"));
            candidates.Add(Path.Combine(dir, "Assets", "skills"));
        }

        string cwd = Directory.GetCurrentDirectory();
        candidates.Add(Path.Combine(cwd, "src", "Core", "Assets", "skills"));
        candidates.Add(Path.Combine(cwd, "Assets", "skills"));

        foreach (string candidate in candidates)
        {
            try
            {
                if (Directory.Exists(candidate) && Directory.GetFiles(candidate, "*.md").Length > 0)
                {
                    return candidate;
                }
            }
            catch
            {
                // 忽略无效目录。
            }
        }

        return null;
    }

    private void LoadEmbedded()
    {
        try
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            foreach (string name in assembly.GetManifestResourceNames())
            {
                if (!name.StartsWith(ResourcePrefix, StringComparison.Ordinal) || !name.EndsWith(".md", StringComparison.Ordinal))
                {
                    continue;
                }

                using Stream? stream = assembly.GetManifestResourceStream(name);
                if (stream is null)
                {
                    continue;
                }

                using var reader = new StreamReader(stream, Encoding.UTF8);
                string file = name[ResourcePrefix.Length..];
                Add(Parse(reader.ReadToEnd(), Path.GetFileNameWithoutExtension(file), $"embedded:{name}"));
            }
        }
        catch
        {
            // 内嵌资源也失败时保持空技能库（BuildSystemPrompt 会走通用提示词）。
        }
    }
}
