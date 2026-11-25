using AiClassAssistant.Core.Configuration;
using AiClassAssistant.Core.Contracts;
using AiClassAssistant.Core.Models;
using OpenCvSharp;
using Size = OpenCvSharp.Size;

namespace AiClassAssistant.Perception.Camera;

/// <summary>
/// 摄像头监控（OpenCvSharp DSHOW / CAP_ANY 共享取流，不设独占）。
///
/// 职责：
/// - 约 5 FPS 取帧，6x8 网格就座率与人脸数统计
/// - 每 30 秒人脸建档（时间由 Analyze 内部自行取 DateTime.UtcNow，见契约第 4 节第 3 条）
/// - 上课按课次写 AVI(MJPG)；无人上课且 monitor_always_record=false 时绝不落盘
/// - 无摄像头 / 被占用时前几次 5 秒探测、之后 60 秒，优雅降级不崩溃
/// </summary>
public sealed class CameraMonitor : ICameraMonitor
{
    private const int GridRows = 6;
    private const int GridCols = 8;
    private const int AnalyzeIntervalSeconds = 5;
    private const int FaceIntervalSeconds = 30;
    private const int FrameIntervalMs = 200; // 约 5 FPS
    private const int MonitorSegmentMinutes = 30;

    private readonly object _gate = new();
    private readonly object _captureGate = new();
    private readonly object _stateGate = new();
    private readonly object _obsGate = new();
    private readonly List<(string faceId, string seat)> _observations = new();
    private readonly FaceRegistry? _injectedRegistry;
    private readonly bool _ownsRegistry;
    private readonly int _cameraIndex;

    private FaceRegistry? _faceRegistry;
    private VideoCapture? _cap;
    private VideoWriter? _writer;
    private Session? _session;
    private Thread? _thread;
    private Size _frameSize = new(1280, 720);
    private DateTime _lastAnalyze = DateTime.MinValue;
    private DateTime _lastFaceObserve = DateTime.MinValue;
    private DateTime _monitorSegmentStart;
    private int _faceCount;
    private double _seatedRatio;
    private string _deviceStatus = "未启动";
    private bool _running;
    private bool _disposed;

    public CameraMonitor(FaceRegistry? faceRegistry = null)
    {
        _injectedRegistry = faceRegistry;
        _ownsRegistry = faceRegistry is null;
        _cameraIndex = AppConfig.Instance.Get("camera_index", 0);
    }

    public int FaceCount
    {
        get
        {
            lock (_stateGate)
            {
                return _faceCount;
            }
        }
    }

    public double SeatedRatio
    {
        get
        {
            lock (_stateGate)
            {
                return _seatedRatio;
            }
        }
    }

    /// <summary>设备状态文本（诊断用，接口外附加）。</summary>
    public string DeviceStatus => Volatile.Read(ref _deviceStatus);

    /// <summary>是否成功加载 Haar 级联（未加载时人脸数为 0，就座率按 0 处理）。</summary>
    public bool FaceDetectionAvailable => _faceRegistry?.FaceDetectionAvailable ?? false;

    public void Start()
    {
        if (_running || _disposed)
        {
            return;
        }

        _running = true;
        _faceRegistry = _injectedRegistry ?? new FaceRegistry();
        Volatile.Write(ref _deviceStatus, "连接中");
        _thread = new Thread(Loop)
        {
            IsBackground = true,
            Name = "camera-monitor"
        };
        _thread.Start();
    }

    public void Stop()
    {
        if (!_running)
        {
            return;
        }

        _running = false;
        Thread? thread = Interlocked.Exchange(ref _thread, null);
        if (thread is not null && thread != Thread.CurrentThread)
        {
            try
            {
                thread.Join(2000);
            }
            catch
            {
                // 线程已退出
            }
        }

        lock (_captureGate)
        {
            VideoCapture? cap = _cap;
            _cap = null;
            try
            {
                cap?.Dispose();
            }
            catch
            {
                // 设备已拔出时释放可能抛异常
            }
        }

        lock (_gate)
        {
            CloseWriterNoLock();
            _session = null;
            _monitorSegmentStart = DateTime.MinValue;
        }

        if (_ownsRegistry)
        {
            _faceRegistry?.Dispose();
            _faceRegistry = null;
        }

        Volatile.Write(ref _deviceStatus, "已停止");
    }

    /// <summary>取出并清空人脸观察记录（供座位表换座检测）。</summary>
    public List<(string faceId, string seat)> CollectObservations()
    {
        lock (_obsGate)
        {
            var result = new List<(string faceId, string seat)>(_observations);
            _observations.Clear();
            return result;
        }
    }

    /// <summary>开始按课次录像；先关闭旧写句柄，避免上一节课文件损坏。</summary>
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

            if (!AppConfig.Instance.Get("record_camera", true))
            {
                Volatile.Write(ref _deviceStatus, "上课录像已在设置中关闭");
                return;
            }

            try
            {
                string dateDir = Path.Combine(AppConfig.Instance.Dir("recordings"), DateTime.Now.ToString("yyyy-MM-dd"));
                Directory.CreateDirectory(dateDir);
                string path = Path.Combine(dateDir, $"{s.StartTime:HHmmss}-{Sanitize(s.Lesson.Subject)}-视频.avi");
                var writer = new VideoWriter(path, VideoWriter.FourCC('M', 'J', 'P', 'G'), 10.0, _frameSize);
                if (!writer.IsOpened())
                {
                    writer.Dispose();
                    Volatile.Write(ref _deviceStatus, "录像初始化失败（编码器不可用）");
                    return;
                }

                _writer = writer;
                s.CameraSegments.Add(path);
                Volatile.Write(ref _deviceStatus, "上课录像中");
            }
            catch (Exception ex)
            {
                _writer = null;
                Volatile.Write(ref _deviceStatus, $"录像不可用：{ex.Message}");
            }
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

        Volatile.Write(ref _deviceStatus, _running ? "取流中（不落盘）" : "已停止");
    }

    /// <summary>关闭无人上课期间的自动切片；有课次时不误伤上课录像。</summary>
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

    private void Loop()
    {
        int failures = 0;
        while (_running)
        {
            if (!EnsureCapture())
            {
                failures++;
                // 前几次 5 秒探测，之后拉长到 60 秒，避免无设备时高频重试
                Thread.Sleep(failures < 6 ? 5000 : 60000);
                continue;
            }

            failures = 0;
            Mat frame = new Mat();
            bool ok = false;
            try
            {
                ok = _cap!.Read(frame);
            }
            catch
            {
                ok = false;
            }

            if (!ok || frame.Empty())
            {
                frame.Dispose();
                Thread.Sleep(1000);
                continue;
            }

            try
            {
                OnFrame(frame);
            }
            catch
            {
                // 单帧处理异常不能终止采集线程
            }
            finally
            {
                frame.Dispose();
            }

            Thread.Sleep(FrameIntervalMs);
        }
    }

    private bool EnsureCapture()
    {
        lock (_captureGate)
        {
            if (_cap is not null && _cap.IsOpened())
            {
                return true;
            }

            try
            {
                _cap?.Dispose();
            }
            catch
            {
                // 忽略释放异常
            }
            _cap = null;

            VideoCaptureAPIs[] apis = { VideoCaptureAPIs.DSHOW, VideoCaptureAPIs.ANY };
            foreach (VideoCaptureAPIs api in apis)
            {
                try
                {
                    var cap = new VideoCapture(_cameraIndex, api);
                    if (!cap.IsOpened())
                    {
                        cap.Dispose();
                        continue;
                    }

                    cap.Set(VideoCaptureProperties.FrameWidth, 1280);
                    cap.Set(VideoCaptureProperties.FrameHeight, 720);
                    int width = (int)cap.Get(VideoCaptureProperties.FrameWidth);
                    int height = (int)cap.Get(VideoCaptureProperties.FrameHeight);
                    _frameSize = width > 0 && height > 0 ? new Size(width, height) : new Size(1280, 720);
                    _cap = cap;
                    Volatile.Write(ref _deviceStatus, "取流中");
                    return true;
                }
                catch
                {
                    // 该后端不可用，尝试下一个
                }
            }

            Volatile.Write(ref _deviceStatus, "摄像头不可用（5 秒后重试）");
            return false;
        }
    }

    private void OnFrame(Mat frame)
    {
        EnsureAutoRecording();

        lock (_gate)
        {
            if (_writer is not null)
            {
                try
                {
                    _writer.Write(frame);
                }
                catch
                {
                    // 帧尺寸与 writer 不匹配时停止本段，等待下一帧重建
                    CloseWriterNoLock();
                }
            }
        }

        DateTime now = DateTime.UtcNow;
        if ((now - _lastAnalyze).TotalSeconds >= AnalyzeIntervalSeconds)
        {
            _lastAnalyze = now;
            Analyze(frame);
        }
    }

    private void Analyze(Mat frame)
    {
        FaceRegistry? registry = _faceRegistry;
        if (registry is null || frame is null || frame.Empty())
        {
            return;
        }

        try
        {
            List<Rect> faces = registry.DetectFaces(frame);
            int height = frame.Height;
            int width = frame.Width;
            var occupied = new HashSet<(int row, int col)>();
            string seatLabel = "";
            long biggest = 0;

            foreach (Rect rect in faces)
            {
                int row = Math.Clamp((int)((rect.Y + rect.Height / 2.0) / height * GridRows), 0, GridRows - 1);
                int col = Math.Clamp((int)((rect.X + rect.Width / 2.0) / width * GridCols), 0, GridCols - 1);
                occupied.Add((row, col));
                long area = (long)rect.Width * rect.Height;
                if (area >= biggest)
                {
                    biggest = area;
                    seatLabel = $"r{row + 1}c{col + 1}"; // 1-based 标签 r行c列
                }
            }

            lock (_stateGate)
            {
                _faceCount = faces.Count;
                _seatedRatio = Math.Round(occupied.Count / (double)(GridRows * GridCols), 2);
            }

            // 契约第 4 节第 3 条：这里自行取时间，绝不引用作用域外的 now。
            DateTime now = DateTime.UtcNow;
            Session? session;
            lock (_gate)
            {
                session = _session;
            }

            if (session is not null && (now - _lastFaceObserve).TotalSeconds >= FaceIntervalSeconds)
            {
                _lastFaceObserve = now;
                List<FaceObservation> observations = registry.Observe(frame, seatLabel);
                if (observations.Count > 0)
                {
                    lock (_obsGate)
                    {
                        foreach (FaceObservation observation in observations)
                        {
                            _observations.Add((observation.FaceId, observation.Seat));
                        }
                    }
                }
            }
        }
        catch
        {
            // 画面分析失败：保持上一帧统计值
        }
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
                string path = Path.Combine(dateDir, $"{DateTime.Now:HHmmss}-监控-视频.avi");
                var writer = new VideoWriter(path, VideoWriter.FourCC('M', 'J', 'P', 'G'), 10.0, _frameSize);
                if (!writer.IsOpened())
                {
                    writer.Dispose();
                    return;
                }

                _writer = writer;
                _monitorSegmentStart = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                _writer = null;
                Volatile.Write(ref _deviceStatus, $"监控录像切片失败：{ex.Message}");
            }
        }
    }

    private void CloseWriterNoLock()
    {
        VideoWriter? writer = _writer;
        _writer = null;
        if (writer is null)
        {
            return;
        }

        try
        {
            writer.Release();
        }
        catch
        {
            // 忽略释放异常
        }

        try
        {
            writer.Dispose();
        }
        catch
        {
            // 忽略释放异常
        }
    }

    private static string Sanitize(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "未知";
        }

        char[] invalid = Path.GetInvalidFileNameChars();
        var builder = new System.Text.StringBuilder();
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
