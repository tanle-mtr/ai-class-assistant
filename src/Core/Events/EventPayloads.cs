using AiClassAssistant.Core.Models;

namespace AiClassAssistant.Core.Events;

// 强类型事件负载：与 EventNames 中的字符串一一对应，供跨层订阅使用。

/// <summary>对应 classisland.state。</summary>
public sealed class ClassIslandStatePayload
{
    public LessonState State { get; set; }
    public LessonInfo? Lesson { get; set; }
}

/// <summary>对应 perception.db。</summary>
public sealed class DbSampledPayload
{
    public DbSample Sample { get; set; } = new();
}

/// <summary>对应 perception.asr_segment。</summary>
public sealed class AsrSegmentedPayload
{
    public AsrSegment Segment { get; set; } = new();
}

/// <summary>对应 session.start。</summary>
public sealed class SessionStartedPayload
{
    public Session Session { get; set; } = new();
}

/// <summary>对应 session.end。</summary>
public sealed class SessionEndedPayload
{
    public Session Session { get; set; } = new();
}

/// <summary>对应 session.overtime。</summary>
public sealed class SessionOvertimePayload
{
    public Session Session { get; set; } = new();
    public bool Overtime { get; set; }
}
