namespace AiClassAssistant.Core.Events;

/// <summary>同步事件总线（契约 §3）。</summary>
public interface IEventBus
{
    void On<T>(Action<T> handler);
    void Emit<T>(T payload);
}

/// <summary>事件名常量（字符串统一，避免各处拼写不一致）。</summary>
public static class EventNames
{
    public const string ClassIslandState = "classisland.state";
    public const string PerceptionDb = "perception.db";
    public const string SessionStart = "session.start";
    public const string SessionEnd = "session.end";
    public const string SessionOvertime = "session.overtime";

    // ASR 分段沿用旧配置名，供感知层发布（附加常量，不影响上面 5 个契约名）。
    public const string PerceptionAsrSegment = "perception.asr_segment";
}

/// <summary>
/// 线程安全、同步派发的事件总线：按负载类型索引；单个处理器异常被吞掉，
/// 绝不影响发布者与其它处理器（契约 §3 与旧 events.py 行为一致）。
/// </summary>
public sealed class EventBus : IEventBus
{
    private readonly object _gate = new();
    private readonly Dictionary<Type, List<Delegate>> _handlers = new();

    public void On<T>(Action<T> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        lock (_gate)
        {
            if (!_handlers.TryGetValue(typeof(T), out List<Delegate>? list))
            {
                list = new List<Delegate>();
                _handlers[typeof(T)] = list;
            }

            list.Add(handler);
        }
    }

    /// <summary>取消订阅；未订阅时静默返回。</summary>
    public void Off<T>(Action<T> handler)
    {
        if (handler is null)
        {
            return;
        }

        lock (_gate)
        {
            if (!_handlers.TryGetValue(typeof(T), out List<Delegate>? list))
            {
                return;
            }

            list.Remove(handler);
            if (list.Count == 0)
            {
                _handlers.Remove(typeof(T));
            }
        }
    }

    public void Emit<T>(T payload)
    {
        Delegate[] snapshot;
        lock (_gate)
        {
            if (!_handlers.TryGetValue(typeof(T), out List<Delegate>? list) || list.Count == 0)
            {
                return;
            }

            snapshot = list.ToArray();
        }

        foreach (Delegate d in snapshot)
        {
            if (d is Action<T> handler)
            {
                try
                {
                    handler(payload);
                }
                catch
                {
                    // 事件处理器异常不影响总线与其它订阅者。
                }
            }
        }
    }

    /// <summary>清空订阅（测试或退出时用）。</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _handlers.Clear();
        }
    }
}
