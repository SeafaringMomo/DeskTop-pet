using System.Windows.Threading;
using DesktopGent.Models;

namespace DesktopGent.Services.Core;

/// <summary>
/// 好感度服务（P6）：管理角色好感度等级、增减、衰减结算。
///
/// 衰减规则（后台计时器每分钟检查一次）：
/// - 1 天不互动：每日扣 2 点
/// - 饱食度低于阈值持续 2 小时：每 2 小时扣 1 点
/// - 静音状态：每小时扣 1 点
///
/// 等级影响：通过 ChatOrchestrator 注入系统提示，影响 AI 对话风格。
/// </summary>
public sealed class AffinityService
{
    public static AffinityService Instance { get; } = new();

    private readonly DispatcherTimer _timer;
    private DateTime _lastTick = DateTime.Now;

    private AffinityService()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _timer.Tick += (_, _) => DecayTick();
    }

    /// <summary>启动衰减后台计时器。在 App.OnStartup 用户登录后调用。</summary>
    public void Start()
    {
        _lastTick = DateTime.Now;
        _timer.Start();
        Logger.Info("Affinity", "好感度衰减计时器已启动（1 分钟/次）");
    }

    /// <summary>停止衰减后台计时器。</summary>
    public void Stop() => _timer.Stop();

    // ===== 等级查询 =====

    /// <summary>根据好感度值获取等级（从高到低匹配，第一个满足 min 的等级生效）。</summary>
    public AffinityLevel GetLevel(int affinity)
    {
        var levels = ConfigService.LoadGentManagement().Levels;
        if (levels.Count == 0)
            return new AffinityLevel { Min = 0, Name = "陌生人" };

        foreach (var lv in levels.OrderByDescending(l => l.Min))
        {
            if (affinity >= lv.Min) return lv;
        }
        return levels.Last();
    }

    /// <summary>获取用户对某角色的好感度。</summary>
    public int GetAffinity(string role)
        => UserService.Instance.CurrentUser?.GetAffinity(role) ?? 50;

    /// <summary>获取当前角色的好感度等级。</summary>
    public AffinityLevel GetCurrentLevel(string role)
        => GetLevel(GetAffinity(role));

    // ===== 好感度增减 =====

    /// <summary>增加好感度（带上限 100）。饱食度>80 时有加成。</summary>
    public void AddAffinity(string role, int delta, bool applySatietyBonus = false)
    {
        if (delta == 0) return;
        var user = UserService.Instance.CurrentUser;
        if (user == null) return;

        var mgmt = ConfigService.LoadGentManagement();

        // 饱食度加成
        if (applySatietyBonus && delta > 0)
        {
            var satiety = user.GetSatiety(role);
            if (satiety > 80)
            {
                delta = (int)Math.Round(delta * mgmt.Rewards.SatietyHighBonus);
            }
        }

        if (!user.Affinity.ContainsKey(role))
            user.Affinity[role] = mgmt.Constants.InitialAffinity;

        user.Affinity[role] = Math.Clamp(user.Affinity[role] + delta, 0, 100);
        user.LastInteraction[role] = DateTime.Now.ToString("o");
        UserService.Instance.SaveCurrentProfile();
        Logger.Info("Affinity", $"好感度变化: {role} {delta:+#;-#;0} → {user.Affinity[role]}");
    }

    /// <summary>减少好感度（带下限 0）。</summary>
    public void SubtractAffinity(string role, int delta)
    {
        if (delta <= 0) return;
        var user = UserService.Instance.CurrentUser;
        if (user == null) return;

        if (!user.Affinity.ContainsKey(role))
            user.Affinity[role] = ConfigService.LoadGentManagement().Constants.InitialAffinity;

        user.Affinity[role] = Math.Clamp(user.Affinity[role] - delta, 0, 100);
        UserService.Instance.SaveCurrentProfile();
        Logger.Info("Affinity", $"好感度衰减: {role} -{delta} → {user.Affinity[role]}");
    }

    // ===== 衰减结算 =====

    private void DecayTick()
    {
        var user = UserService.Instance.CurrentUser;
        if (user == null) return;

        var now = DateTime.Now;
        var elapsed = now - _lastTick;
        _lastTick = now;

        var mgmt = ConfigService.LoadGentManagement();
        var cfg = ConfigService.Load();

        foreach (var role in cfg.Roles.Keys)
        {
            // 1) 每日不互动扣 2 点
            CheckDailyDecay(user, role, now, mgmt);

            // 2) 饱食度低于阈值持续 2 小时扣 1 点
            CheckSatietyLowDecay(user, role, now, mgmt);

            // 3) 静音状态每小时扣 1 点（按本次 tick 累计分钟数比例）
            if (App.IsMuted && elapsed.TotalMinutes >= 60)
            {
                var hours = (int)(elapsed.TotalMinutes / 60);
                if (hours > 0)
                {
                    SubtractAffinity(role, mgmt.Decay.MutedAffinityLossPerHour * hours);
                }
            }
        }
    }

    private void CheckDailyDecay(UserProfile user, string role, DateTime now, GentManagementConfig mgmt)
    {
        if (!user.LastInteraction.TryGetValue(role, out var lastStr)) return;
        if (!DateTime.TryParse(lastStr, out var last)) return;

        var days = (now - last).TotalDays;
        if (days >= 1)
        {
            // 每满 1 天扣 2 点（避免重复扣，更新 last_interaction 到最近一次扣款时间）
            var daysToDecay = (int)days;
            if (daysToDecay > 0)
            {
                SubtractAffinity(role, mgmt.Decay.AffinityLossPerDay * daysToDecay);
                // 更新最后互动时间到现在（已扣款），避免下次 tick 重复扣
                user.LastInteraction[role] = now.ToString("o");
                UserService.Instance.SaveCurrentProfile();
            }
        }
    }

    private void CheckSatietyLowDecay(UserProfile user, string role, DateTime now, GentManagementConfig mgmt)
    {
        var satiety = user.GetSatiety(role);
        var threshold = mgmt.Decay.SatietyLowThreshold;

        if (satiety < threshold)
        {
            // 记录低饱食度起始时间
            if (!user.SatietyLowSince.TryGetValue(role, out var sinceStr) || string.IsNullOrEmpty(sinceStr))
            {
                user.SatietyLowSince[role] = now.ToString("o");
                UserService.Instance.SaveCurrentProfile();
                Logger.Info("Affinity", $"{role} 饱食度低于 {threshold}，开始计时");
                return;
            }

            if (DateTime.TryParse(sinceStr, out var since))
            {
                var elapsedMin = (now - since).TotalMinutes;
                var thresholdMin = mgmt.Decay.SatietyLowAffinityLossMinutes;

                // 每满 thresholdMin 分钟扣 1 点
                if (elapsedMin >= thresholdMin)
                {
                    SubtractAffinity(role, 1);
                    // 重置计时（已扣款）
                    user.SatietyLowSince[role] = now.ToString("o");
                    UserService.Instance.SaveCurrentProfile();
                }
            }
        }
        else
        {
            // 饱食度恢复，清空计时
            if (user.SatietyLowSince.TryGetValue(role, out var sinceStr) && !string.IsNullOrEmpty(sinceStr))
            {
                user.SatietyLowSince[role] = null;
                UserService.Instance.SaveCurrentProfile();
                Logger.Info("Affinity", $"{role} 饱食度恢复，停止好感度扣减");
            }
        }
    }

    /// <summary>获取等级对应的系统提示（注入 AI 对话）。</summary>
    public string GetSystemPromptHint(string role)
    {
        var affinity = GetAffinity(role);
        var level = GetLevel(affinity);
        return level.Name switch
        {
            "挚友" => $"（你与对方好感度极高，是挚友关系，可以非常亲密地称呼对方）",
            "亲密" => $"（你与对方关系亲密，可以亲近地交流）",
            "友好" => $"（你与对方是朋友，可以主动关心对方）",
            "认识" => $"（你认识对方，偶尔可以热情一些）",
            _ => $"（你与对方还不太熟，保持礼貌的距离感）"
        };
    }
}
