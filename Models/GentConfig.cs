using System.IO;
using System.Windows.Media;
using YamlDotNet.Serialization;

namespace DesktopGent.Models;

/// <summary>
/// 对应 config/app.yaml 的应用级配置 + 角色扫描结果。
/// 角色不再来自 characters.yaml，而是从 static/ 目录下扫描每个子目录的 theme.yaml 动态发现。
/// </summary>
public class GentConfig
{
    /// <summary>是否启用人物语音播放</summary>
    [YamlMember(Alias = "audio")]
    public bool Audio { get; set; } = true;

    /// <summary>是否播放背景音乐（保留字段，当前角色目录不再强制提供 BGM）</summary>
    [YamlMember(Alias = "bg_music")]
    public bool BgMusic { get; set; } = true;

    /// <summary>当前角色名（中文显示名，由 theme.yaml 的 name 定义）</summary>
    [YamlMember(Alias = "role")]
    public string Role { get; set; } = "默认角色";

    /// <summary>静态资源根目录（相对 BaseDir，默认 static）</summary>
    [YamlMember(Alias = "static_path")]
    public string StaticPath { get; set; } = "static";

    /// <summary>帧动画默认间隔（毫秒），app.yaml 可覆盖</summary>
    [YamlMember(Alias = "frame_interval_ms")]
    public int FrameIntervalMs { get; set; } = 120;

    /// <summary>随机动作（如扣鼻孔）触发间隔（毫秒），app.yaml 可覆盖</summary>
    [YamlMember(Alias = "random_action_interval_ms")]
    public int RandomActionIntervalMs { get; set; } = 9000;

    /// <summary>每个角色的动画参数：[帧间隔 ms, 缩放比例]</summary>
    [YamlMember(Alias = "frame_scale")]
    public Dictionary<string, List<double>> FrameScale { get; set; } = new();

    /// <summary>角色中文名 → 英文目录名映射（扫描 static/ 生成）</summary>
    [YamlMember(Alias = "roles")]
    public Dictionary<string, string> Roles { get; set; } = new();

    /// <summary>角色中文名 → 主题配色（读取各角色 theme.yaml）</summary>
    [YamlMember(Alias = "themes")]
    public Dictionary<string, RoleTheme> Themes { get; set; } = new();

    /// <summary>获取角色的英文目录名（未配置则退化为原值）</summary>
    public string GetRoleDirName(string roleName)
        => Roles.TryGetValue(roleName, out var dir) ? dir : roleName;

    /// <summary>角色资源目录的相对路径（如 static/default_role）</summary>
    public string GetRoleSubdir(string roleName)
        => Path.Combine(StaticPath, GetRoleDirName(roleName));

    /// <summary>获取角色的帧间隔（毫秒）</summary>
    public int GetFrameIntervalMs(string role)
    {
        if (FrameScale.TryGetValue(role, out var v) && v.Count > 0)
            return (int)v[0];
        return FrameIntervalMs;
    }

    /// <summary>获取角色的缩放比例</summary>
    public double GetScale(string role)
    {
        if (FrameScale.TryGetValue(role, out var v) && v.Count > 1)
            return v[1];
        return 0.4;
    }

    /// <summary>获取角色主题配色</summary>
    public RoleTheme GetTheme(string role)
        => Themes.TryGetValue(role, out var t) ? t : RoleTheme.DefaultRole;
}

/// <summary>
/// 角色主题配色。每个角色目录下的 theme.yaml 直接定义整套配色。
/// </summary>
public class RoleTheme
{
    /// <summary>角色显示名（来自 theme.yaml 的 name）</summary>
    [YamlMember(Alias = "name")]
    public string Name { get; set; } = "";

    [YamlMember(Alias = "primary")]
    public string Primary { get; set; } = "#F5D547";

    [YamlMember(Alias = "accent")]
    public string Accent { get; set; } = "#E74C3C";

    [YamlMember(Alias = "background")]
    public string Background { get; set; } = "#3D2817";

    [YamlMember(Alias = "surface")]
    public string Surface { get; set; } = "#5C3A1E";

    [YamlMember(Alias = "text")]
    public string Text { get; set; } = "#FFF3D9";

    [YamlMember(Alias = "border")]
    public string Border { get; set; } = "#F5D547";

    public static RoleTheme DefaultRole => new();

    public static Color ToColor(string hex)
    {
        try
        {
            return (Color)ColorConverter.ConvertFromString(hex);
        }
        catch
        {
            return Colors.White;
        }
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
