using YamlDotNet.Serialization;

namespace DesktopPetCSharp.Models;

/// <summary>
/// 动作系统配置模型，对应 config/animations.yaml。
/// 动作系统独立于皮肤：不同皮肤共用同一套动作定义，只是帧图不同。
/// </summary>
public class AnimationConfig
{
    /// <summary>动作映射：动作 ID → 动作定义</summary>
    [YamlMember(Alias = "animations")]
    public Dictionary<string, AnimationDef> Animations { get; set; } = new();
}

/// <summary>
/// 单个动作定义。动作类型分两种：
/// - sprite_sheet：帧动画（按 frames 索引切换 PetImage.Source）
/// - procedural：程序化动画（驱动 BreathScale / BobTranslate）
/// </summary>
public class AnimationDef
{
    /// <summary>动作类型：sprite_sheet / procedural</summary>
    [YamlMember(Alias = "type")]
    public string Type { get; set; } = "sprite_sheet";

    /// <summary>帧动画：帧索引列表（如 [0, 1]）</summary>
    [YamlMember(Alias = "frames")]
    public List<int> Frames { get; set; } = new();

    /// <summary>帧动画：帧切换间隔（毫秒）</summary>
    [YamlMember(Alias = "interval_ms")]
    public int IntervalMs { get; set; } = 200;

    /// <summary>程序化动画：驱动目标（scale_y / translate_y / scale）</summary>
    [YamlMember(Alias = "target")]
    public string Target { get; set; } = "";

    /// <summary>程序化动画：起始值</summary>
    [YamlMember(Alias = "from")]
    public double From { get; set; }

    /// <summary>程序化动画：结束值</summary>
    [YamlMember(Alias = "to")]
    public double To { get; set; }

    /// <summary>程序化动画：持续时长（秒）</summary>
    [YamlMember(Alias = "duration_s")]
    public double DurationS { get; set; } = 1.0;

    /// <summary>程序化动画：持续时长（毫秒，与 duration_s 二选一）</summary>
    [YamlMember(Alias = "duration_ms")]
    public int DurationMs { get; set; }

    /// <summary>程序化动画：是否自动反向（来回动画）</summary>
    [YamlMember(Alias = "auto_reverse")]
    public bool AutoReverse { get; set; }

    /// <summary>程序化动画：scale 类型的 X 缩放</summary>
    [YamlMember(Alias = "scale_x")]
    public double ScaleX { get; set; } = 1.0;

    /// <summary>程序化动画：scale 类型的 Y 缩放</summary>
    [YamlMember(Alias = "scale_y")]
    public double ScaleY { get; set; } = 1.0;

    /// <summary>重复次数（0=单次，-1=无限循环）</summary>
    [YamlMember(Alias = "repeat")]
    public string Repeat { get; set; } = "1";

    /// <summary>持续时长（毫秒，统一接口）</summary>
    public int GetDurationMs()
        => DurationMs > 0 ? DurationMs : (int)(DurationS * 1000);

    /// <summary>是否无限循环</summary>
    public bool IsForever => Repeat == "forever";
}
