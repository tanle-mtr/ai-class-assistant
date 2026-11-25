namespace AiClassAssistant.Core.Models;

/// <summary>一节课程会话（契约 §2，字段名不可改）。</summary>
public sealed class Session
{
    public string Id { get; set; } = "";
    public LessonInfo Lesson { get; set; } = new();
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public List<DbSample> DbSamples { get; } = new();
    public List<AsrSegment> AsrSegments { get; } = new();
    public List<string> CameraSegments { get; } = new();
    public string? AudioFile { get; set; }
    public bool OvertimeDetected { get; set; }
    public Dictionary<string, string> Outputs { get; } = new();
    public bool IsActive => EndTime == null;
}
