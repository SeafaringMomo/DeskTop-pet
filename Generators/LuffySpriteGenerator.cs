using System.Windows.Media;

namespace DesktopPetCSharp.Generators;

/// <summary>
/// 路飞（蒙奇·D·路飞）像素画精灵图生成器。
/// 升级到 32×32 像素（比原 16×16 更精细），4 倍放大输出 128×128。
/// 辨识元素：草帽（麦秆黄）+ 红色帽带 + 黑发 + 肤色脸 + 大笑嘴。
/// 2 帧：睁眼 / 眨眼。
/// </summary>
public static class LuffySpriteGenerator
{
    public const int FramePx = 32;
    public const int Scale = 4;

    private static readonly Dictionary<char, Color> Palette = new()
    {
        ['.'] = Color.FromArgb(0, 0, 0, 0),
        ['T'] = Color.FromArgb(255, 232, 197, 71),   // 草帽麦秆黄
        ['t'] = Color.FromArgb(255, 200, 165, 50),   // 草帽阴影
        ['R'] = Color.FromArgb(255, 192, 57, 43),    // 帽带红
        ['H'] = Color.FromArgb(255, 26, 26, 26),     // 黑发
        ['S'] = Color.FromArgb(255, 244, 194, 161),  // 肤色
        ['s'] = Color.FromArgb(255, 220, 170, 130),  // 肤色阴影
        ['E'] = Color.FromArgb(255, 26, 26, 26),     // 眼睛
        ['M'] = Color.FromArgb(255, 139, 58, 43),    // 嘴
        ['W'] = Color.FromArgb(255, 255, 255, 255),  // 牙齿高光
        ['V'] = Color.FromArgb(255, 180, 40, 30),    // 红背心
    };

    /// <summary>第 0 帧：睁眼</summary>
    private static readonly string[] FrameOpen =
    {
        "................................",
        "................................",
        "..........TTTTTT..............",
        ".........TTTTTTTT.............",
        "........TTTTTTTTTT............",
        ".......TTTTTTTTTTTT...........",
        "......TTTTTTTTTTTTTT..........",
        ".....TTTRRRRRRRRRRTTT.........",
        "....TTTRRRRRRRRRRRRTTT........",
        "...TTTtRRRRRRRRRRRtTTT........",
        "..TTTTTTTTTTTTTTTTTTTTT.......",
        "..HHHHHHHHHHHHHHHHHHHHH.......",
        ".HHHHHHHHHHHHHHHHHHHHHHH......",
        ".HSSSSSSSSSSSSSSSSSSSSSH......",
        ".HSSSSSSSSSSSSSSSSSSSSSH......",
        ".HSSEESSSSSSSSSSEESSSSSH......",
        ".HSSEESSSSSSSSSSEESSSSSH......",
        ".HSSSSSSSSSSSSSSSSSSSSSH......",
        ".HSSSSSSSSSSSSSSSSSSSSSH......",
        ".HSSSSSSSSSSSSSSSSSSSSSH......",
        ".HSSSMMMMMMMMMMMMMMMSSSH......",
        ".HSSSWWWWWWWWWWWWWWWSSSH......",
        "..HSSSSSSSSSSSSSSSSSSSH.......",
        "...HHSSSSSSSSSSSSSSSHH........",
        "....HHHHSSSSSSSSHHHHH.........",
        ".....HHHHHHHHHHHHHH...........",
        "......VVVVVVVVVVVVV...........",
        ".....VVVVVVVVVVVVVVV..........",
        "....VVVVVVVVVVVVVVVVV.........",
        "...VVVVVVVVVVVVVVVVVVV........",
        "................................",
        "................................",
    };

    /// <summary>第 1 帧：眨眼（眼睛行替换为肤色）</summary>
    private static readonly string[] FrameBlink =
    {
        "................................",
        "................................",
        "..........TTTTTT..............",
        ".........TTTTTTTT.............",
        "........TTTTTTTTTT............",
        ".......TTTTTTTTTTTT...........",
        "......TTTTTTTTTTTTTT..........",
        ".....TTTRRRRRRRRRRTTT.........",
        "....TTTRRRRRRRRRRRRTTT........",
        "...TTTtRRRRRRRRRRRtTTT........",
        "..TTTTTTTTTTTTTTTTTTTTT.......",
        "..HHHHHHHHHHHHHHHHHHHHH.......",
        ".HHHHHHHHHHHHHHHHHHHHHHH......",
        ".HSSSSSSSSSSSSSSSSSSSSSH......",
        ".HSSSSSSSSSSSSSSSSSSSSSH......",
        ".HSSSSSSSSSSSSSSSSSSSSSH......",
        ".HSSSSSSSSSSSSSSSSSSSSSH......",
        ".HSSSSSSSSSSSSSSSSSSSSSH......",
        ".HSSSSSSSSSSSSSSSSSSSSSH......",
        ".HSSSSSSSSSSSSSSSSSSSSSH......",
        ".HSSSMMMMMMMMMMMMMMMSSSH......",
        ".HSSSWWWWWWWWWWWWWWWSSSH......",
        "..HSSSSSSSSSSSSSSSSSSSH.......",
        "...HHSSSSSSSSSSSSSSSHH........",
        "....HHHHSSSSSSSSHHHHH.........",
        ".....HHHHHHHHHHHHHH...........",
        "......VVVVVVVVVVVVV...........",
        ".....VVVVVVVVVVVVVVV..........",
        "....VVVVVVVVVVVVVVVVV.........",
        "...VVVVVVVVVVVVVVVVVVV........",
        "................................",
        "................................",
    };

    /// <summary>生成精灵图到指定路径。若已存在则跳过（便于用户手动替换）。</summary>
    public static void EnsureGenerated(string sheetPath)
    {
        var frames = new[] { FrameOpen, FrameBlink };
        PixelArtRenderer.RenderSpriteSheet(sheetPath, frames, Palette, Scale);
    }
}
