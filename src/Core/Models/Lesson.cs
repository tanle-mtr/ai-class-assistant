namespace AiClassAssistant.Core.Models;

/// <summary>课堂状态（来自 ClassIsland 或配置兜底）。</summary>
public enum LessonState
{
    None,
    OnClass,
    Breaking,
    AfterSchool
}

/// <summary>课型。</summary>
public enum LessonType
{
    New,
    Review,
    Exam
}

/// <summary>一节课的基本信息（契约 §2，字段名不可改）。</summary>
public sealed class LessonInfo
{
    public string Subject { get; set; } = "";
    public string ClassName { get; set; } = "";
    public string Start { get; set; } = "";   // "09:35"
    public string End { get; set; } = "";     // "10:15"
    public LessonType Type { get; set; } = LessonType.New;
    public int Index { get; set; }
}
