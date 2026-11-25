using System.Windows.Forms;
using AiClassAssistant.Ui.Rendering;

namespace AiClassAssistant.Ui.Shell;

/// <summary>
/// 视图基类：承载顶栏动态标题、进入视图时的数据刷新（CancellationToken 防重复）、定时轮询。
/// </summary>
public abstract class ViewBase : Canvas
{
    private CancellationTokenSource? _refreshCts;
    private System.Windows.Forms.Timer? _pollTimer;

    protected ViewBase(string title)
    {
        Title = title;
        Dock = DockStyle.Fill;
    }

    /// <summary>顶栏显示的动态标题。</summary>
    public string Title { get; protected set; }

    /// <summary>进入视图时调用：取消上一次刷新并重新加载。</summary>
    public virtual void OnShow() => Reload();

    /// <summary>取消旧刷新并触发一次新刷新（避开 Control.Refresh）。</summary>
    public void Reload()
    {
        _refreshCts?.Cancel();
        _refreshCts = new CancellationTokenSource();
        DoRefresh(_refreshCts.Token);
    }

    private async void DoRefresh(CancellationToken ct)
    {
        try
        {
            await LoadAsync(ct);
        }
        catch (OperationCanceledException)
        {
            // 被新一次刷新取消，正常。
        }
        catch
        {
            // 后端不可用时保持空态，不白屏不崩溃。
        }
        finally
        {
            if (!ct.IsCancellationRequested)
            {
                Invalidate();
            }
        }
    }

    /// <summary>子类重写以加载数据；可按需检查 ct。</summary>
    protected virtual Task LoadAsync(CancellationToken ct)
    {
        Invalidate();
        return Task.CompletedTask;
    }

    /// <summary>启动/重启定时轮询（毫秒）。</summary>
    protected void StartPolling(int intervalMs, Action onTick)
    {
        _pollTimer?.Dispose();
        _pollTimer = new System.Windows.Forms.Timer { Interval = Math.Max(100, intervalMs) };
        _pollTimer.Tick += (_, _) =>
        {
            try
            {
                onTick();
            }
            catch
            {
                // 轮询异常不应击穿 UI 线程。
            }
        };
        _pollTimer.Start();
    }

    /// <summary>停止定时轮询。</summary>
    protected void StopPolling()
    {
        _pollTimer?.Stop();
        _pollTimer?.Dispose();
        _pollTimer = null;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _refreshCts?.Cancel();
            _refreshCts?.Dispose();
            _pollTimer?.Dispose();
            _refreshCts = null;
            _pollTimer = null;
        }

        base.Dispose(disposing);
    }
}
