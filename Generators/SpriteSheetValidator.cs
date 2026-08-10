using System.IO;
using System.Windows.Media.Imaging;
using DesktopPetCSharp.Services;

namespace DesktopPetCSharp.Generators;

/// <summary>
/// 精灵图验证器（方向 6）：检查用户放入或 Aseprite 导出的 sheet.png 是否符合项目规格。
///
/// 验证项：
/// 1. 文件存在
/// 2. 可解析为 PNG
/// 3. 尺寸能被 cols × rows 整除（避免切出残缺帧）
/// 4. 帧尺寸 ≥ 8×8（避免空帧或错误文件）
///
/// 用途：用户用 Aseprite 导出 sprite sheet 后，可用此工具快速验证格式是否正确，
/// 避免运行时 CroppedBitmap 切错导致"只显示左上角一小块"的问题。
/// </summary>
public static class SpriteSheetValidator
{
    /// <summary>验证结果。</summary>
    public class Result
    {
        public bool IsValid { get; set; }
        public string Message { get; set; } = "";
        public int SheetWidth { get; set; }
        public int SheetHeight { get; set; }
        public int FrameWidth { get; set; }
        public int FrameHeight { get; set; }
        public int TotalFrames { get; set; }
    }

    /// <summary>验证 sheet.png 是否符合 cols × rows 网格规格。</summary>
    public static Result Validate(string sheetPath, int cols, int rows)
    {
        var result = new Result();

        if (!File.Exists(sheetPath))
        {
            result.Message = $"文件不存在: {sheetPath}";
            return result;
        }

        try
        {
            using var fs = File.OpenRead(sheetPath);
            var decoder = PngBitmapDecoder.Create(fs, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnDemand);
            var frame = decoder.Frames[0];
            result.SheetWidth = frame.PixelWidth;
            result.SheetHeight = frame.PixelHeight;

            if (result.SheetWidth % cols != 0 || result.SheetHeight % rows != 0)
            {
                result.Message = $"尺寸 {result.SheetWidth}×{result.SheetHeight} 无法按 {cols}×{rows} 网格整除。" +
                                  $"请确保宽度是 {cols} 的倍数、高度是 {rows} 的倍数。";
                return result;
            }

            result.FrameWidth = result.SheetWidth / cols;
            result.FrameHeight = result.SheetHeight / rows;
            result.TotalFrames = cols * rows;

            if (result.FrameWidth < 8 || result.FrameHeight < 8)
            {
                result.Message = $"单帧尺寸 {result.FrameWidth}×{result.FrameHeight} 过小（最小 8×8）。";
                return result;
            }

            result.IsValid = true;
            result.Message = $"验证通过: {result.SheetWidth}×{result.SheetHeight} → {cols}×{rows} = {result.TotalFrames} 帧, " +
                              $"单帧 {result.FrameWidth}×{result.FrameHeight}";
            return result;
        }
        catch (Exception ex)
        {
            result.Message = $"解析 PNG 失败: {ex.Message}";
            return result;
        }
    }

    /// <summary>验证并记录日志，返回是否通过。</summary>
    public static bool ValidateAndLog(string sheetPath, int cols, int rows)
    {
        var r = Validate(sheetPath, cols, rows);
        if (r.IsValid)
            Logger.Info("Generator", $"精灵图验证通过: {sheetPath} — {r.Message}");
        else
            Logger.Warn("Generator", $"精灵图验证失败: {sheetPath} — {r.Message}");
        return r.IsValid;
    }
}
