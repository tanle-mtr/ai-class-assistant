using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using AiClassAssistant.Core.Configuration;
using AiClassAssistant.Core.Contracts;

namespace AiClassAssistant.Perception.Screen;

/// <summary>
/// 屏幕活跃度分析：GDI+ CopyFromScreen 抓屏 → 缩放到 160x90 → 与上一帧比较得到变化比例。
///
/// 截图只在内存中瞬时使用，绝不写任何文件到磁盘（契约 §4 / 需求 §6）。
/// Breaking 窗口内的拖堂判定由外部每个 tick 调用 <see cref="ForceSample"/> 主动采样，
/// 不必等待默认 30 秒周期（契约第 4 节第 1 条）。
/// </summary>
public sealed class ScreenAnalyzer : IScreenAnalyzer
{
    private const int SmallWidth = 160;
    private const int SmallHeight = 90;

    private readonly object _gate = new();
    private byte[]? _lastFrame;
    private Thread? _thread;
    private double _activityRatio;
    private string _deviceStatus = "未启动";
    private int _intervalSeconds = 30;
    private bool _running;
    private bool _disposed;

    /// <summary>最近一次采样的画面变化比例，0~1。</summary>
    public double ActivityRatio
    {
        get
        {
            lock (_gate)
            {
                return _activityRatio;
            }
        }
    }

    /// <summary>采样状态文本（诊断用，接口外附加）。</summary>
    public string DeviceStatus => Volatile.Read(ref _deviceStatus);

    public void Start()
    {
        if (_running || _disposed)
        {
            return;
        }

        _intervalSeconds = Math.Max(1, AppConfig.Instance.Get("screen_analysis_interval", 30));
        _running = true;
        Volatile.Write(ref _deviceStatus, "采样中");
        _thread = new Thread(Loop)
        {
            IsBackground = true,
            Name = "screen-analyzer"
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

        lock (_gate)
        {
            _lastFrame = null;
        }

        Volatile.Write(ref _deviceStatus, "已停止");
    }

    /// <summary>立即采样一次并刷新 ActivityRatio（拖堂观察窗口内由外部每个 tick 调用）。</summary>
    public void ForceSample()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            Sample();
        }
        catch
        {
            // 无桌面 / 权限不足时保持上一帧结果
        }
    }

    private void Loop()
    {
        while (_running)
        {
            try
            {
                Sample();
            }
            catch
            {
                // 单轮采样失败不影响常驻循环
            }

            for (int i = 0; i < _intervalSeconds && _running; i++)
            {
                Thread.Sleep(1000);
            }
        }
    }

    private void Sample()
    {
        byte[]? gray = GrabGray();
        if (gray is null)
        {
            Volatile.Write(ref _deviceStatus, "无可用桌面（跳过采样）");
            return;
        }

        Volatile.Write(ref _deviceStatus, "采样中");
        byte[]? previous;
        lock (_gate)
        {
            previous = _lastFrame;
            _lastFrame = gray;
        }

        double diff = 0.0;
        if (previous is not null && previous.Length == gray.Length)
        {
            long sum = 0;
            for (int i = 0; i < gray.Length; i++)
            {
                sum += Math.Abs(gray[i] - previous[i]);
            }

            diff = sum / (double)gray.Length / 255.0;
        }

        lock (_gate)
        {
            _activityRatio = Math.Round(diff, 4);
        }
    }

    /// <summary>抓全屏后缩放为 160x90 灰度数组；任何失败返回 null，不落盘。</summary>
    private static byte[]? GrabGray()
    {
        try
        {
            Rectangle bounds = System.Windows.Forms.Screen.PrimaryScreen?.Bounds ?? Rectangle.Empty;
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                return null;
            }

            using var full = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
            using (Graphics graphics = Graphics.FromImage(full))
            {
                graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size, CopyPixelOperation.SourceCopy);
            }

            using var small = new Bitmap(SmallWidth, SmallHeight, PixelFormat.Format32bppArgb);
            using (Graphics scaled = Graphics.FromImage(small))
            {
                scaled.InterpolationMode = InterpolationMode.Low;
                scaled.DrawImage(full, new Rectangle(0, 0, SmallWidth, SmallHeight));
            }

            return ToGray(small);
        }
        catch
        {
            return null;
        }
    }

    private static byte[] ToGray(Bitmap image)
    {
        var area = new Rectangle(0, 0, image.Width, image.Height);
        BitmapData data = image.LockBits(area, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            int bytes = Math.Abs(data.Stride) * image.Height;
            var buffer = new byte[bytes];
            Marshal.Copy(data.Scan0, buffer, 0, bytes);

            var gray = new byte[image.Width * image.Height];
            int index = 0;
            for (int y = 0; y < image.Height; y++)
            {
                int row = y * data.Stride;
                for (int x = 0; x < image.Width; x++)
                {
                    int offset = row + x * 4;
                    int b = buffer[offset];
                    int g = buffer[offset + 1];
                    int r = buffer[offset + 2];
                    gray[index++] = (byte)((r * 299 + g * 587 + b * 114) / 1000);
                }
            }

            return gray;
        }
        finally
        {
            image.UnlockBits(data);
        }
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
