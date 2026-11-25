namespace AiClassAssistant.Core.Models;

/// <summary>座位（契约 §2，字段名不可改）。</summary>
public sealed class Seat
{
    public string Id { get; set; } = "";
    public int Row { get; set; }
    public int Col { get; set; }
    public string Name { get; set; } = "";
    public string FaceId { get; set; } = "";
}
