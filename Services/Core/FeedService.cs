using DesktopPetCSharp.Models;

namespace DesktopPetCSharp.Services;

/// <summary>
/// 喂食服务（P7）：管理饱食度、背包、喂食逻辑。
///
/// 饱食度规则：
/// - 初始 50，每 6 小时自然 -10（由 AffinityService 衰减计时器附带处理）
/// - 喂食 +satiety_gain（上限 100）
/// - 饱食度<20：触发饥饿状态，开始扣好感度
/// - 饱食度>80：互动好感度加成 ×1.2
///
/// 喂食流程：扣背包 → 加饱食度 → 按偏好加好感度 → 触发动画/音效
/// </summary>
public sealed class FeedService
{
    public static FeedService Instance { get; } = new();

    private FeedService() { }

    // ===== 饱食度查询/修改 =====

    /// <summary>获取角色饱食度。</summary>
    public int GetSatiety(string role)
        => UserService.Instance.CurrentUser?.GetSatiety(role) ?? 50;

    /// <summary>增加饱食度（上限 100）。</summary>
    public void AddSatiety(string role, int delta)
    {
        var user = UserService.Instance.CurrentUser;
        if (user == null) return;

        if (!user.Satiety.ContainsKey(role))
            user.Satiety[role] = ConfigService.LoadPetManagement().Constants.InitialSatiety;

        user.Satiety[role] = System.Math.Clamp(user.Satiety[role] + delta, 0, 100);
        UserService.Instance.SaveCurrentProfile();
        Logger.Info("Feed", $"饱食度变化: {role} {delta:+#;-#;0} → {user.Satiety[role]}");
    }

    /// <summary>饱食度自然衰减（每 6 小时 -10）。由外部计时器调用。</summary>
    public void DecaySatiety(string role, int delta)
    {
        AddSatiety(role, -delta);
    }

    // ===== 背包 =====

    /// <summary>获取背包中食物数量。</summary>
    public int GetInventory(string foodId)
        => UserService.Instance.CurrentUser?.GetInventory(foodId) ?? 0;

    /// <summary>购买食物（扣金币 + 入背包）。</summary>
    public bool PurchaseFood(string foodId, int count, out string failReason)
    {
        failReason = "";
        var user = UserService.Instance.CurrentUser;
        if (user == null) { failReason = "未登录"; return false; }

        var food = ConfigService.LoadFoods().Foods.FirstOrDefault(f => f.Id == foodId);
        if (food == null) { failReason = "食物不存在"; return false; }

        var totalCost = food.Price * count;
        if (user.Coins < totalCost) { failReason = $"金币不足（需 {totalCost}，当前 {user.Coins}）"; return false; }

        user.Coins -= totalCost;
        user.AddInventory(foodId, count);
        UserService.Instance.SaveCurrentProfile();
        Logger.Info("Feed", $"购买食物: {food.Name} ×{count}，花费 {totalCost} 金币");
        return true;
    }

    // ===== 喂食 =====

    /// <summary>
    /// 喂食指定角色指定食物。
    /// 返回喂食结果描述（用于 UI 提示）。
    /// </summary>
    public FeedResult Feed(string role, string foodId)
    {
        var user = UserService.Instance.CurrentUser;
        if (user == null) return new FeedResult { Success = false, Message = "未登录" };

        var food = ConfigService.LoadFoods().Foods.FirstOrDefault(f => f.Id == foodId);
        if (food == null) return new FeedResult { Success = false, Message = "食物不存在" };

        if (!user.ConsumeInventory(foodId))
            return new FeedResult { Success = false, Message = "背包中无此食物" };

        // 加饱食度
        AddSatiety(role, food.SatietyGain);

        // 按偏好加好感度
        var preference = food.GetPreference(role);
        var mgmt = ConfigService.LoadPetManagement();
        var baseAffinity = preference switch
        {
            FoodPreferenceLevel.Loved => mgmt.Rewards.FeedLoved,
            FoodPreferenceLevel.Disliked => mgmt.Rewards.FeedDisliked,
            _ => mgmt.Rewards.FeedNormal
        };

        // 饱食度>80 加成
        var afterSatiety = user.GetSatiety(role);
        var actualDelta = baseAffinity;
        if (afterSatiety > 80)
        {
            actualDelta = (int)System.Math.Round(baseAffinity * mgmt.Rewards.SatietyHighBonus);
        }
        AffinityService.Instance.AddAffinity(role, actualDelta);

        var prefDesc = preference switch
        {
            FoodPreferenceLevel.Loved => "❤️ 喜欢",
            FoodPreferenceLevel.Disliked => "💔 讨厌",
            _ => "😐 普通"
        };

        var msg = $"{role} 吃了 {food.Name}！\n" +
                  $"饱食度 +{food.SatietyGain} → {afterSatiety}\n" +
                  $"好感度 +{actualDelta}（{prefDesc}" + (afterSatiety > 80 ? " +饱腹加成" : "") + $"）";

        Logger.Info("Feed", $"喂食: {role} 吃 {food.Name}，偏好={preference}，饱食度→{afterSatiety}，好感度+{actualDelta}");

        // 按偏好触发情绪
        switch (preference)
        {
            case FoodPreferenceLevel.Loved:
                MoodService.Instance.SetMood(PetMood.Happy);
                break;
            case FoodPreferenceLevel.Disliked:
                MoodService.Instance.SetMood(PetMood.Sad);
                break;
        }

        return new FeedResult
        {
            Success = true,
            Message = msg,
            SatietyAfter = afterSatiety,
            AffinityDelta = actualDelta,
            Preference = preference
        };
    }
}

/// <summary>喂食结果。</summary>
public class FeedResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public int SatietyAfter { get; set; }
    public int AffinityDelta { get; set; }
    public FoodPreferenceLevel Preference { get; set; }
}
