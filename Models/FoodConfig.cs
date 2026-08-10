using YamlDotNet.Serialization;

namespace DesktopPetCSharp.Models;

/// <summary>
/// 食物配置模型，对应 config/foods.yaml。
/// 食物有：名称、图标、价格、饱食度增益、角色偏好。
/// </summary>
public class FoodConfig
{
    /// <summary>所有食物列表</summary>
    [YamlMember(Alias = "foods")]
    public List<Food> Foods { get; set; } = new();
}

/// <summary>
/// 单个食物定义。
/// </summary>
public class Food
{
    /// <summary>食物唯一 ID（如 meat）</summary>
    [YamlMember(Alias = "id")]
    public string Id { get; set; } = "";

    /// <summary>显示名称（如 烤肉）</summary>
    [YamlMember(Alias = "name")]
    public string Name { get; set; } = "";

    /// <summary>Emoji 图标（如 🍖）</summary>
    [YamlMember(Alias = "icon")]
    public string Icon { get; set; } = "";

    /// <summary>价格（金币）</summary>
    [YamlMember(Alias = "price")]
    public int Price { get; set; }

    /// <summary>饱食度增益（0-100）</summary>
    [YamlMember(Alias = "satiety_gain")]
    public int SatietyGain { get; set; }

    /// <summary>角色偏好：角色中文名 → loved/normal/disliked</summary>
    [YamlMember(Alias = "role_preference")]
    public Dictionary<string, string> RolePreference { get; set; } = new();

    /// <summary>食物描述</summary>
    [YamlMember(Alias = "description")]
    public string Description { get; set; } = "";

    /// <summary>获取角色对该食物的偏好等级（默认 normal）。</summary>
    public FoodPreferenceLevel GetPreference(string role)
        => RolePreference.TryGetValue(role, out var v)
            ? ParsePreference(v)
            : FoodPreferenceLevel.Normal;

    private static FoodPreferenceLevel ParsePreference(string s)
        => s?.ToLowerInvariant() switch
        {
            "loved" => FoodPreferenceLevel.Loved,
            "disliked" => FoodPreferenceLevel.Disliked,
            _ => FoodPreferenceLevel.Normal
        };
}

/// <summary>食物偏好等级，影响喂食时好感度加成。</summary>
public enum FoodPreferenceLevel
{
    /// <summary>讨厌（×0.2 好感度）</summary>
    Disliked,
    /// <summary>普通（×1.0 好感度）</summary>
    Normal,
    /// <summary>喜欢（×1.5 好感度）</summary>
    Loved
}
