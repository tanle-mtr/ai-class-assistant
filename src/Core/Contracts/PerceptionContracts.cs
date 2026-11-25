using AiClassAssistant.Core.Models;

namespace AiClassAssistant.Core.Contracts;

/// <summary>
/// 跨层契约：感知层实现这些接口，Core 只依赖接口，不引用 Perception 工程。
/// 签名严格对齐 v3 架构契约 §3。
/// </summary>
public interface IAudioMonitor : IDisposable
{
    void Start();
    void Stop();
    double CurrentDb { get; }
    List<double> RecentSamples(int count);
    void StartRecording(Session s);
    void StopRecording(Session s);
    void CloseAutoWriters();
    event Action<DbSample>? DbSampled;
    event Action<AsrSegment>? AsrSegmented;
}

/// <summary>摄像头监控（共享采集，不独占）。</summary>
public interface ICameraMonitor : IDisposable
{
    void Start();
    void Stop();
    void StartRecording(Session s);
    void StopRecording(Session s);
    void CloseAutoWriters();
    int FaceCount { get; }
    double SeatedRatio { get; }
    List<(string faceId, string seat)> CollectObservations();
}

/// <summary>屏幕分析（只实时分析，不存档）。</summary>
public interface IScreenAnalyzer : IDisposable
{
    void Start();
    void Stop();
    double ActivityRatio { get; }
    void ForceSample();
}

/// <summary>ClassIsland 课表来源（IPC 或配置兜底）。</summary>
public interface IClassIslandSource
{
    event Action<LessonState, LessonInfo?>? StateChanged;
    LessonState State { get; }
    List<LessonInfo> TodaySchedule();
    void Start();
    void Stop();
}
