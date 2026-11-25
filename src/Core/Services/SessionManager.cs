using AiClassAssistant.Core.Configuration;
using AiClassAssistant.Core.Contracts;
using AiClassAssistant.Core.Events;
using AiClassAssistant.Core.Models;

namespace AiClassAssistant.Core.Services;

/// <summary>
/// 课堂状态机：上课 → 会话开始；课间 Breaking → 观察窗口（每个 tick 持续采样屏幕与就座率）；
/// 窗口结束后判定拖堂并结束会话、发布事件。换座检测通过回调/委托注入，不依赖感知层实现。
/// </summary>
public sealed class SessionManager : IDisposable
{
    private const int SeatSampleSeconds = 30;

    private readonly IEventBus _bus;
    private readonly AppConfig _config;
    private readonly SeatmapService _seatmap;
    private readonly object _gate = new();

    private readonly Action<ClassIslandStatePayload> _stateHandler;
    private readonly Action<DbSampledPayload> _dbHandler;
    private readonly Action<AsrSegmentedPayload> _asrHandler;
    private readonly Action<DbSample> _audioDbHandler;
    private readonly Action<AsrSegment> _audioAsrHandler;

    private System.Threading.Timer? _tickTimer;
    private IScreenAnalyzer? _screen;
    private ICameraMonitor? _camera;
    private IAudioMonitor? _audio;
    private PrivacyService? _privacy;
    private Func<IEnumerable<(string faceId, string seat)>>? _observationProvider;
    private Action<IEnumerable<(string faceId, string seat)>>? _observationCallback;
    private bool _audioEventsAttached;
    private bool _started;

    private LessonState _state = LessonState.None;
    private Session? _session;
    private DateTime _breakStartUtc;
    private bool _observing;
    private int _tickCount;
    private int _overtimeHits;
    private DateTime _lastSeatSampleUtc;

    public SessionManager(IEventBus bus, AppConfig config, SeatmapService seatmap)
    {
        _bus = bus;
        _config = config;
        _seatmap = seatmap;

        _stateHandler = p => HandleState(p.State, p.Lesson);
        _dbHandler = p => HandleDbSample(p.Sample);
        _asrHandler = p => HandleAsrSegment(p.Segment);
        _audioDbHandler = HandleDbSample;
        _audioAsrHandler = HandleAsrSegment;
    }

    /// <summary>当前会话（已结束为 null）。</summary>
    public Session? CurrentSession
    {
        get { lock (_gate) { return _session; } }
    }

    /// <summary>当前课表状态。</summary>
    public LessonState State
    {
        get { lock (_gate) { return _state; } }
    }

    public bool IsObserving
    {
        get { lock (_gate) { return _observing; } }
    }

    public event Action<Session>? SessionStarted;
    public event Action<Session>? SessionEnded;
    public event Action<Session>? SessionOvertime;

    /// <summary>启动订阅与 1 秒 tick（供观察窗口持续采样）。</summary>
    public void Start()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        _bus.On(_stateHandler);
        _bus.On(_dbHandler);
        _bus.On(_asrHandler);
        try
        {
            _tickTimer = new System.Threading.Timer(_ => Tick(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        }
        catch
        {
            _tickTimer = null;
        }
    }

    /// <summary>停止订阅与 tick；若有活动会话则正常收尾。</summary>
    public void Stop()
    {
        if (!_started)
        {
            return;
        }

        _started = false;
        if (_bus is EventBus concrete)
        {
            concrete.Off(_stateHandler);
            concrete.Off(_dbHandler);
            concrete.Off(_asrHandler);
        }

        DetachAudioEvents();
        _tickTimer?.Dispose();
        _tickTimer = null;

        Session? pending = null;
        lock (_gate)
        {
            if (_session is not null)
            {
                pending = _session;
                _session = null;
                _observing = false;
            }
        }

        if (pending is not null)
        {
            EndSessionSideEffects(pending, pending.OvertimeDetected);
        }
    }

    /// <summary>注入感知实现（全部是 Core 契约接口，不引用感知工程）。</summary>
    public void AttachPerception(IScreenAnalyzer? screen, ICameraMonitor? camera, IAudioMonitor? audio)
    {
        _screen = screen;
        _camera = camera;
        _audio = audio;
        AttachAudioEvents();
    }

    /// <summary>注入隐私策略；不注入时默认允许写媒体。</summary>
    public void SetPrivacyService(PrivacyService? privacy)
    {
        _privacy = privacy;
    }

    /// <summary>注入换座观察提供者（拉模式，通常由 App 用 camera.CollectObservations 包装）。</summary>
    public void SetObservationProvider(Func<IEnumerable<(string faceId, string seat)>>? provider)
    {
        _observationProvider = provider;
    }

    /// <summary>
    /// 注入换座观察回调（推模式）：SessionManager 采集到观察后回调，供 UI 等层解耦消费。
    /// </summary>
    public void SetObservationCallback(Action<IEnumerable<(string faceId, string seat)>>? callback)
    {
        _observationCallback = callback;
    }

    /// <summary>外部直接推送观察（上课中/下课时调用，等价于相机采集结果）。</summary>
    public void PushObservations(IEnumerable<(string faceId, string seat)>? observations)
    {
        if (observations is null)
        {
            return;
        }

        ApplyObservations(observations.ToList());
    }

    /// <summary>处理课表状态变化（也可由 App 直接调用，不依赖事件总线）。</summary>
    public void HandleState(LessonState state, LessonInfo? lesson)
    {
        Session? toStart = null;
        Session? toEnd = null;
        bool overtime = false;

        lock (_gate)
        {
            _state = state;
            switch (state)
            {
                case LessonState.OnClass:
                    if (_session is { IsActive: true } && _observing)
                    {
                        // 下一节课已经开始：上一节先收尾（观察窗口按当前采样判定）。
                        toEnd = _session;
                        overtime = _session.OvertimeDetected;
                        _session = null;
                        _observing = false;
                    }

                    if (_session is null || !_session.IsActive)
                    {
                        toStart = CreateSession(lesson);
                        _session = toStart;
                        _observing = false;
                        _tickCount = 0;
                        _overtimeHits = 0;
                        _lastSeatSampleUtc = DateTime.UtcNow;
                    }
                    else if (lesson is not null)
                    {
                        UpdateLesson(_session.Lesson, lesson);
                    }

                    break;

                case LessonState.Breaking:
                    // 关键：Breaking 只开启观察窗口，不在这里结束会话；每个 tick 持续采样。
                    if (_session is { IsActive: true } && !_observing)
                    {
                        _observing = true;
                        _breakStartUtc = DateTime.UtcNow;
                        _tickCount = 0;
                        _overtimeHits = 0;
                    }

                    break;

                case LessonState.AfterSchool:
                case LessonState.None:
                    // 窗口未结束时继续由 tick 采样，窗口结束才判定拖堂并收尾。
                    if (_session is { IsActive: true } && !_observing)
                    {
                        toEnd = _session;
                        overtime = _session.OvertimeDetected;
                        _session = null;
                    }

                    break;
            }
        }

        if (toEnd is not null)
        {
            EndSessionSideEffects(toEnd, overtime);
        }

        if (toStart is not null)
        {
            StartSessionSideEffects(toStart);
        }
    }

    /// <summary>追加分贝采样（供感知层直接调用或经事件总线发布）。</summary>
    public void HandleDbSample(DbSample? sample)
    {
        if (sample is null)
        {
            return;
        }

        lock (_gate)
        {
            _session?.DbSamples.Add(sample);
        }
    }

    /// <summary>追加语音转写片段。</summary>
    public void HandleAsrSegment(AsrSegment? segment)
    {
        if (segment is null)
        {
            return;
        }

        lock (_gate)
        {
            _session?.AsrSegments.Add(segment);
        }
    }

    /// <summary>追加摄像头切片路径。</summary>
    public void HandleCameraSegment(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        lock (_gate)
        {
            _session?.CameraSegments.Add(path!);
        }
    }

    /// <summary>记录录音文件（由感知层回调）。</summary>
    public void SetAudioFile(string? path)
    {
        lock (_gate)
        {
            if (_session is not null)
            {
                _session.AudioFile = path;
            }
        }
    }

    /// <summary>
    /// 每秒 tick：Breaking 时强制屏幕采样 + 读就座率，观察窗口结束后判定拖堂并收尾；
    /// 上课中每 30 秒采集一次换座观察。
    /// </summary>
    public void Tick()
    {
        Session? finalize = null;
        bool overtime = false;
        bool sampleSeats = false;

        lock (_gate)
        {
            Session? active = _session;
            if (active is not { IsActive: true })
            {
                return;
            }

            if (_state == LessonState.OnClass
                && (DateTime.UtcNow - _lastSeatSampleUtc).TotalSeconds >= SeatSampleSeconds)
            {
                sampleSeats = true;
                _lastSeatSampleUtc = DateTime.UtcNow;
            }

            if (_observing)
            {
                double screenRatio = 0.0;
                double seatedRatio = 0.0;
                try
                {
                    _screen?.ForceSample();
                    screenRatio = _screen?.ActivityRatio ?? 0.0;
                }
                catch
                {
                    // 屏幕采集失败按 0 处理。
                }

                try
                {
                    seatedRatio = _camera?.SeatedRatio ?? 0.0;
                }
                catch
                {
                    // 摄像头采集失败按 0 处理。
                }

                double screenThreshold = _config.Get("overtime_screen_threshold", 0.6);
                double seatedThreshold = _config.Get("overtime_seated_ratio", 0.7);
                _tickCount++;
                if (screenRatio >= screenThreshold && seatedRatio >= seatedThreshold)
                {
                    _overtimeHits++;
                }

                int windowSeconds = Math.Max(1, _config.Get("overtime_check_seconds", 120));
                if ((DateTime.UtcNow - _breakStartUtc).TotalSeconds >= windowSeconds)
                {
                    // 观察窗口结束后才写 OvertimeDetected，不能提前判定。
                    active.OvertimeDetected = _tickCount > 0 && (double)_overtimeHits / _tickCount >= 0.5;
                    overtime = active.OvertimeDetected;
                    finalize = active;
                    _session = null;
                    _observing = false;
                    sampleSeats = true;
                }
            }
        }

        if (sampleSeats)
        {
            CollectAndApplyObservations();
        }

        if (finalize is not null)
        {
            EndSessionSideEffects(finalize, overtime);
        }
    }

    public void Dispose()
    {
        Stop();
        DetachAudioEvents();
    }

    private static Session CreateSession(LessonInfo? lesson)
    {
        var info = new LessonInfo();
        if (lesson is not null)
        {
            UpdateLesson(info, lesson);
        }

        LessonType type = info.Type != LessonType.New ? info.Type : LessonTypeUtil.Of(info.Subject);
        info.Type = type;

        return new Session
        {
            Id = DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..6],
            Lesson = info,
            StartTime = DateTime.Now
        };
    }

    private static void UpdateLesson(LessonInfo target, LessonInfo source)
    {
        if (!string.IsNullOrWhiteSpace(source.Subject))
        {
            target.Subject = source.Subject;
        }

        if (!string.IsNullOrWhiteSpace(source.ClassName))
        {
            target.ClassName = source.ClassName;
        }

        if (!string.IsNullOrWhiteSpace(source.Start))
        {
            target.Start = source.Start;
        }

        if (!string.IsNullOrWhiteSpace(source.End))
        {
            target.End = source.End;
        }

        target.Index = source.Index;
        if (source.Type != LessonType.New)
        {
            target.Type = source.Type;
        }
    }

    private void StartSessionSideEffects(Session session)
    {
        // 开始上课前先关闭旧 WAV/AVI 句柄（契约 §4.4），避免旧文件头损坏。
        if (_privacy is null || _privacy.ShouldWriteMedia(session))
        {
            TryCloseWriters();
            try
            {
                _audio?.StartRecording(session);
            }
            catch
            {
                // 感知层异常不影响状态机。
            }

            try
            {
                _camera?.StartRecording(session);
            }
            catch
            {
                // 同上。
            }
        }
        else
        {
            TryCloseWriters();
        }

        _bus.Emit(new SessionStartedPayload { Session = session });
        SessionStarted?.Invoke(session);
    }

    private void EndSessionSideEffects(Session session, bool overtime)
    {
        session.EndTime = DateTime.Now;
        try
        {
            _audio?.StopRecording(session);
        }
        catch
        {
            // 忽略。
        }

        try
        {
            _camera?.StopRecording(session);
        }
        catch
        {
            // 忽略。
        }

        // 无会话时不写任何媒体：立即关闭自动写入句柄，但不误伤正在进行的录像。
        if (_privacy is null || !_privacy.ShouldWriteMedia(null))
        {
            TryCloseWriters();
        }

        CollectAndApplyObservations();

        _bus.Emit(new SessionEndedPayload { Session = session });
        SessionEnded?.Invoke(session);
        if (overtime)
        {
            _bus.Emit(new SessionOvertimePayload { Session = session, Overtime = true });
            SessionOvertime?.Invoke(session);
        }
    }

    private void TryCloseWriters()
    {
        try
        {
            _audio?.CloseAutoWriters();
        }
        catch
        {
            // 忽略。
        }

        try
        {
            _camera?.CloseAutoWriters();
        }
        catch
        {
            // 忽略。
        }
    }

    private void AttachAudioEvents()
    {
        if (_audio is null || _audioEventsAttached)
        {
            return;
        }

        try
        {
            _audio.DbSampled += _audioDbHandler;
            _audio.AsrSegmented += _audioAsrHandler;
            _audioEventsAttached = true;
        }
        catch
        {
            // 忽略。
        }
    }

    private void DetachAudioEvents()
    {
        if (_audio is null || !_audioEventsAttached)
        {
            return;
        }

        try
        {
            _audio.DbSampled -= _audioDbHandler;
            _audio.AsrSegmented -= _audioAsrHandler;
        }
        catch
        {
            // 忽略。
        }

        _audioEventsAttached = false;
    }

    private void CollectAndApplyObservations()
    {
        IEnumerable<(string faceId, string seat)>? observations = null;
        if (_observationProvider is not null)
        {
            try
            {
                observations = _observationProvider();
            }
            catch
            {
                // 忽略。
            }
        }

        if (observations is null && _camera is not null)
        {
            try
            {
                observations = _camera.CollectObservations();
            }
            catch
            {
                // 忽略。
            }
        }

        if (observations is null)
        {
            return;
        }

        ApplyObservations(observations.ToList());
    }

    private void ApplyObservations(List<(string faceId, string seat)> observations)
    {
        if (observations.Count == 0)
        {
            return;
        }

        try
        {
            if (_seatmap.UpdateFromObservations(observations))
            {
                _seatmap.ExportMarkdown();
            }
        }
        catch
        {
            // 座位表更新失败不影响主流程。
        }

        try
        {
            _observationCallback?.Invoke(observations);
        }
        catch
        {
            // 回调异常不影响主流程。
        }
    }
}
