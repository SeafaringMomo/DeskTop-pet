using System.IO;
using System.Windows.Media;
using YamlDotNet.Serialization;

namespace DesktopPetCSharp.Models;

/// <summary>
/// 对应 config/config_pet.yaml 的配置模型。
/// 重构后：静态资源按角色英文目录分类（static/luffy、static/zoro），
/// 每个角色可绑定独立配色主题（themes），切换角色时聊天/待办窗口配色随之切换。
/// </summary>
public class PetConfig
{
    /// <summary>是否启用人物语音播放</summary>
    [YamlMember(Alias = "audio")]
    public bool Audio { get; set; } = true;

    /// <summary>是否播放当前角色的背景音乐（切换角色时自动播放该角色的 bgm.mp3）</summary>
    [YamlMember(Alias = "bg_music")]
    public bool BgMusic { get; set; } = true;

    /// <summary>当前角色名（中文，如 路飞）</summary>
    [YamlMember(Alias = "role")]
    public string Role { get; set; } = "路飞";

    /// <summary>静态资源根目录（相对 BaseDir，默认 static）</summary>
    [YamlMember(Alias = "static_path")]
    public string StaticPath { get; set; } = "static";

    /// <summary>每个角色的动画参数：[帧间隔(ms), 缩放比例]</summary>
    [YamlMember(Alias = "frame_scale")]
    public Dictionary<string, List<double>> FrameScale { get; set; } = new();

    /// <summary>角色中文名 → 英文目录名映射（如 路飞 → luffy）。决定 static 下的子目录。</summary>
    [YamlMember(Alias = "roles")]
    public Dictionary<string, string> Roles { get; set; } = new();

    /// <summary>角色配色主题：角色中文名 → 配色。切换角色时聊天/待办窗口应用对应配色。</summary>
    [YamlMember(Alias = "themes")]
    public Dictionary<string, RoleTheme> Themes { get; set; } = new();

    // ===== 精灵图（Sprite Sheet）动画相关配置 =====

    [YamlMember(Alias = "sprite_file")]
    public string SpriteFile { get; set; } = "sheet.png";

    [YamlMember(Alias = "sprite_cols")]
    public int SpriteCols { get; set; } = 2;

    [YamlMember(Alias = "sprite_rows")]
    public int SpriteRows { get; set; } = 1;

    [YamlMember(Alias = "blink_interval_ms")]
    public int BlinkIntervalMs { get; set; } = 3000;

    // ===== 便捷方法 =====

    /// <summary>获取角色的英文目录名（用于定位 static/&lt;dir&gt;/）。未配置则用中文转拼音首字母或原值。</summary>
    public string GetRoleDirName(string roleName)
        => Roles.TryGetValue(roleName, out var dir) ? dir : roleName;

    /// <summary>角色资源目录的相对路径（如 static/luffy）</summary>
    public string GetRoleSubdir(string roleName)
        => Path.Combine(StaticPath, GetRoleDirName(roleName));

    /// <summary>获取角色的帧间隔（毫秒）</summary>
    public int GetFrameIntervalMs(string role)
    {
        if (FrameScale.TryGetValue(role, out var v) && v.Count > 0)
            return (int)v[0];
        return 150;
    }

    /// <summary>获取角色的缩放比例</summary>
    public double GetScale(string role)
    {
        if (FrameScale.TryGetValue(role, out var v) && v.Count > 1)
            return v[1];
        return 0.4;
    }

    /// <summary>获取角色配色主题。未配置则返回默认路飞配色。</summary>
    public RoleTheme GetTheme(string role)
        => Themes.TryGetValue(role, out var t) ? t : RoleTheme.DefaultLuffy;
}

/// <summary>
/// 角色配色主题。与角色绑定，用于聊天/待办窗口的 UI 配色，实现"切换角色即切换配色"。
/// 所有值为十六进制颜色字符串（如 "#F5D547"）。
/// </summary>
public class RoleTheme
{
    /// <summary>主色（标题栏背景、强调）</summary>
    [YamlMember(Alias = "primary")]
    public string Primary { get; set; } = "#F5D547";

    /// <summary>强调色（按钮边框、高亮）</summary>
    [YamlMember(Alias = "accent")]
    public string Accent { get; set; } = "#E74C3C";

    /// <summary>窗口背景色</summary>
    [YamlMember(Alias = "background")]
    public string Background { get; set; } = "#3D2817";

    /// <summary>输入框/列表项表面色</summary>
    [YamlMember(Alias = "surface")]
    public string Surface { get; set; } = "#5C3A1E";

    /// <summary>文字颜色</summary>
    [YamlMember(Alias = "text")]
    public string Text { get; set; } = "#FFF3D9";

    /// <summary>边框颜色</summary>
    [YamlMember(Alias = "border")]
    public string Border { get; set; } = "#F5D547";

    /// <summary>默认路飞配色（草帽黄 + 红 + 深棕）</summary>
    public static RoleTheme DefaultLuffy => new();

    /// <summary>把十六进制字符串转为 WPF Color。</summary>
    public static Color ToColor(string hex)
    {
        try
        {
            var c = (Color)ColorConverter.ConvertFromString(hex);
            return c;
        }
        catch { return Colors.White; }
    }

    [YamlIgnore]
    public Color PrimaryColor => ToColor(Primary);
    [YamlIgnore]
    public Color AccentColor => ToColor(Accent);
    [YamlIgnore]
    public Color BackgroundColor => ToColor(Background);
    [YamlIgnore]
    public Color SurfaceColor => ToColor(Surface);
    [YamlIgnore]
    public Color TextColor => ToColor(Text);
    [YamlIgnore]
    public Color BorderColor => ToColor(Border);
}
