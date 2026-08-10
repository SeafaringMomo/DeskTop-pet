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

    /// <summary>情绪覆盖配置：mood 名称 → 该情绪下的 idle 动作权重表</summary>
    [YamlMember(Alias = "mood_overrides")]
    public Dictionary<string, Dictionary<string, double>> MoodOverrides { get; set; } = new();

    /// <summary>粒子定义：粒子类型 → 粒子参数（颜色/速度/生命周期）</summary>
    [YamlMember(Alias = "particles")]
    public Dictionary<string, ParticleDef> Particles { get; set; } = new();
}

/// <summary>
/// 单个动作定义。动作类型分两种：
/// - sprite_sheet：帧动画（按 frames 索引切换 PetImage.Source）
/// - procedural：程序化动画（驱动 BreathScale / BobTranslate / RotateTransform / Opacity）
///
/// 程序化动画支持两种模式：
/// - 简单 from→to 模式（设置 from/to/duration 即可）
/// - 关键帧序列模式（设置 keyframes 列表，可同时驱动多个属性）
///
/// 动作可通过 chain 字段串行组合（如 "jump_then_blink" = 先跳再眨眼）。
/// 动作可通过 particles 字段在播放时同步触发粒子效果。
/// </summary>
public class AnimationDef
{
    /// <summary>动作类型：sprite_sheet / procedural</summary>
    [YamlMember(Alias = "type")]
    public string Type { get; set; } = "sprite_sheet";

    // ===== sprite_sheet 模式字段 =====

    /// <summary>帧动画：帧索引列表（如 [0, 1]）</summary>
    [YamlMember(Alias = "frames")]
    public List<int> Frames { get; set; } = new();

    /// <summary>帧动画：帧切换间隔（毫秒）</summary>
    [YamlMember(Alias = "interval_ms")]
    public int IntervalMs { get; set; } = 200;

    // ===== procedural 模式：简单 from→to 字段 =====

    /// <summary>程序化动画：驱动目标
    /// 可选值：scale_y / translate_y / scale / rotate / translate_x / opacity / scale_xy
    /// </summary>
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

    /// <summary>程序化动画：scale 类型的 X 缩放（target=scale 时使用）</summary>
    [YamlMember(Alias = "scale_x")]
    public double ScaleX { get; set; } = 1.0;

    /// <summary>程序化动画：scale 类型的 Y 缩放（target=scale 时使用）</summary>
    [YamlMember(Alias = "scale_y")]
    public double ScaleY { get; set; } = 1.0;

    /// <summary>程序化动画：缓动函数
    /// 可选值：linear / sine / cubic / bounce / elastic / back / quadratic
    /// 默认 sine
    /// </summary>
    [YamlMember(Alias = "easing")]
    public string Easing { get; set; } = "sine";

    // ===== procedural 模式：关键帧序列字段 =====

    /// <summary>程序化动画：关键帧序列（与 from/to 二选一）
    /// 每个关键帧可同时设置 scale/translate/rotate/opacity 多个属性
    /// </summary>
    [YamlMember(Alias = "keyframes")]
    public List<KeyframeDef> Keyframes { get; set; } = new();

    // ===== 动作链字段 =====

    /// <summary>动作链：串行播放的动作 ID 列表（如 ["jump", "blink"]）
    /// 设置该字段后，本动作变成「组合动作」，会按顺序播放每个子动作
    /// </summary>
    [YamlMember(Alias = "chain")]
    public List<string> Chain { get; set; } = new();

    // ===== 粒子触发字段 =====

    /// <summary>粒子触发：动作播放时同步触发的粒子列表</summary>
    [YamlMember(Alias = "particles")]
    public List<ParticleTrigger> Particles { get; set; } = new();

    // ===== 通用字段 =====

    /// <summary>重复次数（0=单次，-1=无限循环）</summary>
    [YamlMember(Alias = "repeat")]
    public string Repeat { get; set; } = "1";

    /// <summary>权重：在 idle 循环中根据 mood 选择动作时的概率权重（默认 0 表示不参与随机）</summary>
    [YamlMember(Alias = "weight")]
    public double Weight { get; set; } = 0;

    /// <summary>持续时长（毫秒，统一接口）</summary>
    public int GetDurationMs()
        => DurationMs > 0 ? DurationMs : (int)(DurationS * 1000);

    /// <summary>是否无限循环</summary>
    public bool IsForever => Repeat == "forever";
}

/// <summary>
/// 关键帧定义：在某个进度点（0-100）同时设置多个变换属性。
/// 未设置的属性保持动画前的值。
/// </summary>
public class KeyframeDef
{
    /// <summary>进度百分比（0-100）</summary>
    [YamlMember(Alias = "progress")]
    public double Progress { get; set; }

    /// <summary>X 缩放（null 表示不变）</summary>
    [YamlMember(Alias = "scale_x")]
    public double? ScaleX { get; set; }

    /// <summary>Y 缩放（null 表示不变）</summary>
    [YamlMember(Alias = "scale_y")]
    public double? ScaleY { get; set; }

    /// <summary>X 平移（null 表示不变，单位像素）</summary>
    [YamlMember(Alias = "translate_x")]
    public double? TranslateX { get; set; }

    /// <summary>Y 平移（null 表示不变，单位像素）</summary>
    [YamlMember(Alias = "translate_y")]
    public double? TranslateY { get; set; }

    /// <summary>旋转角度（null 表示不变，单位度）</summary>
    [YamlMember(Alias = "rotate")]
    public double? Rotate { get; set; }

    /// <summary>不透明度（null 表示不变，0-1）</summary>
    [YamlMember(Alias = "opacity")]
    public double? Opacity { get; set; }
}

/// <summary>
/// 粒子触发定义：动作播放时在某时刻生成粒子。
/// </summary>
public class ParticleTrigger
{
    /// <summary>粒子类型（heart / sweat / zzz / question / exclamation / sparkle 等）</summary>
    [YamlMember(Alias = "type")]
    public string Type { get; set; } = "heart";

    /// <summary>粒子数量</summary>
    [YamlMember(Alias = "count")]
    public int Count { get; set; } = 1;

    /// <summary>触发时机（毫秒，0=动作开始时）</summary>
    [YamlMember(Alias = "at_ms")]
    public int AtMs { get; set; } = 0;

    /// <summary>X 起始偏移（相对宠物中心，像素）</summary>
    [YamlMember(Alias = "offset_x")]
    public int OffsetX { get; set; } = 0;

    /// <summary>Y 起始偏移（相对宠物头顶，像素）</summary>
    [YamlMember(Alias = "offset_y")]
    public int OffsetY { get; set; } = 0;
}

/// <summary>
/// 粒子全局定义：粒子类型 → 渲染参数。
/// 在 animations.yaml 顶层 particles 段配置。
/// </summary>
public class ParticleDef
{
    /// <summary>渲染符号（emoji 或文本符号，如 ❤️ / 💧 / 💤）</summary>
    [YamlMember(Alias = "symbol")]
    public string Symbol { get; set; } = "❤";

    /// <summary>字体大小（像素）</summary>
    [YamlMember(Alias = "font_size")]
    public int FontSize { get; set; } = 16;

    /// <summary>颜色（ARGB 十六进制，如 #FFFF4D6E）；为空则用符号原色</summary>
    [YamlMember(Alias = "color")]
    public string? Color { get; set; }

    /// <summary>上升速度（像素/秒，正值向上飘）</summary>
    [YamlMember(Alias = "rise_speed")]
    public double RiseSpeed { get; set; } = 30;

    /// <summary>水平漂移幅度（像素，0=直线上升）</summary>
    [YamlMember(Alias = "drift")]
    public double Drift { get; set; } = 8;

    /// <summary>生命周期（毫秒）</summary>
    [YamlMember(Alias = "lifetime_ms")]
    public int LifetimeMs { get; set; } = 2000;
}
