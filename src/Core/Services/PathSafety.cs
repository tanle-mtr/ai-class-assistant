namespace AiClassAssistant.Core.Services;

/// <summary>路径安全工具：所有用户可控名称写入文件系统前必须过这一层。</summary>
internal static class PathSafety
{
    private static readonly char[] InvalidChars = Path.GetInvalidFileNameChars();

    /// <summary>清理成安全文件名/目录名：去掉路径分隔符与 ..，去掉非法字符。</summary>
    public static string Sanitize(string? name, string fallback = "未命名")
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return fallback;
        }

        string value = name.Replace('\\', '/').Split('/').LastOrDefault() ?? "";
        value = value.Replace("..", "").Trim();
        var sb = new System.Text.StringBuilder(value.Length);
        foreach (char c in value)
        {
            sb.Append(InvalidChars.Contains(c) || c == '/' || c == '\\' || c == ':' ? '_' : c);
        }

        string result = sb.ToString().Trim().Trim('.');
        return result.Length == 0 ? fallback : result;
    }

    /// <summary>确保 child 位于 root 之内；路径穿越时返回 root 本身。</summary>
    public static string EnsureUnderRoot(string root, string child)
    {
        try
        {
            string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string fullChild = Path.GetFullPath(child);
            return fullChild.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase) ? fullChild : fullRoot;
        }
        catch
        {
            return root;
        }
    }

    /// <summary>在 root 下安全拼接子路径。</summary>
    public static string Combine(string root, params string[] parts)
    {
        string child = parts.Aggregate(root, (current, part) => Path.Combine(current, Sanitize(part)));
        return EnsureUnderRoot(root, child);
    }
}
