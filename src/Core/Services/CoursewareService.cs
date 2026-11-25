using AiClassAssistant.Core.Configuration;

namespace AiClassAssistant.Core.Services;

/// <summary>课件按科目分类存 data/courseware/{subject}/，不存在的科目自动创建。</summary>
public sealed class CoursewareService
{
    private readonly AppConfig _config;
    private readonly object _gate = new();

    public CoursewareService(AppConfig config)
    {
        _config = config;
        // 兼容旧配置里的 courseware_subjects，启动时确保目录存在。
        foreach (string subject in _config.Get<List<string>>("courseware_subjects", new List<string>()))
        {
            EnsureSubject(subject);
        }
    }

    public string Root => _config.Dir("courseware");

    /// <summary>列出全部科目（以目录为准）。</summary>
    public List<string> ListSubjects()
    {
        try
        {
            return Directory.GetDirectories(Root, "*", SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileName)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s!)
                .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch
        {
            return new List<string>();
        }
    }

    /// <summary>确保科目目录存在并返回完整路径。</summary>
    public string EnsureSubject(string subject)
    {
        string safe = PathSafety.Sanitize(subject, "未分类");
        string dir = PathSafety.Combine(Root, safe);
        try
        {
            Directory.CreateDirectory(dir);
        }
        catch
        {
            // 忽略。
        }

        lock (_gate)
        {
            List<string> known = _config.Get<List<string>>("courseware_subjects", new List<string>());
            if (!known.Any(s => string.Equals(s, safe, StringComparison.OrdinalIgnoreCase)))
            {
                known.Add(safe);
                _config.Set("courseware_subjects", known);
            }
        }

        return dir;
    }

    /// <summary>保存上传文件，返回完整路径；文件名做路径穿越防护。</summary>
    public async Task<string> SaveAsync(string subject, string fileName, Stream content, CancellationToken ct = default)
    {
        string dir = EnsureSubject(subject);
        string safeName = PathSafety.Sanitize(fileName, "课件.pdf");
        string path = Path.Combine(dir, safeName);
        try
        {
            await using FileStream fs = new(path, FileMode.Create, FileAccess.Write, FileShare.None);
            await content.CopyToAsync(fs, ct).ConfigureAwait(false);
        }
        catch
        {
            // 写入失败时返回路径，由调用方检查文件是否存在。
        }

        return path;
    }

    /// <summary>列出某科目下已上传的文件完整路径。</summary>
    public List<string> ListFiles(string subject)
    {
        string dir = EnsureSubject(subject);
        try
        {
            return Directory.GetFiles(dir, "*", SearchOption.TopDirectoryOnly)
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch
        {
            return new List<string>();
        }
    }

    /// <summary>取文件路径（不存在时不创建）。</summary>
    public string GetFilePath(string subject, string fileName)
    {
        string dir = PathSafety.Combine(Root, PathSafety.Sanitize(subject, "未分类"));
        return Path.Combine(dir, PathSafety.Sanitize(fileName, "课件.pdf"));
    }
}
