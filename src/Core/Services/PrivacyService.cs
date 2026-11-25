using AiClassAssistant.Core.Configuration;
using AiClassAssistant.Core.Models;

namespace AiClassAssistant.Core.Services;

/// <summary>
/// 隐私策略：monitor_always_record=false 时无人上课不写任何 AVI/WAV；
/// 取流继续（分贝/就座率需要），关闭自动写入用 CloseAutoWriters 立即生效。
/// </summary>
public sealed class PrivacyService
{
    private readonly AppConfig _config;

    public PrivacyService(AppConfig config)
    {
        _config = config;
    }

    /// <summary>是否始终录制（开机录制模式）。</summary>
    public bool AlwaysRecord => _config.Get("monitor_always_record", false);

    /// <summary>是否允许为当前会话落盘媒体。</summary>
    public bool ShouldWriteMedia(Session? session)
    {
        if (AlwaysRecord)
        {
            return true;
        }

        return session is { IsActive: true };
    }

    /// <summary>带课表状态的严格判定：无人上课（无会话且非上课/课间）不落盘。</summary>
    public bool ShouldWriteMedia(Session? session, LessonState state)
    {
        if (AlwaysRecord)
        {
            return true;
        }

        if (session is not { IsActive: true })
        {
            return false;
        }

        return state is LessonState.OnClass or LessonState.Breaking;
    }

    /// <summary>
    /// 开关切换后立即调用：若当前不允许落盘则关闭感知层自动写入句柄。
    /// session 非空（正在上课）时不误伤已有录像。
    /// </summary>
    public void OnPolicyChanged(Session? session, Action? closeWriters)
    {
        if (ShouldWriteMedia(session))
        {
            return;
        }

        try
        {
            closeWriters?.Invoke();
        }
        catch
        {
            // 忽略。
        }
    }
}
