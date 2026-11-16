using System.Windows.Threading;

namespace DesktopGent.Services.Core;

/// <summary>
/// 情绪服务（单例）：管理助手当前情绪状态。
///
/// GentMood 表示情绪状态（Neutral/Happy/Excited/Sad/Angry/Curious/Shy）—— 由用户操作/聊天/AI 触发。
///
/// 情绪会持续一段时间后自动回 Neutral（默认 30 秒），可通过 SetMood(mood, durationMs) 指定持续时长。
/// 情绪变化会通过 OnMoodChanged 回调通知上层订阅方。
/// </summary>
public sealed class MoodService
{
    public static MoodService Instance { get; } = new();

    private GentMood _mood = GentMood.Neutral;
    private DispatcherTimer? _moodTimer;
    private DateTime _moodSetAt = DateTime.MinValue;

    /// <summary>默认情绪持续时长（毫秒）：30 秒后自动回 Neutral。</summary>
    public const int DefaultMoodDurationMs = 30_000;

    /// <summary>当前情绪</summary>
    public GentMood Current => _mood;

    /// <summary>情绪变化回调：参数为新 mood。</summary>
    public event Action<GentMood>? OnMoodChanged;

    private MoodService() { }

    /// <summary>设置当前情绪。durationMs=0 表示永久持续（直到下次 SetMood），>0 表示持续指定时长后回 Neutral。</summary>
    public void SetMood(GentMood mood, int durationMs = DefaultMoodDurationMs)
    {
        // 同情绪且未过期，不触发事件
        if (_mood == mood && _moodTimer != null && _moodTimer.IsEnabled) return;

        _mood = mood;
        _moodSetAt = DateTime.Now;

        _moodTimer?.Stop();
        if (durationMs > 0 && mood != GentMood.Neutral)
        {
            _moodTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(durationMs)
            };
            _moodTimer.Tick += (_, _) =>
            {
                _moodTimer?.Stop();
                _moodTimer = null;
                if (_mood == mood)  // 防止期间又被改了
                {
                    _mood = GentMood.Neutral;
                    OnMoodChanged?.Invoke(GentMood.Neutral);
                    Logger.Info("Mood", $"情绪 {mood} 超时，回 Neutral");
                }
            };
            _moodTimer.Start();
        }

        OnMoodChanged?.Invoke(mood);
        Logger.Info("Mood", $"情绪切换: {mood}（持续 {durationMs}ms）");
    }

    /// <summary>立即重置为 Neutral。</summary>
    public void Reset()
    {
        SetMood(GentMood.Neutral, 0);
    }

    /// <summary>根据用户消息文本识别情绪关键词，自动设置对应情绪。</summary>
    public void DetectFromMessage(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        var lower = text.ToLowerInvariant();

        // 关键词 → 情绪映射（按优先级）
        if (ContainsAny(lower, "笨蛋", "白痴", "傻", "蠢", "讨厌", "烦", "滚"))
            SetMood(GentMood.Angry);
        else if (ContainsAny(lower, "喜欢", "爱你", "想", "抱抱", "亲", "可爱", "棒", "厉害"))
            SetMood(GentMood.Happy);
        else if (ContainsAny(lower, "为什么", "是什么", "怎么", "为什么", "?", "？", "啥"))
            SetMood(GentMood.Curious);
        else if (ContainsAny(lower, "再见", "拜拜", "走了", "睡觉", "晚安"))
            SetMood(GentMood.Sad);
        else if (ContainsAny(lower, "夸", "好厉害", "挚友", "升级"))
            SetMood(GentMood.Excited);
        // 默认不改变情绪
    }

    private static bool ContainsAny(string text, params string[] keywords)
    {
        foreach (var k in keywords)
            if (text.Contains(k, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }
}

/// <summary>助手情绪枚举。</summary>
public enum GentMood
{
    /// <summary>中性（默认）</summary>
    Neutral,
    /// <summary>开心</summary>
    Happy,
    /// <summary>兴奋（升级、达到目标）</summary>
    Excited,
    /// <summary>伤心</summary>
    Sad,
    /// <summary>生气</summary>
    Angry,
    /// <summary>好奇（思考、提问）</summary>
    Curious,
    /// <summary>害羞</summary>
    Shy,
}
