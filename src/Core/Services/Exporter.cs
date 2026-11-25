using System.Globalization;
using System.Text;
using AiClassAssistant.Core.Configuration;
using AiClassAssistant.Core.Models;

namespace AiClassAssistant.Core.Services;

/// <summary>
/// 导出：data/exports/{date}/{课程}/ 下的 md 文件；按 retention_days 只清理 recordings；
/// ExportUnclassified 供 --export-only 离线导出使用。
/// </summary>
public sealed class Exporter
{
    private readonly AppConfig _config;

    public Exporter(AppConfig config)
    {
        _config = config;
    }

    public string ExportRoot => _config.Dir("exports");

    /// <summary>导出一节课的所有 md 产物，返回写入的文件列表。</summary>
    public IReadOnlyList<string> ExportSession(Session session, SummaryDoc? doc = null)
    {
        var written = new List<string>();
        try
        {
            string date = (session.StartTime == default ? DateTime.Now : session.StartTime).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            string course = PathSafety.Sanitize(
                $"{session.Lesson.Subject}-{session.Lesson.ClassName}".Trim('-'), "未命名课程");
            string dir = PathSafety.Combine(ExportRoot, date, course);
            Directory.CreateDirectory(dir);

            foreach ((string key, string markdown) in session.Outputs)
            {
                if (string.IsNullOrWhiteSpace(markdown))
                {
                    continue;
                }

                string file = PathSafety.Combine(dir, PathSafety.Sanitize(key, "课堂记录") + ".md");
                File.WriteAllText(file, markdown, Encoding.UTF8);
                written.Add(file);
            }

            if (written.Count == 0 && doc is not null)
            {
                string file = Path.Combine(dir, "课堂总结.md");
                File.WriteAllText(file, BuildDocumentMarkdown(doc), Encoding.UTF8);
                written.Add(file);
            }

            if (written.Count == 0)
            {
                // 没有任何产物时也留一个结构化记录，保证界面有文件可展示。
                string file = Path.Combine(dir, "课堂记录.md");
                File.WriteAllText(file, BuildSessionMarkdown(session), Encoding.UTF8);
                written.Add(file);
            }
        }
        catch
        {
            // 导出失败不影响主流程。
        }

        return written;
    }

    /// <summary>按 retention_days 清理 recordings（只动录像目录，绝不碰 exports/用户数据）。</summary>
    public void Cleanup()
    {
        try
        {
            int days = _config.Get("retention_days", 14);
            if (days <= 0)
            {
                return;
            }

            string root = _config.Dir("recordings");
            DateTime cutoff = DateTime.UtcNow.AddDays(-days);
            foreach (string file in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
            {
                try
                {
                    if (File.GetLastWriteTimeUtc(file) < cutoff)
                    {
                        File.Delete(file);
                    }
                }
                catch
                {
                    // 单文件删除失败继续。
                }
            }
        }
        catch
        {
            // 目录不存在或无权限：忽略。
        }
    }

    /// <summary>
    /// 离线导出（--export-only）：把导出根目录的散落 md 归入当日「未分类」，
    /// 并为 recordings 生成清单；只复制不删除用户数据。
    /// </summary>
    public IReadOnlyList<string> ExportUnclassified()
    {
        var written = new List<string>();
        try
        {
            string dateDir = Path.Combine(ExportRoot, DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            string target = PathSafety.Combine(dateDir, "未分类");
            Directory.CreateDirectory(target);

            foreach (string file in Directory.GetFiles(ExportRoot, "*.md", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    string dest = Path.Combine(target, Path.GetFileName(file));
                    File.Copy(file, dest, true);
                    written.Add(dest);
                }
                catch
                {
                    // 忽略。
                }
            }

            string manifest = Path.Combine(target, "录像清单.md");
            var sb = new StringBuilder();
            sb.AppendLine("# 录像清单（离线导出）");
            sb.AppendLine();
            sb.AppendLine($"生成时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine();
            string recordings = _config.Dir("recordings");
            try
            {
                string[] media = Directory.GetFiles(recordings, "*", SearchOption.AllDirectories)
                    .Where(f => new[] { ".avi", ".wav", ".mp4", ".mkv" }
                        .Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
                    .ToArray();
                if (media.Length == 0)
                {
                    sb.AppendLine("暂无录像文件。");
                }
                else
                {
                    foreach (string file in media)
                    {
                        sb.AppendLine($"- {Path.GetFileName(file)}  ({new FileInfo(file).Length / 1024.0 / 1024.0:F1} MB)");
                    }
                }
            }
            catch
            {
                sb.AppendLine("录像目录不可读。");
            }

            File.WriteAllText(manifest, sb.ToString(), Encoding.UTF8);
            written.Add(manifest);
        }
        catch
        {
            // 忽略。
        }

        return written;
    }

    /// <summary>把 SummaryDoc 拼成完整 Markdown（供 UI 预览与离线导出）。</summary>
    public static string BuildDocumentMarkdown(SummaryDoc doc)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# {doc.Title}");
        sb.AppendLine();
        sb.AppendLine(doc.Meta);
        sb.AppendLine();
        sb.AppendLine($"课堂质量评分：{new string('★', Math.Clamp(doc.Stars, 0, 5))}{new string('☆', 5 - Math.Clamp(doc.Stars, 0, 5))}");
        sb.AppendLine();
        if (doc.Advice.Count > 0)
        {
            sb.AppendLine("## 改进建议");
            foreach (string item in doc.Advice)
            {
                sb.AppendLine($"- {item}");
            }

            sb.AppendLine();
        }

        if (doc.KeyPoints.Count > 0)
        {
            sb.AppendLine("## 重点");
            foreach (string item in doc.KeyPoints)
            {
                sb.AppendLine($"- {item}");
            }

            sb.AppendLine();
        }

        if (doc.Difficulties.Count > 0)
        {
            sb.AppendLine("## 难点");
            foreach (string item in doc.Difficulties)
            {
                sb.AppendLine($"- {item}");
            }

            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(doc.MindmapMarkdown))
        {
            sb.AppendLine("## 思维导图");
            sb.AppendLine();
            sb.AppendLine(doc.MindmapMarkdown);
            sb.AppendLine();
        }

        sb.AppendLine("## 分贝统计");
        sb.AppendLine();
        sb.AppendLine($"平均 {doc.AvgDb:F1} dB，峰值 {doc.PeakDb:F1} dB，共 {doc.DbSamples.Count} 个采样点。");
        sb.AppendLine();
        if (!string.IsNullOrWhiteSpace(doc.RawMarkdown))
        {
            sb.AppendLine("## 原始生成内容");
            sb.AppendLine();
            sb.AppendLine(doc.RawMarkdown);
        }

        return sb.ToString();
    }

    private static string BuildSessionMarkdown(Session session)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# 课堂记录 · {session.Lesson.Subject}");
        sb.AppendLine();
        sb.AppendLine($"- 班级：{session.Lesson.ClassName}");
        sb.AppendLine($"- 时间：{session.StartTime:yyyy-MM-dd HH:mm:ss} — {session.EndTime:HH:mm:ss}");
        sb.AppendLine($"- 课型：{LessonTypeUtil.Name(session.Lesson.Type)}");
        sb.AppendLine($"- 拖堂：{(session.OvertimeDetected ? "是" : "否")}");
        sb.AppendLine($"- 分贝采样：{session.DbSamples.Count} 条");
        sb.AppendLine($"- 转写片段：{session.AsrSegments.Count} 条");
        return sb.ToString();
    }
}
