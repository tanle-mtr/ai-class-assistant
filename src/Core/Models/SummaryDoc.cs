namespace AiClassAssistant.Core.Models;

/// <summary>课堂总结解析结果（契约 §2，字段名不可改）。</summary>
public sealed class SummaryDoc
{
    public string Title { get; set; } = "";
    public string Meta { get; set; } = "";
    public int Stars { get; set; }
    public List<string> Advice { get; set; } = new();     // 细讲/缩短/防拖堂
    public List<string> KeyPoints { get; set; } = new();
    public List<string> Difficulties { get; set; } = new();
    public string MindmapMarkdown { get; set; } = "";
    public List<double> DbSamples { get; set; } = new();
    public double AvgDb { get; set; }
    public double PeakDb { get; set; }
    public string RawMarkdown { get; set; } = "";
}
