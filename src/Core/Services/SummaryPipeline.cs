using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using AiClassAssistant.Core.Ai;
using AiClassAssistant.Core.Configuration;
using AiClassAssistant.Core.Models;

namespace AiClassAssistant.Core.Services;

/// <summary>
/// 课型分流总结生成（契约 §4.6）：
/// 新授课 = 老师总结 + 学生总结 + 导学案 + 思维导图；
/// 评讲课 = 老师总结 + 学生总结 + 导学案（不出思维导图）；
/// 考试/自习课 = 只有老师总结 + 分贝报告。
/// AI 不可用时用模板兜底，保证 SummaryDoc 结构完整、界面有内容渲染。
/// </summary>
public sealed class SummaryPipeline
{
    private readonly AppConfig _config;
    private readonly IOllamaClient _ollama;
    private readonly SkillLibrary _skills;

    public SummaryPipeline(AppConfig config, IOllamaClient ollama, SkillLibrary skills)
    {
        _config = config;
        _ollama = ollama;
        _skills = skills;
    }

    /// <summary>生成并解析总结；任何 AI 失败都走模板兜底，不抛异常。</summary>
    public async Task<SummaryDoc> GenerateAsync(Session session, CancellationToken ct = default)
    {
        LessonType type = session.Lesson.Type;
        string model = _config.Get("model", "");
        string context = BuildContext(session);

        string teacher;
        string student = "";
        string guide = "";
        string mindmap = "";

        if (type == LessonType.Exam)
        {
            // 考试/自习课：只出老师总结（纪律报告）+ 分贝统计。
            teacher = await AskAsync(model, "db-report", context, ct).ConfigureAwait(false);
        }
        else
        {
            var teacherTask = AskAsync(model, "classroom-summary", context, ct);
            var studentTask = AskAsync(model, "classroom-summary", context + "\n\n任务：只输出学生课堂总结（重点与难点两部分）。", ct);
            var guideTask = AskAsync(model, "guide-plan", context, ct);
            Task<string>? mindTask = type == LessonType.New
                ? AskAsync(model, "mindmap", context, ct)
                : null;

            await Task.WhenAll(new[] { teacherTask, studentTask, guideTask }
                .Concat(mindTask is null ? Array.Empty<Task<string>>() : new[] { mindTask }))
                .ConfigureAwait(false);

            teacher = teacherTask.Result;
            student = studentTask.Result;
            guide = guideTask.Result;
            mindmap = mindTask?.Result ?? "";
        }

        bool aiUp = !IsUnavailable(teacher);
        var doc = new SummaryDoc
        {
            Title = $"课堂总结 · {session.Lesson.Subject}（{LessonTypeUtil.Name(type)}）",
            Meta = BuildMeta(session),
            Stars = ParseStars(teacher),
            DbSamples = session.DbSamples.Select(s => (double)s.Db).ToList(),
            AvgDb = session.DbSamples.Count == 0
                ? 0
                : Math.Round(session.DbSamples.Average(s => s.Db), 1),
            PeakDb = session.DbSamples.Count == 0
                ? 0
                : Math.Round(session.DbSamples.Max(s => s.Db), 1)
        };

        doc.Advice = ExtractSection(teacher, new[] { "建议", "细讲", "缩短", "防拖堂" });
        if (doc.Advice.Count == 0)
        {
            doc.Advice = ExtractBullets(teacher);
        }

        doc.KeyPoints = ExtractSection(student, new[] { "重点", "知识点", "主要内容" });
        doc.Difficulties = ExtractSection(student, new[] { "难点", "易错", "困惑" });
        if (doc.KeyPoints.Count == 0)
        {
            doc.KeyPoints = ExtractBullets(teacher).Take(3).ToList();
        }

        var raw = new StringBuilder();
        raw.AppendLine("## 老师总结");
        raw.AppendLine(teacher);
        raw.AppendLine();
        if (!string.IsNullOrWhiteSpace(student))
        {
            raw.AppendLine("## 学生总结");
            raw.AppendLine(student);
            raw.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(guide))
        {
            raw.AppendLine("## 导学案");
            raw.AppendLine(guide);
            raw.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(mindmap))
        {
            raw.AppendLine("## 思维导图");
            raw.AppendLine(mindmap);
            raw.AppendLine();
        }

        doc.RawMarkdown = raw.ToString();
        doc.MindmapMarkdown = type == LessonType.New ? ExtractMindmap(mindmap) : "";

        if (!aiUp)
        {
            // AI 错误文本不能被当成建议/重点解析，清空后统一用模板内容。
            doc.Advice.Clear();
            doc.KeyPoints.Clear();
            doc.Difficulties.Clear();
            return ApplyFallback(doc, session);
        }

        if (doc.Stars <= 0)
        {
            doc.Stars = 3;
        }

        if (doc.Advice.Count == 0)
        {
            doc.Advice = DefaultAdvice(session);
        }

        if (doc.KeyPoints.Count == 0)
        {
            doc.KeyPoints = DefaultKeyPoints(session);
        }

        if (doc.Difficulties.Count == 0)
        {
            doc.Difficulties = DefaultDifficulties();
        }

        if (type == LessonType.New && string.IsNullOrWhiteSpace(doc.MindmapMarkdown))
        {
            doc.MindmapMarkdown = BuildTemplateMindmap(session);
        }

        FillOutputs(session, doc, teacher, student, guide, doc.MindmapMarkdown);
        return doc;
    }

    /// <summary>生成模板兜底文档（AI 不可用时保证结构完整）。</summary>
    public SummaryDoc GenerateFallback(Session session)
    {
        var doc = new SummaryDoc
        {
            Title = $"课堂总结 · {session.Lesson.Subject}（{LessonTypeUtil.Name(session.Lesson.Type)}）",
            Meta = BuildMeta(session),
            Stars = 4,
            DbSamples = session.DbSamples.Select(s => (double)s.Db).ToList(),
            AvgDb = session.DbSamples.Count == 0 ? 0 : Math.Round(session.DbSamples.Average(s => s.Db), 1),
            PeakDb = session.DbSamples.Count == 0 ? 0 : Math.Round(session.DbSamples.Max(s => s.Db), 1)
        };
        return ApplyFallback(doc, session);
    }

    private SummaryDoc ApplyFallback(SummaryDoc doc, Session session)
    {
        LessonType type = session.Lesson.Type;
        doc.Stars = doc.Stars > 0 ? doc.Stars : 4;
        doc.Advice = doc.Advice.Count > 0 ? doc.Advice : DefaultAdvice(session);
        doc.KeyPoints = doc.KeyPoints.Count > 0 ? doc.KeyPoints : DefaultKeyPoints(session);
        doc.Difficulties = doc.Difficulties.Count > 0 ? doc.Difficulties : DefaultDifficulties();

        if (type == LessonType.New)
        {
            doc.MindmapMarkdown = string.IsNullOrWhiteSpace(doc.MindmapMarkdown)
                ? BuildTemplateMindmap(session)
                : doc.MindmapMarkdown;
        }
        else
        {
            // 评讲课 / 考试自习课不出思维导图（契约 §4.6）。
            doc.MindmapMarkdown = "";
        }

        string fallbackText = type == LessonType.Exam
            ? BuildTemplateDbReport(doc)
            : BuildTemplateTeacherSummary(doc, session);

        var raw = new StringBuilder();
        raw.AppendLine("> AI 服务不可用，以下内容由本地模板生成。");
        raw.AppendLine();
        raw.AppendLine("## 老师总结");
        raw.AppendLine(fallbackText);
        raw.AppendLine();
        if (type != LessonType.Exam)
        {
            raw.AppendLine("## 学生总结");
            raw.AppendLine("### 重点");
            foreach (string item in doc.KeyPoints)
            {
                raw.AppendLine($"- {item}");
            }

            raw.AppendLine("### 难点");
            foreach (string item in doc.Difficulties)
            {
                raw.AppendLine($"- {item}");
            }

            raw.AppendLine();
            raw.AppendLine("## 导学案");
            raw.AppendLine(BuildTemplateGuide(session));
            raw.AppendLine();
        }

        if (type == LessonType.New)
        {
            raw.AppendLine("## 思维导图");
            raw.AppendLine(doc.MindmapMarkdown);
        }

        doc.RawMarkdown = raw.ToString();
        FillOutputs(session, doc, fallbackText, "", type == LessonType.Exam ? "" : BuildTemplateGuide(session), doc.MindmapMarkdown);
        return doc;
    }

    private static void FillOutputs(
        Session session, SummaryDoc doc, string teacher, string student, string guide, string mindmap)
    {
        session.Outputs["老师总结"] = teacher;
        if (doc.RawMarkdown.Length > 0)
        {
            session.Outputs["完整总结"] = doc.RawMarkdown;
        }

        if (session.Lesson.Type != LessonType.Exam && !string.IsNullOrWhiteSpace(student))
        {
            session.Outputs["学生总结"] = student;
        }

        if (session.Lesson.Type != LessonType.Exam && !string.IsNullOrWhiteSpace(guide))
        {
            session.Outputs["导学案"] = guide;
        }

        if (session.Lesson.Type == LessonType.New && !string.IsNullOrWhiteSpace(mindmap))
        {
            session.Outputs["思维导图"] = mindmap;
        }

        if (session.Lesson.Type == LessonType.Exam)
        {
            session.Outputs["分贝报告"] = teacher;
        }
    }

    private async Task<string> AskAsync(string model, string skillName, string context, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(model))
        {
            return OllamaClient.UnavailableText;
        }

        try
        {
            string prompt = _skills.BuildSystemPrompt(skillName, context)
                            + "\n\n请严格按上述技能要求输出中文 Markdown 文档，不要输出其它说明。";
            return await _ollama.GenerateAsync(model, prompt, ct).ConfigureAwait(false);
        }
        catch
        {
            return OllamaClient.UnavailableText;
        }
    }

    private static string BuildContext(Session session)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"科目：{session.Lesson.Subject}");
        sb.AppendLine($"班级：{session.Lesson.ClassName}");
        sb.AppendLine($"时间：{session.StartTime:yyyy-MM-dd HH:mm} — {session.EndTime:HH:mm}");
        sb.AppendLine($"课型：{LessonTypeUtil.Name(session.Lesson.Type)}");
        sb.AppendLine($"是否拖堂：{(session.OvertimeDetected ? "是" : "否")}");
        sb.AppendLine($"平均分贝：{(session.DbSamples.Count == 0 ? 0 : session.DbSamples.Average(s => s.Db)):F1} dB");
        sb.AppendLine($"峰值分贝：{(session.DbSamples.Count == 0 ? 0 : session.DbSamples.Max(s => s.Db)):F1} dB");
        sb.AppendLine($"分贝采样数：{session.DbSamples.Count}");
        sb.AppendLine();
        sb.AppendLine("课堂语音转写：");
        if (session.AsrSegments.Count == 0)
        {
            sb.AppendLine("（无转写内容）");
        }
        else
        {
            foreach (AsrSegment seg in session.AsrSegments.Take(200))
            {
                sb.AppendLine($"[{seg.Ts}s] {seg.Text}");
            }
        }

        return sb.ToString();
    }

    private static string BuildMeta(Session session)
    {
        string end = session.EndTime?.ToString("HH:mm", CultureInfo.InvariantCulture) ?? "进行中";
        return $"班级：{session.Lesson.ClassName}　时间：{session.StartTime:yyyy-MM-dd HH:mm}—{end}　"
               + $"课型：{LessonTypeUtil.Name(session.Lesson.Type)}　拖堂：{(session.OvertimeDetected ? "是" : "否")}";
    }

    private static bool IsUnavailable(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        return text.Contains("AI 服务不可用", StringComparison.Ordinal)
               || text.StartsWith("错误", StringComparison.Ordinal)
               || text.StartsWith("error", StringComparison.OrdinalIgnoreCase);
    }

    private static int ParseStars(string text)
    {
        int stars = text.Count(c => c == '★');
        if (stars > 0)
        {
            return Math.Clamp(stars, 0, 5);
        }

        Match match = Regex.Match(text, @"(\d)\s*/\s*5|评分[：:]\s*(\d)");
        if (match.Success)
        {
            string value = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
            if (int.TryParse(value, out int n))
            {
                return Math.Clamp(n, 0, 5);
            }
        }

        return 0;
    }

    /// <summary>提取某个小标题下的条目（遇到下一个标题即停止）。</summary>
    private static List<string> ExtractSection(string markdown, string[] keywords)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return result;
        }

        bool inside = false;
        foreach (string raw in markdown.Split('\n'))
        {
            string line = raw.Trim();
            if (line.StartsWith('#'))
            {
                if (inside && result.Count > 0)
                {
                    break;
                }

                inside = keywords.Any(k => line.Contains(k, StringComparison.Ordinal));
                continue;
            }

            if (!inside)
            {
                continue;
            }

            string item = StripBullet(line);
            if (item.Length > 0)
            {
                result.Add(item);
            }
        }

        return result.Take(10).ToList();
    }

    private static List<string> ExtractBullets(string markdown)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return result;
        }

        foreach (string raw in markdown.Split('\n'))
        {
            string item = StripBullet(raw.Trim());
            if (item.Length > 0)
            {
                result.Add(item);
            }
        }

        return result.Take(10).ToList();
    }

    private static string StripBullet(string line)
    {
        if (line.Length == 0 || line.StartsWith('#'))
        {
            return "";
        }

        string value = Regex.Replace(line, @"^[-*+]\s+|^\d+[.、)]\s*", "").Trim();
        return value.Length > 120 ? value[..120] : value;
    }

    /// <summary>从 AI 输出中截出 Markmap 列表（h1 + 无序列表），去掉代码围栏。</summary>
    private static string ExtractMindmap(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return "";
        }

        string text = markdown.Trim();
        text = Regex.Replace(text, @"^```(?:markdown|md|markmap)?\s*|\s*```$", "", RegexOptions.Multiline).Trim();
        var sb = new StringBuilder();
        bool started = false;
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.TrimEnd();
            if (!started)
            {
                if (line.TrimStart().StartsWith("# "))
                {
                    started = true;
                }
                else if (line.TrimStart().StartsWith("- "))
                {
                    sb.AppendLine("# 思维导图");
                    started = true;
                }
                else
                {
                    continue;
                }
            }

            if (line.TrimStart().StartsWith("- ") || line.TrimStart().StartsWith("# "))
            {
                sb.AppendLine(line);
            }
        }

        return sb.ToString().Trim();
    }

    private static List<string> DefaultAdvice(Session session)
    {
        var list = new List<string>
        {
            "建议对核心概念与典型例题多留 3~5 分钟，配合板书分步推导。",
            "可缩短重复性练习与点名环节，把时间留给当堂反馈。",
            "防拖堂：下课前 2 分钟收束内容并布置作业，避免过度展开。"
        };
        if (session.OvertimeDetected)
        {
            list.Add("本节课观察到拖堂，建议提前设定下课提醒并检查课间观察窗口内的收尾节奏。");
        }

        return list;
    }

    private static List<string> DefaultKeyPoints(Session session)
    {
        var points = session.AsrSegments
            .Where(s => !string.IsNullOrWhiteSpace(s.Text))
            .Take(3)
            .Select(s => s.Text.Length > 60 ? s.Text[..60] + "…" : s.Text)
            .ToList();
        if (points.Count == 0)
        {
            points.Add("本课核心概念、公式与适用范围");
            points.Add("典型例题的解题步骤与规范表达");
            points.Add("知识在实际问题中的基本应用");
        }

        return points;
    }

    private static List<string> DefaultDifficulties() => new()
    {
        "概念之间的区别与联系，容易混淆的条件",
        "综合应用题的分析思路与步骤拆解"
    };

    private static string BuildTemplateMindmap(Session session)
    {
        string subject = string.IsNullOrWhiteSpace(session.Lesson.Subject) ? "本课" : session.Lesson.Subject;
        var sb = new StringBuilder();
        sb.AppendLine($"# {subject}思维导图");
        sb.AppendLine($"- 核心概念");
        sb.AppendLine($"  - 定义与适用条件");
        sb.AppendLine($"  - 与旧知识的联系");
        sb.AppendLine($"- 重点方法");
        sb.AppendLine($"  - 典型例题步骤");
        sb.AppendLine($"  - 常见变式");
        sb.AppendLine($"- 易错与难点");
        sb.AppendLine($"  - 易混淆点");
        sb.AppendLine($"  - 规范表达");
        return sb.ToString().Trim();
    }

    private static string BuildTemplateTeacherSummary(SummaryDoc doc, Session session)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"本节课整体节奏平稳，课堂质量评价 {doc.Stars}/5。");
        sb.AppendLine($"平均音量 {doc.AvgDb:F1} dB，峰值 {doc.PeakDb:F1} dB，课堂互动与讲授分配基本合理。");
        sb.AppendLine();
        sb.AppendLine("### 改进建议");
        foreach (string item in doc.Advice)
        {
            sb.AppendLine($"- {item}");
        }

        sb.AppendLine();
        sb.AppendLine("### 节奏分析");
        sb.AppendLine($"- 可细讲：{session.Lesson.Subject}的核心方法与易错环节。");
        sb.AppendLine("- 可缩短：机械重复与过渡性表述。");
        sb.AppendLine($"- 防拖堂：{(session.OvertimeDetected ? "本节课已判定拖堂，需加强下课提醒。" : "按计划在下课前完成收束。")}");
        return sb.ToString().Trim();
    }

    private static string BuildTemplateGuide(Session session)
    {
        string subject = string.IsNullOrWhiteSpace(session.Lesson.Subject) ? "本课" : session.Lesson.Subject;
        var sb = new StringBuilder();
        sb.AppendLine($"# 导学案 · {subject}");
        sb.AppendLine();
        sb.AppendLine("## 学习目标");
        sb.AppendLine("- 能说出本课核心概念及其适用条件。");
        sb.AppendLine("- 能独立完成典型例题并说明思路。");
        sb.AppendLine("## 课前预习");
        sb.AppendLine("- 回顾相关知识，列出 2 个疑问。");
        sb.AppendLine("## 课堂任务");
        sb.AppendLine("- 记录重点与易错点，完成当堂练习。");
        sb.AppendLine("## 课后巩固");
        sb.AppendLine("- 完成变式练习并整理错题。");
        return sb.ToString().Trim();
    }

    private static string BuildTemplateDbReport(SummaryDoc doc)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# 考试/自习课课堂报告");
        sb.AppendLine();
        sb.AppendLine($"平均分贝：{doc.AvgDb:F1} dB；峰值分贝：{doc.PeakDb:F1} dB；采样点：{doc.DbSamples.Count}。");
        sb.AppendLine();
        sb.AppendLine("| 分钟 | 分贝(dB) |");
        sb.AppendLine("| --- | --- |");
        for (int i = 0; i < doc.DbSamples.Count; i++)
        {
            sb.AppendLine($"| {i + 1} | {doc.DbSamples[i]:F1} |");
        }

        return sb.ToString().Trim();
    }
}
