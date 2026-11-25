using System.Text;
using AiClassAssistant.Core.Configuration;
using AiClassAssistant.Core.Contracts;
using AiClassAssistant.Core.Models;
using NAudio.Wave;
using Timer = System.Threading.Timer;

namespace AiClassAssistant.Perception.Audio;

/// <summary>
/// 麦克风监控（NAudio WaveInEvent，系统共享模式，不独占设备）。
///
/// 职责：
/// - 16 kHz / 16 bit / 单声道采集，实时 RMS 分贝（0 dBFS 满幅 + 90 偏移，日常语音 30~80）
/// - 环形缓冲供 RecentSamples；每秒 PCM 块通过 <see cref="AudioChunkReady"/> 喂给 ASR 层
/// - 上课按课次写 WAV；无人上课且 monitor_always_record=false 时绝不落盘
/// - 设备被占用 / 无设备时只记录状态并每 3 秒重试，绝不抛异常到调用方
/// </summary>
public sealed class AudioMonitor : IAudioMonitor
{
    private const int SampleRate = 16000;
    private const int BitsPerSample = 16;
    private const int Channels = 1;
    private const int AsrBlockSeconds = 30;
    private const int AsrWindowBytes = SampleRate * (BitsPerSample / 8) * AsrBlockSeconds;
    private const int MonitorSegmentMinutes = 30;

    private readonly object _gate = new();
    private readonly object _deviceGate = new();
    private readonly object _ringGate = new();
    private readonly object _asrGate = new();
    private readonly Queue<double> _ring = new();
    private readonly List<byte> _asrBuffer = new();

    private WaveInEvent? _waveIn;
    private WaveFileWriter? _writer;
    private Session? _session;
    private Timer? _retryTimer;
    private string _deviceStatus = "未启动";
    private long _lastDbBits;
    private int _ringCapacity = 60;
    private DateTime _monitorSegmentStart;

    private bool _running;
    private bool _disposed;

    /// <summary>设备状态文本（诊断用，接口外附加）。</summary>
    public string DeviceStatus => Volatile.Read(ref _deviceStatus);

    public double CurrentDb => BitConverter.Int64BitsToDouble(Interlocked.Read(ref _lastDbBits));

    public event Action<DbSample>? DbSampled;
    public event Action<AsrSegment>? AsrSegmented;

    /// <summary>
    /// 每秒一次的 16 bit PCM 块，供 App/ASR 层自行转写。
    /// 契约接口只暴露带文本的 AsrSegment，故额外提供此事件承载音频本身。
    /// </summary>
    public event Action<byte[], long>? AudioChunkReady;

    /// <summary>
    /// 可选 ASR 转写器注入：设置后，每累计 30 秒音频自动转写并通过 AsrSegmented 输出。
    /// 未设置时不转写（App 层可直接消费 AudioChunkReady）。
    /// </summary>
    public Func<byte[], string?>? AsrTranscriber { get; set; }

    public WaveFormat Format { get; } = new(SampleRate, BitsPerSample, Channels);

    public void Start()
    {
        if (_running || _disposed)
        {
            return;
        }

        _running = true;
        _ringCapacity = Math.Max(8, AppConfig.Instance.Get("db_windows", 60));
        Volatile.Write(ref _deviceStatus, "连接中");
        // 设备被占用 / 未插麦克风时每 3 秒重试一次；插上后自动恢复。
        _retryTimer = new Timer(_ => TryOpenDevice(), null, 0, 3000);
    }

    public void Stop()
    {
        if (!_running)
        {
            return;
        }

        _running = false;
        Timer? timer = Interlocked.Exchange(ref _retryTimer, null);
        timer?.Dispose();
        CloseDevice();

        lock (_gate)
        {
            CloseWriterNoLock();
            _session = null;
            _monitorSegmentStart = DateTime.MinValue;
        }

        lock (_asrGate)
        {
            _asrBuffer.Clear();
        }

        Volatile.Write(ref _deviceStatus, "已停止");
    }

    public List<double> RecentSamples(int count)
    {
        if (count <= 0)
        {
            return new List<double>();
        }

        lock (_ringGate)
        {
            int skip = Math.Max(0, _ring.Count - count);
            return _ring.Skip(skip).ToList();
        }
    }

    /// <summary>
    /// 开始按课次录音。契约第 4 节第 4 条：先关闭旧 WAV 句柄，避免文件头损坏。
    /// </summary>
    public void StartRecording(Session s)
    {
        if (s is null)
        {
            return;
        }

        lock (_gate)
        {
            CloseWriterNoLock();
            _session = s;
            _monitorSegmentStart = DateTime.MinValue;

            if (!AppConfig.Instance.Get("record_audio", true))
            {
                Volatile.Write(ref _deviceStatus, "上课录音已在设置中关闭");
                return;
            }

            try
            {
                string dateDir = Path.Combine(AppConfig.Instance.Dir("recordings"), DateTime.Now.ToString("yyyy-MM-dd"));
                Directory.CreateDirectory(dateDir);
                string path = Path.Combine(dateDir, $"{s.StartTime:HHmmss}-{Sanitize(s.Lesson.Subject)}-音频.wav");
                _writer = new WaveFileWriter(path, Format);
                s.AudioFile = path;
                Volatile.Write(ref _deviceStatus, "上课录音中");
            }
            catch (Exception ex)
            {
                _writer = null;
                Volatile.Write(ref _deviceStatus, $"录音不可用：{ex.Message}");
            }
        }

        lock (_asrGate)
        {
            _asrBuffer.Clear();
        }
    }

    public void StopRecording(Session s)
    {
        lock (_gate)
        {
            _session = null;
            CloseWriterNoLock();
            _monitorSegmentStart = DateTime.MinValue;
        }

        Volatile.Write(ref _deviceStatus, _running ? "监控中（不落盘）" : "已停止");
    }

    /// <summary>
    /// 关闭「无人上课」期间的自动切片；有课次时不误伤上课录音。
    /// </summary>
    public void CloseAutoWriters()
    {
        lock (_gate)
        {
            if (_session is not null)
            {
                return;
            }

            CloseWriterNoLock();
            _monitorSegmentStart = DateTime.MinValue;
        }
    }

    private void TryOpenDevice()
    {
        if (!_running || _disposed)
        {
            return;
        }

        lock (_deviceGate)
        {
            if (_waveIn is not null)
            {
                return;
            }

            try
            {
                var waveIn = new WaveInEvent
                {
                    DeviceNumber = 0, // 系统默认设备；WaveInEvent 天然共享，不设独占
                    WaveFormat = Format
                };
                waveIn.DataAvailable += OnDataAvailable;
                waveIn.StartRecording();
                _waveIn = waveIn;
                Volatile.Write(ref _deviceStatus, "采集中");
            }
            catch (Exception ex)
            {
                _waveIn = null;
                Volatile.Write(ref _deviceStatus, $"麦克风不可用（每 3 秒重试）：{ex.Message}");
            }
        }
    }

    private void CloseDevice()
    {
        WaveInEvent? waveIn;
        lock (_deviceGate)
        {
            waveIn = _waveIn;
            _waveIn = null;
        }

        if (waveIn is null)
        {
            return;
        }

        try
        {
            waveIn.DataAvailable -= OnDataAvailable;
        }
        catch
        {
            // 解绑失败不影响后续释放
        }

        try
        {
            waveIn.StopRecording();
        }
        catch
        {
            // 设备已拔出时 StopRecording 可能抛异常，忽略
        }

        try
        {
            waveIn.Dispose();
        }
        catch
        {
            // 同上
        }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (!_running || e.BytesRecorded <= 0)
        {
            return;
        }

        try
        {
            double db = ComputeDb(e.Buffer, e.BytesRecorded);
            Interlocked.Exchange(ref _lastDbBits, BitConverter.DoubleToInt64Bits(db));
            PushRing(db);
            DbSampled?.Invoke(new DbSample { Sec = SessionSeconds(), Db = db });

            lock (_gate)
            {
                _writer?.Write(e.Buffer, 0, e.BytesRecorded);
            }

            EnsureAutoRecording();
            AppendAsr(e.Buffer, e.BytesRecorded);
        }
        catch
        {
            // 单块采集异常绝不能冒泡到 NAudio 的采集线程，否则设备会停止
        }
    }

    /// <summary>16 bit PCM → RMS 分贝：归一化后 90 + 20*log10(rms)。</summary>
    private static double ComputeDb(byte[] buffer, int bytes)
    {
        int samples = bytes / 2;
        if (samples <= 0)
        {
            return 0.0;
        }

        double sum = 0.0;
        for (int i = 0; i < samples; i++)
        {
            double normalized = BitConverter.ToInt16(buffer, i * 2) / 32768.0;
            sum += normalized * normalized;
        }

        double rms = Math.Sqrt(sum / samples);
        return rms < 1e-7 ? 0.0 : Math.Round(90.0 + 20.0 * Math.Log10(rms), 1);
    }

    private void PushRing(double db)
    {
        lock (_ringGate)
        {
            _ring.Enqueue(db);
            while (_ring.Count > _ringCapacity)
            {
                _ring.Dequeue();
            }
        }
    }

    private long SessionSeconds()
    {
        try
        {
            Session? session;
            lock (_gate)
            {
                session = _session;
            }

            if (session is not null && session.StartTime > DateTime.MinValue)
            {
                DateTime start = session.StartTime.Kind == DateTimeKind.Local
                    ? session.StartTime.ToUniversalTime()
                    : DateTime.SpecifyKind(session.StartTime, DateTimeKind.Utc);
                return (long)Math.Max(0, (DateTime.UtcNow - start).TotalSeconds);
            }
        }
        catch
        {
            // 起始时间异常时退回 0
        }

        return 0;
    }

    /// <summary>隐私策略：仅当 monitor_enabled 且 monitor_always_record 且无课次时写监控切片。</summary>
    private void EnsureAutoRecording()
    {
        if (!AppConfig.Instance.Get("monitor_enabled", true) || !AppConfig.Instance.Get("monitor_always_record", false))
        {
            return;
        }

        lock (_gate)
        {
            if (_session is not null)
            {
                return;
            }

            if (_writer is not null)
            {
                if (_monitorSegmentStart != DateTime.MinValue
                    && DateTime.UtcNow - _monitorSegmentStart >= TimeSpan.FromMinutes(MonitorSegmentMinutes))
                {
                    CloseWriterNoLock();
                }
                else
                {
                    return;
                }
            }

            try
            {
                string dateDir = Path.Combine(AppConfig.Instance.Dir("recordings"), DateTime.Now.ToString("yyyy-MM-dd"));
                Directory.CreateDirectory(dateDir);
                string path = Path.Combine(dateDir, $"{DateTime.Now:HHmmss}-监控-音频.wav");
                _writer = new WaveFileWriter(path, Format);
                _monitorSegmentStart = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                _writer = null;
                Volatile.Write(ref _deviceStatus, $"监控切片失败：{ex.Message}");
            }
        }
    }

    private void AppendAsr(byte[] buffer, int count)
    {
        byte[] chunk = new byte[count];
        Buffer.BlockCopy(buffer, 0, chunk, 0, count);
        AudioChunkReady?.Invoke(chunk, DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        Func<byte[], string?>? transcriber;
        byte[]? window = null;
        lock (_asrGate)
        {
            for (int i = 0; i < count; i++)
            {
                _asrBuffer.Add(chunk[i]);
            }

            if (_asrBuffer.Count < AsrWindowBytes)
            {
                return;
            }

            window = _asrBuffer.Take(AsrWindowBytes).ToArray();
            _asrBuffer.Clear();
            transcriber = AsrTranscriber;
        }

        if (transcriber is null || window is null)
        {
            return;
        }

        _ = Task.Run(() =>
        {
            try
            {
                string? text = transcriber(window);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    AsrSegmented?.Invoke(new AsrSegment
                    {
                        Ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                        Text = text.Trim()
                    });
                }
            }
            catch
            {
                // ASR 层异常不允许影响采集
            }
        });
    }

    private void CloseWriterNoLock()
    {
        WaveFileWriter? writer = _writer;
        _writer = null;
        if (writer is null)
        {
            return;
        }

        try
        {
            writer.Flush();
        }
        catch
        {
            // 文件已关闭时忽略
        }

        try
        {
            writer.Dispose();
        }
        catch
        {
            // 同上
        }
    }

    private static string Sanitize(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "未知";
        }

        char[] invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder();
        foreach (char c in text.Trim())
        {
            builder.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
        }

        return builder.Length == 0 ? "未知" : builder.ToString();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();
        GC.SuppressFinalize(this);
    }
}
