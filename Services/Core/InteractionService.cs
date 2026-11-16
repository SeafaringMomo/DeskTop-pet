using DesktopGent.Models;

namespace DesktopGent.Services.Core;

/// <summary>
/// 互动服务（P8）：管理抚摸/击掌/玩耍的冷却与好感度奖励。
///
/// 互动类型：
/// - pat（抚摸）：+1 好感度，冷却 5 秒
/// - highfive（击掌）：+2 好感度，冷却 30 秒
/// - play（玩耍）：预留接口（小游戏暂不实现），冷却 300 秒
///
/// 冷却规则：同种互动在冷却时间内重复触发会被拒绝。
/// 好感度加成：饱食度>80 时由 AffinityService 自动应用 ×1.2。
/// </summary>
public sealed class InteractionService
{
    public static InteractionService Instance { get; } = new();

    /// <summary>每个角色每种动作的最后触发时间。key = (role, actionId)。</summary>
    private readonly Dictionary<(string role, string action), DateTime> _lastTrigger = new();

    private InteractionService() { }

    // ===== 冷却查询 =====

    /// <summary>获取某动作的冷却剩余秒数（0 表示可触发）。</summary>
    public int GetCooldownRemaining(string role, string action)
    {
        if (!_lastTrigger.TryGetValue((role, action), out var last))
            return 0;

        var cd = GetCooldownSeconds(action);
        var elapsed = (DateTime.Now - last).TotalSeconds;
        if (elapsed >= cd) return 0;
        return (int)(cd - elapsed);
    }

    /// <summary>判断某动作是否可触发（冷却已结束）。</summary>
    public bool CanTrigger(string role, string action)
        => GetCooldownRemaining(role, action) == 0;

    // ===== 互动触发 =====

    /// <summary>
    /// 触发互动。若冷却中返回失败；否则扣冷却、加好感度。
    /// 返回 InteractResult（含消息、好感度变化、剩余冷却）。
    /// </summary>
    public InteractResult Interact(string role, string action)
    {
        var remaining = GetCooldownRemaining(role, action);
        if (remaining > 0)
        {
            return new InteractResult
            {
                Success = false,
                Message = $"{ActionDisplayName(action)}冷却中，还需 {remaining} 秒",
                CooldownRemaining = remaining
            };
        }

        var reward = GetReward(action);
        if (reward <= 0)
        {
            return new InteractResult
            {
                Success = false,
                Message = $"{ActionDisplayName(action)}暂未实现，敬请期待",
                CooldownRemaining = 0
            };
        }

        // 加好感度（带上饱食度加成）
        var beforeLevel = AffinityService.Instance.GetLevel(AffinityService.Instance.GetAffinity(role));
        AffinityService.Instance.AddAffinity(role, reward, applySatietyBonus: true);

        // 记录冷却
        _lastTrigger[(role, action)] = DateTime.Now;

        var after = AffinityService.Instance.GetAffinity(role);
        var level = AffinityService.Instance.GetLevel(after);
        var msg = $"{role} 接受了你的{ActionDisplayName(action)}！\n" +
                  $"好感度 +{reward} → {after}（{level.Name}）";

        Logger.Info("Interact", $"{role} 互动 {action}，好感度+{reward} → {after}（{level.Name}）");

        // 触发情绪（根据互动类型）
        switch (action)
        {
            case "pat":
                MoodService.Instance.SetMood(GentMood.Happy);
                break;
            case "highfive":
                MoodService.Instance.SetMood(GentMood.Excited);
                break;
        }

        // 等级跃迁 → 兴奋
        if (!string.Equals(beforeLevel.Name, level.Name, StringComparison.Ordinal) &&
            beforeLevel.Min < level.Min)
        {
            MoodService.Instance.SetMood(GentMood.Excited, 60_000);
        }

        return new InteractResult
        {
            Success = true,
            Message = msg,
            AffinityDelta = reward,
            AffinityAfter = after,
            LevelName = level.Name,
            CooldownRemaining = GetCooldownSeconds(action)
        };
    }

    // ===== 配置读取 =====

    private int GetCooldownSeconds(string action)
    {
        var cd = ConfigService.LoadGentManagement().Cooldown;
        return action switch
        {
            "pat" => cd.Pat,
            "highfive" => cd.Highfive,
            "play" => cd.Play,
            "feed" => cd.Feed,
            _ => 0
        };
    }

    private int GetReward(string action)
    {
        var r = ConfigService.LoadGentManagement().Rewards;
        return action switch
        {
            "pat" => r.Pat,
            "highfive" => r.Highfive,
            // play 暂未实现（小游戏），返回 0 让 Interact 提示"暂未实现"
            "play" => 0,
            _ => 0
        };
    }

    /// <summary>动作显示名（中文）。</summary>
    public static string ActionDisplayName(string action) => action switch
    {
        "pat" => "抚摸",
        "highfive" => "击掌",
        "play" => "玩耍",
        _ => action
    };
}

/// <summary>互动结果。</summary>
public class InteractResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public int AffinityDelta { get; set; }
    public int AffinityAfter { get; set; }
    public string LevelName { get; set; } = "";
    public int CooldownRemaining { get; set; }
}
