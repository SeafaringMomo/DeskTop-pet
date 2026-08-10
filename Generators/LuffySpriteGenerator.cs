using System.Text;
using System.Windows.Media;

namespace DesktopPetCSharp.Generators;

/// <summary>
/// 路飞（蒙奇·D·路飞）像素画精灵图生成器。
/// 辨识元素：草帽（麦秆黄）+ 红色帽带 + 黑发 + 肤色脸 + 大笑嘴。
///
/// 64×64 像素（由 32×32 设计 Upscale2x 放大），2 倍放大输出 128×128。
/// 6×5 = 30 帧精灵图（按行优先）：
///   行 0 基础：0=睁眼 1=闭眼 2=微笑 3=张嘴 4=撇眉 5=半眯眼
///   行 1 强烈：6=大笑 7=红脸 8=眨眼过渡 9=微笑过渡 10=大笑过渡 11=蓄泪
///   行 2 情绪：12=流泪 13=惊讶 14=红脸渐变 15=张大嘴 16=困倦 17=说话
///   行 3 看向：18=看上 19=看下 20=看左 21=看右 22=看左上 23=看右上
///   行 4 看向续：24=看左下 25=看右下 26=看前 27=生气瞪眼 28=得意 29=预留
/// </summary>
public static class LuffySpriteGenerator
{
    /// <summary>放大后的帧像素边长（64×64）。</summary>
    public const int FramePx = 64;
    /// <summary>放大倍数：64×64 × 2 = 128×128 输出。</summary>
    public const int Scale = 2;
    public const int Cols = 6;
    public const int Rows = 5;

    public static readonly Dictionary<char, Color> Palette = new()
    {
        ['.'] = Color.FromArgb(0, 0, 0, 0),
        ['T'] = Color.FromArgb(255, 232, 197, 71),   // 草帽麦秆黄
        ['t'] = Color.FromArgb(255, 200, 165, 50),   // 草帽阴影
        ['R'] = Color.FromArgb(255, 192, 57, 43),    // 帽带红
        ['H'] = Color.FromArgb(255, 26, 26, 26),     // 黑发
        ['S'] = Color.FromArgb(255, 244, 194, 161),  // 肤色
        ['s'] = Color.FromArgb(255, 220, 170, 130),  // 肤色阴影
        ['E'] = Color.FromArgb(255, 26, 26, 26),     // 眼睛
        ['W'] = Color.FromArgb(255, 255, 255, 255),  // 牙齿高光
        ['M'] = Color.FromArgb(255, 139, 58, 43),    // 嘴
        ['V'] = Color.FromArgb(255, 180, 40, 30),    // 红背心
        ['P'] = Color.FromArgb(255, 255, 120, 120),  // 红脸（害羞）
        ['p'] = Color.FromArgb(255, 240, 90, 90),    // 深红脸
        ['O'] = Color.FromArgb(255, 60, 30, 20),      // 张嘴嘴内
        ['B'] = Color.FromArgb(255, 100, 180, 255),  // 泪滴蓝
        ['b'] = Color.FromArgb(255, 70, 140, 220),    // 深泪滴
    };

    // ===== 32×32 基础设计（保留，由 Upscale2x 放大到 64×64）=====

    /// <summary>脸部基础行（32×32，不含眼睛/嘴部），所有帧共用。</summary>
    private static readonly string[] BaseRows =
    {
        "................................",  // 0
        "................................",  // 1
        "..........TTTTTT..............",   // 2
        ".........TTTTTTTT.............",   // 3
        "........TTTTTTTTTT............",   // 4
        ".......TTTTTTTTTTTT...........",   // 5
        "......TTTTTTTTTTTTTT..........",   // 6
        ".....TTTRRRRRRRRRRTTT.........",   // 7
        "....TTTRRRRRRRRRRRRTTT........",   // 8
        "...TTTtRRRRRRRRRRRtTTT........",   // 9
        "..TTTTTTTTTTTTTTTTTTTTT.......",   // 10
        "..HHHHHHHHHHHHHHHHHHHHH.......",   // 11
        ".HHHHHHHHHHHHHHHHHHHHHHH......",  // 12
        ".HSSSSSSSSSSSSSSSSSSSSSH......",   // 13 - 脸顶
        "PLACEHOLDER_EYES_ROW_1",            // 14 - 眼睛行1（替换）
        "PLACEHOLDER_EYES_ROW_2",            // 15 - 眼睛行2（替换）
        ".HSSSSSSSSSSSSSSSSSSSSSH......",   // 16
        ".HSSSSSSSSSSSSSSSSSSSSSH......",   // 17
        ".HSSSSSSSSSSSSSSSSSSSSSH......",   // 18
        ".HSSSSSSSSSSSSSSSSSSSSSH......",   // 19
        "PLACEHOLDER_MOUTH_ROW_1",           // 20 - 嘴行1（替换）
        "PLACEHOLDER_MOUTH_ROW_2",           // 21 - 嘴行2（替换）
        "..HSSSSSSSSSSSSSSSSSSSH.......",   // 22
        "...HHSSSSSSSSSSSSSSSHH........",   // 23
        "....HHHHSSSSSSSSHHHHH.........",   // 24
        ".....HHHHHHHHHHHHHH...........",   // 25
        "......VVVVVVVVVVVVV...........",   // 26
        ".....VVVVVVVVVVVVVVV..........",   // 27
        "....VVVVVVVVVVVVVVVVV.........",   // 28
        "...VVVVVVVVVVVVVVVVVVV........",   // 29
        "................................",  // 30
        "................................",  // 31
    };

    // ===== 眼睛行变体（32×32，30 帧用）=====
    private const string EyesOpen1 = ".HSSEESSSSSSSSSSEESSSSSH......";
    private const string EyesOpen2 = ".HSSEESSSSSSSSSSEESSSSSH......";
    private const string EyesClosed1 = ".HSSSSSSSSSSSSSSSSSSSSSH......";
    private const string EyesClosed2 = ".HSSSSSSSSSSSSSSSSSSSSSH......";
    private const string EyesSmile1 = ".HSSSSSSSSSSSSSSSSSSSSSH......";
    private const string EyesSmile2 = ".HSSssSSSSSSSSSSSSssSSSH......";
    private const string EyesFrown1 = ".HSEESSSSSSSSSSSSEESSSSH......";
    private const string EyesFrown2 = ".HSEESSSSSSSSSSSSEESSSSH......";
    private const string EyesSleepy1 = ".HSSssESSSSSSSSSSssESSSH......";
    private const string EyesSleepy2 = ".HSSssESSSSSSSSSSssESSSH......";
    private const string EyesLaugh1 = ".HSSssSSSSSSSSSSSSssSSSH......";
    private const string EyesLaugh2 = ".HSSssSSSSSSSSSSSSssSSSH......";
    private const string EyesBlush1 = ".HSSEESSSSSSSSSSEESSSSSH......";
    private const string EyesBlush2 = ".HSSEESSSSSSSSSSEESSSSSH......";
    // 过渡帧
    private const string EyesHalfBlink1 = ".HSSsESSSSSSSSSSsESSSSSH......";
    private const string EyesHalfBlink2 = ".HSSsESSSSSSSSSSsESSSSSH......";
    private const string EyesSmileHalf1 = ".HSSSSSSSSSSSSSSSSSSSSSH......";
    private const string EyesSmileHalf2 = ".HSSsSSSSSSSSSSSSsSSSSH......";
    private const string EyesLaughHalf1 = ".HSSsSSSSSSSSSSSSsSSSSH......";
    private const string EyesLaughHalf2 = ".HSSsSSSSSSSSSSSSsSSSSH......";
    // 情绪
    private const string EyesTears1 = ".HSSEEBSSSSSSSSSSEESSSSSH......";
    private const string EyesTears2 = ".HSSEEBSSSSSSSSSSBESSSSSH......";
    private const string EyesCrying1 = ".HSSBEBBSSSSSSSSBBEBSSSH......";
    private const string EyesCrying2 = ".HSSBbBBSSSSSSSSBBbBSSSH......";
    private const string EyesSurprise1 = ".HSSSEESSSSSSSSSSEEESSSH......";
    private const string EyesSurprise2 = ".HSSSEESSSSSSSSSSEEESSSH......";
    private const string EyesBlushFade1 = ".HSSEESSSSSSSSSSEESSSSSH......";
    private const string EyesBlushFade2 = ".HSSEESSSSSSSSSSEESSSSSH......";
    private const string EyesYawn1 = ".HSSssESSSSSSSSSSssESSSH......";
    private const string EyesYawn2 = ".HSSSSSSSSSSSSSSSSSSSSSH......";
    private const string EyesTalk1 = ".HSSEESSSSSSSSSSEESSSSSH......";
    private const string EyesTalk2 = ".HSSSSSSSSSSSSSSSSSSSSSH......";
    // 看向（8 方向）
    private const string EyesUp1 = ".HSSEE.SSSSSSSSS.SEESSSH......";
    private const string EyesUp2 = ".HSSEE.SSSSSSSSS.SEESSSH......";
    private const string EyesDown1 = ".HSSSSSEESSSSSSEESSSSSSSH......";
    private const string EyesDown2 = ".HSSSSSEESSSSSSEESSSSSSSH......";
    private const string EyesLeft1 = ".SSEESSSSSSSSSSSSEESSSSS.......";
    private const string EyesLeft2 = ".SSEESSSSSSSSSSSSEESSSSS.......";
    private const string EyesRight1 = ".HSSSSSSSSSSSSSSEESSSSS.......";
    private const string EyesRight2 = ".HSSSSSSSSSSSSSSEESSSSS.......";
    private const string EyesUpLeft1 = ".SSEE.SSSSSSSSSS.SEESSS.......";
    private const string EyesUpLeft2 = ".SSEE.SSSSSSSSSS.SEESSS.......";
    private const string EyesUpRight1 = ".HSSSSSSSSSSSSSS.SEESSS.......";
    private const string EyesUpRight2 = ".HSSSSSSSSSSSSSS.SEESSS.......";
    private const string EyesDownLeft1 = ".SSSSEESSSSSSSSEESSSSSSS.......";
    private const string EyesDownLeft2 = ".SSSSEESSSSSSSSEESSSSSSS.......";
    private const string EyesDownRight1 = ".HSSSSSSSSSSSSEESSSSSSS.......";
    private const string EyesDownRight2 = ".HSSSSSSSSSSSSEESSSSSSS.......";
    // 其他
    private const string EyesGlare1 = ".HSEESSSSSSSSSSSSEESSSSH......";
    private const string EyesGlare2 = ".HSEEESSSSSSSSSSSEEESSSH......";
    private const string EyesSmug1 = ".HSSsESSSSSSSSSSsESSSSSH......";
    private const string EyesSmug2 = ".HSSsESSSSSSSSSSsESSSSSH......";

    // ===== 嘴行变体（32×32，30 帧用）=====
    private const string MouthFlat1 = ".HSSSMMMMMMMMMMMMMMMSSSH......";
    private const string MouthFlat2 = ".HSSSWWWWWWWWWWWWWWWSSSH......";
    private const string MouthSmile1 = ".HSSSMMMMMMMMMMMMMMMSSSH......";
    private const string MouthSmile2 = ".HSSSWWWWWWWWWWWWWWWSSSH......";
    private const string MouthOpen1 = ".HSSSMMMMMOMMMMMMMMSSSH......";
    private const string MouthOpen2 = ".HSSSWWWWWOWWWWWWWWSSSH......";
    private const string MouthFrown1 = ".HSSSMMMMMMMMMMMMMMMSSSH......";
    private const string MouthFrown2 = ".HSSSdddddddddddddddSSSH......";
    private const string MouthLaugh1 = ".HSSMMMMMMMMMMMMMMMMMSSH......";
    private const string MouthLaugh2 = ".HSSWWWWWWWWWWWWWWWWWSSH......";
    private const string MouthSmall1 = ".HSSSSMMMMMMMMMMMSSSSSH......";
    private const string MouthSmall2 = ".HSSSSWWWWWWWWWWWSSSSSH......";
    // 过渡帧
    private const string MouthHalfSmile1 = ".HSSSMMMMMMMMMMMMMMMSSSH......";
    private const string MouthHalfSmile2 = ".HSSSWWWWWWWWWWWWWWWSSSH......";
    private const string MouthHalfLaugh1 = ".HSSSMMMMMMMMMMMMMMMSSSH......";
    private const string MouthHalfLaugh2 = ".HSSSWWWWWWWWWWWWWWWSSSH......";
    // 情绪
    private const string MouthQuiver1 = ".HSSSMMMMMMMMMMMMMMMSSSH......";
    private const string MouthQuiver2 = ".HSSSdMdMdMdMdMdMdMdSSSH......";
    private const string MouthWideOpen1 = ".HSSMMMMMOMMMMMMMMMMSSH......";
    private const string MouthWideOpen2 = ".HSSWWWWWOOWWWWWWWWWSSH......";
    private const string MouthYawn1 = ".HSSMMMMOOOOMMMMMMMMSSH......";
    private const string MouthYawn2 = ".HSSWWWWOOOOWWWWWWWWWSHH......";
    private const string MouthTalk1 = ".HSSSMMMMOMMMMMMMMMMSSSH......";
    private const string MouthTalk2 = ".HSSSWWWWOWWWWWWWWWWSSSH......";
    // 看向（嘴部保持 flat）
    private const string MouthFlatOnly1 = ".HSSSMMMMMMMMMMMMMMMSSSH......";
    private const string MouthFlatOnly2 = ".HSSSWWWWWWWWWWWWWWWSSSH......";
    // 其他
    private const string MouthSmug1 = ".HSSSMMMMMMMMMMMMMMMSSSH......";
    private const string MouthSmug2 = ".HSSSSWWWWWWWWWWWWWSSSH......";
    private const string MouthGrin1 = ".HSSMMMMMMMMMMMMMMMMMSSH......";
    private const string MouthGrin2 = ".HSSWWWWWWWWWWWWWWWWWSSH......";

    /// <summary>构建一帧 32×32 像素画：用基础行 + 替换眼睛/嘴行。</summary>
    private static string[] BuildFrame32(string eyes1, string eyes2, string mouth1, string mouth2, bool blush = false)
    {
        var rows = (string[])BaseRows.Clone();
        rows[14] = eyes1;
        rows[15] = eyes2;
        rows[20] = mouth1;
        rows[21] = mouth2;
        if (blush)
        {
            rows[17] = ".HSSPPSSSSSSSSSSSSPPSSSSSH......";
            rows[18] = ".HSSPpPSSSSSSSSSSPPpSSSSH......";
        }
        return rows;
    }

    /// <summary>把 32×32 字符网格放大 2 倍到 64×64（每个字符变 2×2 像素块）。</summary>
    private static string[] Upscale2x(string[] frame32)
    {
        var frame64 = new string[64];
        for (int y = 0; y < 32; y++)
        {
            var row = y < frame32.Length ? frame32[y] : new string('.', 32);
            var sb1 = new StringBuilder(64);
            var sb2 = new StringBuilder(64);
            for (int x = 0; x < 32; x++)
            {
                char c = x < row.Length ? row[x] : '.';
                sb1.Append(c); sb1.Append(c);
                sb2.Append(c); sb2.Append(c);
            }
            frame64[y * 2] = sb1.ToString();
            frame64[y * 2 + 1] = sb2.ToString();
        }
        return frame64;
    }

    /// <summary>构建一帧 64×64 像素画：32×32 组合 → Upscale2x。</summary>
    private static string[] BuildFrame(string eyes1, string eyes2, string mouth1, string mouth2, bool blush = false)
    {
        var frame32 = BuildFrame32(eyes1, eyes2, mouth1, mouth2, blush);
        return Upscale2x(frame32);
    }

    /// <summary>生成精灵图到指定路径。若已存在则跳过（便于用户手动替换）。</summary>
    public static void EnsureGenerated(string sheetPath)
    {
        var frames = new[]
        {
            // 行 0：基础 6 帧
            BuildFrame(EyesOpen1, EyesOpen2, MouthFlat1, MouthFlat2),                       // 0=睁眼
            BuildFrame(EyesClosed1, EyesClosed2, MouthFlat1, MouthFlat2),                   // 1=闭眼
            BuildFrame(EyesSmile1, EyesSmile2, MouthSmile1, MouthSmile2),                   // 2=微笑
            BuildFrame(EyesOpen1, EyesOpen2, MouthOpen1, MouthOpen2),                       // 3=张嘴
            BuildFrame(EyesFrown1, EyesFrown2, MouthFrown1, MouthFrown2),                   // 4=撇眉
            BuildFrame(EyesSleepy1, EyesSleepy2, MouthFlat1, MouthFlat2),                   // 5=半眯眼

            // 行 1：强烈 6 帧
            BuildFrame(EyesLaugh1, EyesLaugh2, MouthLaugh1, MouthLaugh2),                   // 6=大笑
            BuildFrame(EyesBlush1, EyesBlush2, MouthSmall1, MouthSmall2, blush: true),     // 7=红脸
            BuildFrame(EyesHalfBlink1, EyesHalfBlink2, MouthFlat1, MouthFlat2),             // 8=眨眼过渡
            BuildFrame(EyesSmileHalf1, EyesSmileHalf2, MouthHalfSmile1, MouthHalfSmile2),   // 9=微笑过渡
            BuildFrame(EyesLaughHalf1, EyesLaughHalf2, MouthHalfLaugh1, MouthHalfLaugh2),   // 10=大笑过渡
            BuildFrame(EyesTears1, EyesTears2, MouthFrown1, MouthFrown2),                   // 11=蓄泪

            // 行 2：情绪 6 帧
            BuildFrame(EyesCrying1, EyesCrying2, MouthQuiver1, MouthQuiver2),               // 12=流泪
            BuildFrame(EyesSurprise1, EyesSurprise2, MouthWideOpen1, MouthWideOpen2),       // 13=惊讶
            BuildFrame(EyesBlushFade1, EyesBlushFade2, MouthSmall1, MouthSmall2, blush: true), // 14=红脸渐变
            BuildFrame(EyesOpen1, EyesOpen2, MouthWideOpen1, MouthWideOpen2),               // 15=张大嘴
            BuildFrame(EyesYawn1, EyesYawn2, MouthYawn1, MouthYawn2),                       // 16=困倦
            BuildFrame(EyesTalk1, EyesTalk2, MouthTalk1, MouthTalk2),                       // 17=说话

            // 行 3：看向 6 帧（眼睛跟随鼠标方向）
            BuildFrame(EyesUp1, EyesUp2, MouthFlatOnly1, MouthFlatOnly2),                   // 18=看上
            BuildFrame(EyesDown1, EyesDown2, MouthFlatOnly1, MouthFlatOnly2),               // 19=看下
            BuildFrame(EyesLeft1, EyesLeft2, MouthFlatOnly1, MouthFlatOnly2),               // 20=看左
            BuildFrame(EyesRight1, EyesRight2, MouthFlatOnly1, MouthFlatOnly2),             // 21=看右
            BuildFrame(EyesUpLeft1, EyesUpLeft2, MouthFlatOnly1, MouthFlatOnly2),           // 22=看左上
            BuildFrame(EyesUpRight1, EyesUpRight2, MouthFlatOnly1, MouthFlatOnly2),         // 23=看右上

            // 行 4：看向续 + 其他 6 帧
            BuildFrame(EyesDownLeft1, EyesDownLeft2, MouthFlatOnly1, MouthFlatOnly2),       // 24=看左下
            BuildFrame(EyesDownRight1, EyesDownRight2, MouthFlatOnly1, MouthFlatOnly2),     // 25=看右下
            BuildFrame(EyesOpen1, EyesOpen2, MouthFlat1, MouthFlat2),                       // 26=看前（默认）
            BuildFrame(EyesGlare1, EyesGlare2, MouthFrown1, MouthFrown2),                   // 27=生气瞪眼
            BuildFrame(EyesSmug1, EyesSmug2, MouthSmug1, MouthSmug2),                       // 28=得意
            BuildFrame(EyesOpen1, EyesOpen2, MouthFlat1, MouthFlat2),                       // 29=预留
        };
        PixelArtRenderer.RenderSpriteSheetGrid(sheetPath, frames, Palette, Scale, Cols, Rows);
    }
}
