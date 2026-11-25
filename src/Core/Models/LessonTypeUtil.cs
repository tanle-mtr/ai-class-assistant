namespace AiClassAssistant.Core.Models;

/// <summary>课型识别辅助：供 ClassIsland 解析层与总结流水线共用（旧 models.py 的逻辑）。</summary>
public static class LessonTypeUtil
{
    private static readonly string[] ExamKeywords =
        { "考试", "自习", "测验", "月考", "期中", "期末", "模拟考", "周测" };

    private static readonly string[] ReviewKeywords =
        { "评讲", "讲评", "作业讲评", "试卷", "习题课", "订正", "试卷分析" };

    /// <summary>按科目文本判定课型。</summary>
    public static LessonType Of(string? subject)
    {
        string text = subject ?? "";
        if (ExamKeywords.Any(k => text.Contains(k, StringComparison.Ordinal)))
        {
            return LessonType.Exam;
        }

        if (ReviewKeywords.Any(k => text.Contains(k, StringComparison.Ordinal)))
        {
            return LessonType.Review;
        }

        return LessonType.New;
    }

    /// <summary>按课程信息判定课型；Lesson.Type 已显式设置时优先采用。</summary>
    public static LessonType Of(LessonInfo? lesson)
    {
        if (lesson is null)
        {
            return LessonType.New;
        }

        // 默认值 New 无法区分「显式指定」与「未设置」，因此仍从科目文本推断。
        return Of(lesson.Subject);
    }

    /// <summary>课型中文名。</summary>
    public static string Name(LessonType type) => type switch
    {
        LessonType.New => "新授课",
        LessonType.Review => "评讲课",
        LessonType.Exam => "考试/自习课",
        _ => "新授课"
    };
}
