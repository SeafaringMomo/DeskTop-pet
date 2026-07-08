using YamlDotNet.Serialization;

namespace DesktopPetCSharp.Models;

/// <summary>
/// 用户档案，对应 user_data/users/&lt;name&gt;/profile.yaml。
/// 存储该用户的个人偏好与游戏数据，与全局配置隔离。
///
/// 字段分组：
/// - 基础信息：name / role / scale / default_provider / created_at / last_login_at
/// - 经济系统：coins / unlocked_skins / current_skins
/// - 好感度：affinity / last_interaction
/// - 饱食度：satiety / satiety_low_since
/// - 背包：inventory
/// </summary>
public class UserProfile
{
    // ===== 基础信息 =====

    /// <summary>用户名（唯一标识）</summary>
    [YamlMember(Alias = "name")]
    public string Name { get; set; } = "";

    /// <summary>当前角色（中文，如 路飞）</summary>
    [YamlMember(Alias = "role")]
    public string Role { get; set; } = "路飞";

    /// <summary>当前缩放比例</summary>
    [YamlMember(Alias = "scale")]
    public double Scale { get; set; } = 0.4;

    /// <summary>默认 AI 提供方名（如 local-klee / deepseek）</summary>
    [YamlMember(Alias = "default_provider")]
    public string DefaultProvider { get; set; } = "local-klee";

    /// <summary>创建时间</summary>
    [YamlMember(Alias = "created_at")]
    public string CreatedAt { get; set; } = "";

    /// <summary>最后登录时间</summary>
    [YamlMember(Alias = "last_login_at")]
    public string LastLoginAt { get; set; } = "";

    // ===== 经济系统 =====

    /// <summary>金币余额</summary>
    [YamlMember(Alias = "coins")]
    public int Coins { get; set; } = 100;

    /// <summary>已解锁皮肤 ID 列表</summary>
    [YamlMember(Alias = "unlocked_skins")]
    public List<string> UnlockedSkins { get; set; } = new();

    /// <summary>各角色当前装备的皮肤 ID（角色中文名 → 皮肤 ID）</summary>
    [YamlMember(Alias = "current_skins")]
    public Dictionary<string, string> CurrentSkins { get; set; } = new();

    // ===== 好感度系统 =====

    /// <summary>各角色好感度（角色中文名 → 0-100）</summary>
    [YamlMember(Alias = "affinity")]
    public Dictionary<string, int> Affinity { get; set; } = new();

    /// <summary>各角色最后互动时间（角色中文名 → ISO 时间戳）</summary>
    [YamlMember(Alias = "last_interaction")]
    public Dictionary<string, string> LastInteraction { get; set; } = new();

    // ===== 饱食度系统 =====

    /// <summary>各角色饱食度（角色中文名 → 0-100）</summary>
    [YamlMember(Alias = "satiety")]
    public Dictionary<string, int> Satiety { get; set; } = new();

    /// <summary>饱食度低于阈值的起始时间戳（角色中文名 → ISO，null 表示未触发）</summary>
    [YamlMember(Alias = "satiety_low_since")]
    public Dictionary<string, string?> SatietyLowSince { get; set; } = new();

    // ===== 背包 =====

    /// <summary>食物背包（食物 ID → 数量）</summary>
    [YamlMember(Alias = "inventory")]
    public Dictionary<string, int> Inventory { get; set; } = new();

    // ===== 便捷方法 =====

    /// <summary>获取角色的好感度（默认初始值）。</summary>
    public int GetAffinity(string role) => Affinity.TryGetValue(role, out var v) ? v : 50;

    /// <summary>获取角色的饱食度（默认初始值）。</summary>
    public int GetSatiety(string role) => Satiety.TryGetValue(role, out var v) ? v : 50;

    /// <summary>获取角色当前皮肤 ID（未设置返回空，由 SkinService 决定默认皮肤）。</summary>
    public string GetCurrentSkin(string role)
        => CurrentSkins.TryGetValue(role, out var v) ? v : "";

    /// <summary>设置角色当前皮肤 ID。</summary>
    public void SetCurrentSkin(string role, string skinId) => CurrentSkins[role] = skinId;

    /// <summary>获取背包中食物数量。</summary>
    public int GetInventory(string foodId) => Inventory.TryGetValue(foodId, out var v) ? v : 0;

    /// <summary>增加背包食物数量。</summary>
    public void AddInventory(string foodId, int count)
    {
        if (count <= 0) return;
        Inventory[foodId] = GetInventory(foodId) + count;
    }

    /// <summary>消耗背包食物数量。数量不足返回 false。</summary>
    public bool ConsumeInventory(string foodId, int count = 1)
    {
        var cur = GetInventory(foodId);
        if (cur < count) return false;
        Inventory[foodId] = cur - count;
        return true;
    }
}

/// <summary>
/// 用户注册表条目，对应 user_data/users.yaml 中的一项。
/// </summary>
public class UserRegistryEntry
{
    [YamlMember(Alias = "name")]
    public string Name { get; set; } = "";

    [YamlMember(Alias = "created_at")]
    public string CreatedAt { get; set; } = "";

    [YamlMember(Alias = "last_login_at")]
    public string LastLoginAt { get; set; } = "";
}

/// <summary>用户注册表，对应 user_data/users.yaml。</summary>
public class UserRegistry
{
    [YamlMember(Alias = "users")]
    public List<UserRegistryEntry> Users { get; set; } = new();
}
