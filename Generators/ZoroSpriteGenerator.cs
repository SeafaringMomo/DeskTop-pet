using System.Text;
using System.Windows.Media;

namespace DesktopPetCSharp.Generators;

/// <summary>
/// 索隆（罗罗诺亚·索隆）像素画精灵图生成器。
/// 辨识元素：绿色短发 + 黑色头巾（系头上）+ 左眼刀疤 + 严肃眼神 + 绿色肚兜 + 三刀流佩刀。
///
/// 64×64 像素（由 32×32 设计 Upscale2x 放大），2 倍放大输出 128×128。
/// 6×5 = 30 帧精灵图（按行优先，与 Luffy 保持一致）：
///   行 0 基础：0=睁眼 1=闭眼 2=微笑 3=张嘴 4=撇眉 5=半眯眼
///   行 1 强烈：6=大笑 7=红脸 8=眨眼过渡 9=微笑过渡 10=大笑过渡 11=蓄泪
///   行 2 情绪：12=流泪 13=惊讶 14=红脸渐变 15=张大嘴 16=困倦 17=说话
///   行 3 看向：18=看上 19=看下 20=看左 21=看右 22=看左上 23=看右上
///   行 4 看向续：24=看左下 25=看右下 26=看前 27=生气瞪眼 28=得意 29=预留
/// </summary>
public static class ZoroSpriteGenerator
{
    public const int FramePx = 64;
    public const int Scale = 2;
    public const int Cols = 6;
    public const int Rows = 5;

    public static readonly Dictionary<char, Color> Palette = new()
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
        ['O'] = Color.FromArgb(255, 60, 30, 20),     // 张嘴嘴内
        ['P'] = Color.FromArgb(255, 255, 120, 120),  // 红脸（害羞）
        ['p'] = Color.FromArgb(255, 240, 90, 90),    // 深红脸
        ['d'] = Color.FromArgb(255, 180, 120, 100),  // 撇嘴阴影
        ['L'] = Color.FromArgb(255, 100, 180, 255),  // 泪滴蓝
        ['l'] = Color.FromArgb(255, 70, 140, 220),   // 深泪滴
    };

    /// <summary>脸部基础行（32×32，不含眼睛/嘴部），所有帧共用。</summary>
    private static readonly string[] BaseRows =
    {
        "................................",  // 0
        "................................",  // 1
        ".........GGGGGGGG..............",   // 2
        "........GGGGGGGGGG.............",   // 3
        ".......GGGGGGGGGGGG............",   // 4
        "......GGGGGGGGGGGGGG...........",   // 5
        ".....GGGGGGGGGGGGGGGG..........",   // 6
        "....BBBBBBBBBBBBBBBBBB.........",   // 7
        "...BBBBBBBBBBBBBBBBBBBB........",   // 8
        "..BBBBBBBBBBBBBBBBBBBBBB.......",   // 9
        ".SSSSSSSSSSSSSSSSSSSSSSSS......",  // 10
        ".SSSSSSSSSSSSSSSSSSSSSSSS......",  // 11
        "PLACEHOLDER_EYES_ROW_1",            // 12 - 眼睛行1（替换，保留左眼刀疤）
        "PLACEHOLDER_EYES_ROW_2",            // 13 - 眼睛行2（替换）
        ".SSCSSSSSSSSSSSSSSEESSSSS......",   // 14 - 刀疤下沿
        ".SSSSSSSSSSSSSSSSSSSSSSSS......",  // 15
        ".SSSSSSSSSSSSSSSSSSSSSSSS......",  // 16
        ".SSSSSSSSSSSSSSSSSSSSSSSS......",  // 17
        ".SSSSSSSSSSSSSSSSSSSSSSSS......",  // 18
        "PLACEHOLDER_MOUTH_ROW_1",           // 19 - 嘴行1（替换）
        ".SSSSSSSSSSSSSSSSSSSSSSS.......",   // 20
        "..SSSSSSSSSSSSSSSSSSSSSS.......",   // 21
        "...SSSSSSSSSSSSSSSSSSSSS.......",   // 22
        "....SSSSSSSSSSSSSSSSSSSS.......",   // 23
        ".....GGGGGGGGGGGGGGGGGGG.......",   // 24
        "....GGGGGGGGGGGGGGGGGGGGG......",   // 25
        "...WWWWWWGGGGGGGWWWWWWWWW......",  // 26
        "..WWWWWWWWGGGGWWWWWWWWWWWW.....",  // 27
        "..KKKWWWWWWWWWWWWWWWWKKKKK.....",  // 28
        ".KKKKKWWWWWWWWWWWWWWKKKKKKK....",   // 29
        "KKKKKKWWWWWWWWWWWWWKKKKKKKKK...",  // 30
        "................................",  // 31
    };

    // ===== 眼睛行变体（32×32，保留左眼刀疤 C）=====
    private const string EyesOpen1 = ".SCSSSSSSSSSSSSSSSSEESSS......";
    private const string EyesOpen2 = ".SCCSSSSSSSSSSSSSSEESSSSS......";
    private const string EyesClosed1 = ".SSSSSSSSSSSSSSSSSSSSSSSS......";
    private const string EyesClosed2 = ".SCCSSSSSSSSSSSSSSSSSSSSS......";
    private const string EyesSmile1 = ".SCSSSSSSSSSSSSSSSSSSSSSSSS......";
    private const string EyesSmile2 = ".SCCssSSSSSSSSSSSSssSSSSS......";
    private const string EyesFrown1 = ".SCSEESSSSSSSSSSSSEESSSSS......";
    private const string EyesFrown2 = ".SCSEESSSSSSSSSSSSEESSSSS......";
    private const string EyesSleepy1 = ".SCSssESSSSSSSSSSssESSSSS......";
    private const string EyesSleepy2 = ".SCSssESSSSSSSSSSssESSSSS......";
    private const string EyesLaugh1 = ".SCSSssSSSSSSSSSSSSssSSSSS......";
    private const string EyesLaugh2 = ".SCSSssSSSSSSSSSSSSssSSSSS......";
    private const string EyesBlush1 = ".SCSSSSSSSSSSSSSSSSEESSS......";
    private const string EyesBlush2 = ".SCCSSSSSSSSSSSSSSEESSSSS......";
    // 过渡帧
    private const string EyesHalfBlink1 = ".SCSSsESSSSSSSSSSsESSSSS......";
    private const string EyesHalfBlink2 = ".SCSSsESSSSSSSSSSsESSSSS......";
    private const string EyesSmileHalf1 = ".SCSSSSSSSSSSSSSSSSSSSSSSSS......";
    private const string EyesSmileHalf2 = ".SCSssSSSSSSSSSSSSsSSSSS......";
    private const string EyesLaughHalf1 = ".SCSSsSSSSSSSSSSSSsSSSSS......";
    private const string EyesLaughHalf2 = ".SCSSsSSSSSSSSSSSSsSSSSS......";
    // 情绪
    private const string EyesTears1 = ".SCSEELSSSSSSSSSSEESSSSS......";
    private const string EyesTears2 = ".SCSEELSSSSSSSSSSLESSSSS......";
    private const string EyesCrying1 = ".SCSLELLSSSSSSSSLELELSSS......";
    private const string EyesCrying2 = ".SCSlLllSSSSSSSSllLlLSSS......";
    private const string EyesSurprise1 = ".SCSSSEESSSSSSSSSSEEESSSSS......";
    private const string EyesSurprise2 = ".SCSSSEESSSSSSSSSSEEESSSSS......";
    private const string EyesBlushFade1 = ".SCSSSSSSSSSSSSSSSSEESSS......";
    private const string EyesBlushFade2 = ".SCCSSSSSSSSSSSSSSEESSSSS......";
    private const string EyesYawn1 = ".SCSssESSSSSSSSSSssESSSSS......";
    private const string EyesYawn2 = ".SCSSSSSSSSSSSSSSSSSSSSSS......";
    private const string EyesTalk1 = ".SCSEESSSSSSSSSSEESSSSS......";
    private const string EyesTalk2 = ".SCSSSSSSSSSSSSSSSSSSSSSS......";
    // 看向（8 方向，保留左眼刀疤 C）
    private const string EyesUp1 = ".SCSEE.SSSSSSSSS.SEESSS......";
    private const string EyesUp2 = ".SCSEE.SSSSSSSSS.SEESSS......";
    private const string EyesDown1 = ".SCSSSSEESSSSSSEESSSSSSS......";
    private const string EyesDown2 = ".SCSSSSEESSSSSSEESSSSSSS......";
    private const string EyesLeft1 = ".SCEESSSSSSSSSSSSEESSSSS......";
    private const string EyesLeft2 = ".SCEESSSSSSSSSSSSEESSSSS......";
    private const string EyesRight1 = ".SCSSSSSSSSSSSSSSEESSSSS......";
    private const string EyesRight2 = ".SCSSSSSSSSSSSSSSEESSSSS......";
    private const string EyesUpLeft1 = ".SCEE.SSSSSSSSSS.SEESSS......";
    private const string EyesUpLeft2 = ".SCEE.SSSSSSSSSS.SEESSS......";
    private const string EyesUpRight1 = ".SCSSSSSSSSSSSSSS.SEESSS......";
    private const string EyesUpRight2 = ".SCSSSSSSSSSSSSSS.SEESSS......";
    private const string EyesDownLeft1 = ".SCSSSEESSSSSSSSEESSSSSSS......";
    private const string EyesDownLeft2 = ".SCSSSEESSSSSSSSEESSSSSSS......";
    private const string EyesDownRight1 = ".SCSSSSSSSSSSSSEESSSSSSS......";
    private const string EyesDownRight2 = ".SCSSSSSSSSSSSSEESSSSSSS......";
    // 其他
    private const string EyesGlare1 = ".SCSEESSSSSSSSSSSSEESSSSS......";
    private const string EyesGlare2 = ".SCSEEESSSSSSSSSSSEEESSSSS......";
    private const string EyesSmug1 = ".SCSSsESSSSSSSSSSsESSSSS......";
    private const string EyesSmug2 = ".SCSSsESSSSSSSSSSsESSSSS......";

    // ===== 嘴行变体（32×32，30 帧用）=====
    private const string MouthFlat1 = ".SSSSSMMMMMMMMMMMSSSSSSS.......";
    private const string MouthSmile1 = ".SSSSSMMMMMMMMMMMSSSSSSS.......";
    private const string MouthOpen1 = ".SSSSSMMMMOMMMMMMMSSSSSS.......";
    private const string MouthFrown1 = ".SSSSSdddddddddddSSSSSSS.......";
    private const string MouthLaugh1 = ".SSSSMMMMMMMMMMMMMMMSSSS.......";
    private const string MouthSmall1 = ".SSSSSSMMMMMMMMMSSSSSSSS.......";
    // 过渡帧
    private const string MouthHalfSmile1 = ".SSSSSMMMMMMMMMMMSSSSSSS.......";
    private const string MouthHalfLaugh1 = ".SSSSSMMMMMMMMMMMMMSSSSSS.......";
    // 情绪
    private const string MouthQuiver1 = ".SSSSSdMdMdMdMdMdMdSSSSSSS.......";
    private const string MouthWideOpen1 = ".SSSMMMMOMMMMMMMMMMMSSSS.......";
    private const string MouthYawn1 = ".SSSMMMOOOOMMMMMMMMMSSSS.......";
    private const string MouthTalk1 = ".SSSSSMMMMOMMMMMMMMMSSSSSS.......";
    // 看向（嘴部保持 flat）
    private const string MouthFlatOnly1 = ".SSSSSMMMMMMMMMMMSSSSSSS.......";
    // 其他
    private const string MouthSmug1 = ".SSSSSMMMMMMMMMMMSSSSSSS.......";
    private const string MouthGrin1 = ".SSSSMMMMMMMMMMMMMMMSSSS.......";

    /// <summary>构建一帧 32×32 像素画：用基础行 + 替换眼睛/嘴行。</summary>
    private static string[] BuildFrame32(string eyes1, string eyes2, string mouth1, bool blush = false)
    {
        var rows = (string[])BaseRows.Clone();
        rows[12] = eyes1;
        rows[13] = eyes2;
        rows[19] = mouth1;
        if (blush)
        {
            rows[16] = ".SSPPSSSSSSSSSSSSPPSSSSSSS.......";
            rows[17] = ".SSPpPSSSSSSSSSSPPpSSSSSS.......";
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
    private static string[] BuildFrame(string eyes1, string eyes2, string mouth1, bool blush = false)
    {
        var frame32 = BuildFrame32(eyes1, eyes2, mouth1, blush);
        return Upscale2x(frame32);
    }

    /// <summary>生成精灵图到指定路径。若已存在则跳过（便于用户手动替换）。</summary>
    public static void EnsureGenerated(string sheetPath)
    {
        var frames = new[]
        {
            // 行 0：基础 6 帧
            BuildFrame(EyesOpen1, EyesOpen2, MouthFlat1),                       // 0=睁眼
            BuildFrame(EyesClosed1, EyesClosed2, MouthFlat1),                   // 1=闭眼
            BuildFrame(EyesSmile1, EyesSmile2, MouthSmile1),                   // 2=微笑
            BuildFrame(EyesOpen1, EyesOpen2, MouthOpen1),                       // 3=张嘴
            BuildFrame(EyesFrown1, EyesFrown2, MouthFrown1),                   // 4=撇眉
            BuildFrame(EyesSleepy1, EyesSleepy2, MouthFlat1),                   // 5=半眯眼

            // 行 1：强烈 6 帧
            BuildFrame(EyesLaugh1, EyesLaugh2, MouthLaugh1),                   // 6=大笑
            BuildFrame(EyesBlush1, EyesBlush2, MouthSmall1, blush: true),       // 7=红脸
            BuildFrame(EyesHalfBlink1, EyesHalfBlink2, MouthFlat1),             // 8=眨眼过渡
            BuildFrame(EyesSmileHalf1, EyesSmileHalf2, MouthHalfSmile1),       // 9=微笑过渡
            BuildFrame(EyesLaughHalf1, EyesLaughHalf2, MouthHalfLaugh1),       // 10=大笑过渡
            BuildFrame(EyesTears1, EyesTears2, MouthFrown1),                   // 11=蓄泪

            // 行 2：情绪 6 帧
            BuildFrame(EyesCrying1, EyesCrying2, MouthQuiver1),               // 12=流泪
            BuildFrame(EyesSurprise1, EyesSurprise2, MouthWideOpen1),           // 13=惊讶
            BuildFrame(EyesBlushFade1, EyesBlushFade2, MouthSmall1, blush: true), // 14=红脸渐变
            BuildFrame(EyesOpen1, EyesOpen2, MouthWideOpen1),                   // 15=张大嘴
            BuildFrame(EyesYawn1, EyesYawn2, MouthYawn1),                       // 16=困倦
            BuildFrame(EyesTalk1, EyesTalk2, MouthTalk1),                       // 17=说话

            // 行 3：看向 6 帧
            BuildFrame(EyesUp1, EyesUp2, MouthFlatOnly1),                       // 18=看上
            BuildFrame(EyesDown1, EyesDown2, MouthFlatOnly1),                   // 19=看下
            BuildFrame(EyesLeft1, EyesLeft2, MouthFlatOnly1),                   // 20=看左
            BuildFrame(EyesRight1, EyesRight2, MouthFlatOnly1),                   // 21=看右
            BuildFrame(EyesUpLeft1, EyesUpLeft2, MouthFlatOnly1),               // 22=看左上
            BuildFrame(EyesUpRight1, EyesUpRight2, MouthFlatOnly1),               // 23=看右上

            // 行 4：看向续 + 其他 6 帧
            BuildFrame(EyesDownLeft1, EyesDownLeft2, MouthFlatOnly1),           // 24=看左下
            BuildFrame(EyesDownRight1, EyesDownRight2, MouthFlatOnly1),           // 25=看右下
            BuildFrame(EyesOpen1, EyesOpen2, MouthFlat1),                       // 26=看前
            BuildFrame(EyesGlare1, EyesGlare2, MouthFrown1),                   // 27=生气瞪眼
            BuildFrame(EyesSmug1, EyesSmug2, MouthSmug1),                       // 28=得意
            BuildFrame(EyesOpen1, EyesOpen2, MouthFlat1),                       // 29=预留
        };
        PixelArtRenderer.RenderSpriteSheetGrid(sheetPath, frames, Palette, Scale, Cols, Rows);
    }
}
