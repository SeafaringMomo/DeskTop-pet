using YamlDotNet.Serialization;

namespace DesktopGent.Models;

/// <summary>
/// 助手管理系统常量配置模型，对应 config/gent_management.yaml。
/// 集中管理衰减规则、奖励值、冷却时间、好感度等级等常量。
/// </summary>
public class GentManagementConfig
{
    [YamlMember(Alias = "constants")]
    public ManagementConstants Constants { get; set; } = new();

    [YamlMember(Alias = "decay")]
    public DecayRules Decay { get; set; } = new();

    [YamlMember(Alias = "rewards")]
    public RewardRules Rewards { get; set; } = new();

    [YamlMember(Alias = "cooldown")]
    public CooldownRules Cooldown { get; set; } = new();

    [YamlMember(Alias = "levels")]
    public List<AffinityLevel> Levels { get; set; } = new();
}

public class ManagementConstants
{
    [YamlMember(Alias = "food_default_price")]
    public int FoodDefaultPrice { get; set; } = 20;

    [YamlMember(Alias = "initial_coins")]
    public int InitialCoins { get; set; } = 100;

    [YamlMember(Alias = "initial_affinity")]
    public int InitialAffinity { get; set; } = 50;

    [YamlMember(Alias = "initial_satiety")]
    public int InitialSatiety { get; set; } = 50;
}

public class DecayRules
{
    /// <summary>饱食度每 6 小时 -10</summary>
    [YamlMember(Alias = "satiety_loss_per_6h")]
    public int SatietyLossPer6h { get; set; } = 10;

    /// <summary>好感度每日 -2（无互动）</summary>
    [YamlMember(Alias = "affinity_loss_per_day")]
    public int AffinityLossPerDay { get; set; } = 2;

    /// <summary>饱食度低阈值（默认 20）</summary>
    [YamlMember(Alias = "satiety_low_threshold")]
    public int SatietyLowThreshold { get; set; } = 20;

    /// <summary>饱食度低于阈值持续 N 分钟后扣好感度</summary>
    [YamlMember(Alias = "satiety_low_affinity_loss_minutes")]
    public int SatietyLowAffinityLossMinutes { get; set; } = 120;

    /// <summary>静音状态每小时 -1</summary>
    [YamlMember(Alias = "muted_affinity_loss_per_hour")]
    public int MutedAffinityLossPerHour { get; set; } = 1;
}

public class RewardRules
{
    [YamlMember(Alias = "feed_loved")]
    public int FeedLoved { get; set; } = 8;

    [YamlMember(Alias = "feed_normal")]
    public int FeedNormal { get; set; } = 5;

    [YamlMember(Alias = "feed_disliked")]
    public int FeedDisliked { get; set; } = 1;

    [YamlMember(Alias = "pat")]
    public int Pat { get; set; } = 1;

    [YamlMember(Alias = "highfive")]
    public int Highfive { get; set; } = 2;

    [YamlMember(Alias = "chat_per_message")]
    public int ChatPerMessage { get; set; } = 1;

    [YamlMember(Alias = "todo_complete")]
    public int TodoComplete { get; set; } = 2;

    [YamlMember(Alias = "idle_per_hour")]
    public int IdlePerHour { get; set; } = 1;

    /// <summary>饱食度>80 时互动好感度加成（如 1.2）</summary>
    [YamlMember(Alias = "satiety_high_bonus")]
    public double SatietyHighBonus { get; set; } = 1.2;
}

public class CooldownRules
{
    [YamlMember(Alias = "feed")]
    public int Feed { get; set; } = 5;

    [YamlMember(Alias = "pat")]
    public int Pat { get; set; } = 5;

    [YamlMember(Alias = "highfive")]
    public int Highfive { get; set; } = 30;

    [YamlMember(Alias = "play")]
    public int Play { get; set; } = 300;
}

public class AffinityLevel
{
    [YamlMember(Alias = "min")]
    public int Min { get; set; }

    [YamlMember(Alias = "name")]
    public string Name { get; set; } = "";

    [YamlMember(Alias = "description")]
    public string Description { get; set; } = "";
}
