using System.Windows.Media;

namespace DesktopPetCSharp.Generators;

/// <summary>
/// 索隆（罗罗诺亚·索隆）像素画精灵图生成器。
/// 32×32 像素，4 倍放大输出 128×128。
/// 辨识元素：绿色短发 + 黑色头巾（系头上）+ 左眼刀疤 + 严肃眼神 + 绿色肚兜 + 三刀流佩刀。
/// 2 帧：睁眼 / 眨眼。
/// </summary>
public static class ZoroSpriteGenerator
{
    public const int FramePx = 32;
    public const int Scale = 4;

    private static readonly Dictionary<char, Color> Palette = new()
    {
        ['.'] = Color.FromArgb(0, 0, 0, 0),
        ['G'] = Color.FromArgb(255, 58, 125, 58),    // 绿发/绿肚兜
        ['g'] = Color.FromArgb(255, 40, 90, 40),     // 深绿阴影
        ['B'] = Color.FromArgb(255, 30, 30, 30),     // 黑头巾
        ['b'] = Color.FromArgb(255, 50, 50, 50),     // 深灰
        ['S'] = Color.FromArgb(255, 244, 194, 161),  // 肤色
        ['s'] = Color.FromArgb(255, 220, 170, 130),  // 肤色阴影
        ['E'] = Color.FromArgb(255, 26, 26, 26),     // 眼睛
        ['C'] = Color.FromArgb(255, 180, 40, 40),    // 疤痕红
        ['M'] = Color.FromArgb(255, 120, 50, 40),    // 嘴
        ['W'] = Color.FromArgb(255, 230, 230, 230),  // 白衣
        ['K'] = Color.FromArgb(255, 90, 90, 100),    // 刀鞘灰
    };

    /// <summary>第 0 帧：睁眼（左眼有刀疤）</summary>
    private static readonly string[] FrameOpen =
    {
        "................................",
        "................................",
        ".........GGGGGGGG..............",
        "........GGGGGGGGGG.............",
        ".......GGGGGGGGGGGG............",
        "......GGGGGGGGGGGGGG...........",
        ".....GGGGGGGGGGGGGGGG..........",
        "....BBBBBBBBBBBBBBBBBB.........",
        "...BBBBBBBBBBBBBBBBBBBB........",
        "..BBBBBBBBBBBBBBBBBBBBBB.......",
        ".SSSSSSSSSSSSSSSSSSSSSSSS......",
        ".SSSSSSSSSSSSSSSSSSSSSSSS......",
        ".SCSSSSSSSSSSSSSSSSSEESSS......",
        ".SCCSSSSSSSSSSSSSSEESSSSS......",
        ".SSCSSSSSSSSSSSSSSEESSSSS......",
        ".SSSSSSSSSSSSSSSSSSSSSSSS......",
        ".SSSSSSSSSSSSSSSSSSSSSSSS......",
        ".SSSSSSSSSSSSSSSSSSSSSSSS......",
        ".SSSSSMMMMMMMMMMMSSSSSSS.......",
        ".SSSSSSSSSSSSSSSSSSSSSSS.......",
        "..SSSSSSSSSSSSSSSSSSSSSS.......",
        "...SSSSSSSSSSSSSSSSSSSSS.......",
        "....SSSSSSSSSSSSSSSSSSSS.......",
        ".....GGGGGGGGGGGGGGGGGGG.......",
        "....GGGGGGGGGGGGGGGGGGGGG......",
        "...WWWWWWGGGGGGGWWWWWWWWW......",
        "..WWWWWWWWGGGGWWWWWWWWWWWW.....",
        "..KKKWWWWWWWWWWWWWWWWKKKKK.....",
        ".KKKKKWWWWWWWWWWWWWWKKKKKKK....",
        "KKKKKKWWWWWWWWWWWWWKKKKKKKKK...",
        "................................",
        "................................",
    };

    /// <summary>第 1 帧：眨眼（眼睛行替换为肤色，保留疤痕）</summary>
    private static readonly string[] FrameBlink =
    {
        "................................",
        "................................",
        ".........GGGGGGGG..............",
        "........GGGGGGGGGG.............",
        ".......GGGGGGGGGGGG............",
        "......GGGGGGGGGGGGGG...........",
        ".....GGGGGGGGGGGGGGGG..........",
        "....BBBBBBBBBBBBBBBBBB.........",
        "...BBBBBBBBBBBBBBBBBBBB........",
        "..BBBBBBBBBBBBBBBBBBBBBB.......",
        ".SSSSSSSSSSSSSSSSSSSSSSSS......",
        ".SSSSSSSSSSSSSSSSSSSSSSSS......",
        ".SCSSSSSSSSSSSSSSSSSSSSSSS.....",
        ".SCCSSSSSSSSSSSSSSSSSSSSSS.....",
        ".SSCSSSSSSSSSSSSSSSSSSSSSS.....",
        ".SSSSSSSSSSSSSSSSSSSSSSSS......",
        ".SSSSSSSSSSSSSSSSSSSSSSSS......",
        ".SSSSSSSSSSSSSSSSSSSSSSSS......",
        ".SSSSSMMMMMMMMMMMSSSSSSS.......",
        ".SSSSSSSSSSSSSSSSSSSSSSS.......",
        "..SSSSSSSSSSSSSSSSSSSSSS.......",
        "...SSSSSSSSSSSSSSSSSSSSS.......",
        "....SSSSSSSSSSSSSSSSSSSS.......",
        ".....GGGGGGGGGGGGGGGGGGG.......",
        "....GGGGGGGGGGGGGGGGGGGGG......",
        "...WWWWWWGGGGGGGWWWWWWWWW......",
        "..WWWWWWWWGGGGWWWWWWWWWWWW.....",
        "..KKKWWWWWWWWWWWWWWWWKKKKK.....",
        ".KKKKKWWWWWWWWWWWWWWKKKKKKK....",
        "KKKKKKWWWWWWWWWWWWWKKKKKKKKK...",
        "................................",
        "................................",
    };

    public static void EnsureGenerated(string sheetPath)
    {
        var frames = new[] { FrameOpen, FrameBlink };
        PixelArtRenderer.RenderSpriteSheet(sheetPath, frames, Palette, Scale);
    }
}
