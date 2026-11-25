namespace AiClassAssistant.Core.Models;

/// <summary>一次分贝采样：Sec 为会话开始后的秒数。</summary>
public sealed class DbSample
{
    public long Sec { get; set; }
    public double Db { get; set; }
}

/// <summary>一段语音转写：Ts 为会话开始后的秒数。</summary>
public sealed class AsrSegment
{
    public long Ts { get; set; }
    public string Text { get; set; } = "";
}
