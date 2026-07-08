using YamlDotNet.Serialization;

namespace DesktopPetCSharp.Models;

/// <summary>
/// 皮肤配置模型，对应 config/skins.yaml。
/// 皮肤系统包含：声音、主题配色、角色，与角色绑定但可独立切换。
/// </summary>
public class SkinConfig
{
    /// <summary>所有皮肤列表</summary>
    [YamlMember(Alias = "skins")]
    public List<Skin> Skins { get; set; } = new();
}

/// <summary>
/// 单个皮肤定义。皮肤 = 精灵图 + 主题配色 + 声音，与角色绑定。
/// </summary>
public class Skin
{
    /// <summary>皮肤唯一 ID（如 luffy_classic）</summary>
    [YamlMember(Alias = "id")]
    public string Id { get; set; } = "";

    /// <summary>所属角色中文名（如 路飞）</summary>
    [YamlMember(Alias = "role")]
    public string Role { get; set; } = "";

    /// <summary>皮肤显示名称（如 经典路飞）</summary>
    [YamlMember(Alias = "name")]
    public string Name { get; set; } = "";

    /// <summary>精灵图目录（相对 BaseDir，如 static/luffy/classic）</summary>
    [YamlMember(Alias = "sprite_dir")]
    public string SpriteDir { get; set; } = "";

    /// <summary>皮肤配色主题（独立于角色，皮肤级）</summary>
    [YamlMember(Alias = "theme")]
    public RoleTheme Theme { get; set; } = new();

    /// <summary>皮肤声音配置</summary>
    [YamlMember(Alias = "audio")]
    public SkinAudio Audio { get; set; } = new();

    /// <summary>皮肤描述</summary>
    [YamlMember(Alias = "description")]
    public string Description { get; set; } = "";

    /// <summary>是否为该角色的默认皮肤</summary>
    [YamlMember(Alias = "is_default")]
    public bool IsDefault { get; set; }
}

/// <summary>
/// 皮肤声音配置。所有路径相对 sprite_dir；缺失则回退到默认皮肤。
/// </summary>
public class SkinAudio
{
    /// <summary>背景音乐文件名（如 bgm.mp3）</summary>
    [YamlMember(Alias = "bgm")]
    public string? Bgm { get; set; }

    /// <summary>早上问候语音</summary>
    [YamlMember(Alias = "greeting_morning")]
    public string? GreetingMorning { get; set; }

    /// <summary>中午问候语音</summary>
    [YamlMember(Alias = "greeting_noon")]
    public string? GreetingNoon { get; set; }

    /// <summary>晚上问候语音</summary>
    [YamlMember(Alias = "greeting_evening")]
    public string? GreetingEvening { get; set; }

    /// <summary>退出语音</summary>
    [YamlMember(Alias = "farewell")]
    public string? Farewell { get; set; }

    /// <summary>喂食音效（可选，缺失则跳过）</summary>
    [YamlMember(Alias = "feed_sfx")]
    public string? FeedSfx { get; set; }
}
