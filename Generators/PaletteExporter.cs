using System.IO;
using System.Windows.Media;
using DesktopPetCSharp.Services;

namespace DesktopPetCSharp.Generators;

/// <summary>
/// 调色板导出器（方向 6）：把角色的字符调色板导出为外部图像编辑工具可识别的格式。
///
/// 支持的输出格式：
/// - .gpl（GIMP Palette）：GIMP / Aseprite / Photoshop 均可导入，是最通用的调色板交换格式
/// - .hex（CSS/HEX 列表）：纯文本十六进制颜色列表，便于文档参考或 CSS 引用
///
/// 用途：用户在 Aseprite 中导入 .gpl 后，可以按项目调色板绘制像素画，
/// 保证生成的 sheet.png 与代码生成的版本色彩一致，无缝替换。
///
/// 导出时机：App.OnStartup 生成精灵图后调用 ExportForRole，已存在则跳过。
/// </summary>
public static class PaletteExporter
{
    /// <summary>导出角色调色板到指定目录（roleDir）。
    /// 输出 palette.gpl + palette.hex，已存在则跳过（便于用户手动修改）。</summary>
    public static void ExportForRole(string roleDir, string roleName, Dictionary<char, Color> palette)
    {
        if (!Directory.Exists(roleDir)) Directory.CreateDirectory(roleDir);

        var gplPath = Path.Combine(roleDir, "palette.gpl");
        var hexPath = Path.Combine(roleDir, "palette.hex");

        if (!File.Exists(gplPath))
        {
            ExportGpl(gplPath, roleName, palette);
            Logger.Info("Generator", $"导出调色板 GPL: {gplPath}（{palette.Count} 色）");
        }
        if (!File.Exists(hexPath))
        {
            ExportHex(hexPath, palette);
            Logger.Info("Generator", $"导出调色板 HEX: {hexPath}");
        }
    }

    /// <summary>导出为 GIMP Palette (.gpl) 格式。
    /// 格式规范：
    ///   GIMP Palette
    ///   Name: &lt;palette name&gt;
    ///   Columns: &lt;N&gt;
    ///   # &lt;comment&gt;
    ///   R G B  &lt;color name&gt;
    /// </summary>
    public static void ExportGpl(string path, string paletteName, Dictionary<char, Color> palette)
    {
        using var sw = new StreamWriter(path);
        sw.WriteLine("GIMP Palette");
        sw.WriteLine($"Name: {paletteName}");
        sw.WriteLine("Columns: 4");
        sw.WriteLine("# 由 DesktopPetCSharp PaletteExporter 自动生成");
        sw.WriteLine("# 字符调色板：每行 R G B + 字符标记（便于在 Aseprite 中按名称查找）");
        foreach (var kv in palette)
        {
            var c = kv.Value;
            // 透明色也导出（A=0），但标注为 "transparent"
            var name = c.A == 0 ? "transparent" : $"char_{kv.Key}";
            sw.WriteLine($"{c.R,3} {c.G,3} {c.B,3}\t{name}");
        }
    }

    /// <summary>导出为 HEX 列表 (.hex) 格式，每行一个 #RRGGBB。</summary>
    public static void ExportHex(string path, Dictionary<char, Color> palette)
    {
        using var sw = new StreamWriter(path);
        sw.WriteLine($"# {palette.Count} colors");
        foreach (var kv in palette)
        {
            var c = kv.Value;
            sw.WriteLine($"#{c.R:X2}{c.G:X2}{c.B:X2}\t# char '{kv.Key}'");
        }
    }
}
