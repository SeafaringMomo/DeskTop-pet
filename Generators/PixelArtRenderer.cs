using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DesktopPetCSharp.Generators;

/// <summary>
/// 像素画渲染器：把字符网格 + 调色板渲染成 PNG 精灵图。
/// 供各角色生成器共用，避免重复的 PNG 写入代码。
/// </summary>
public static class PixelArtRenderer
{
    /// <summary>
    /// 生成精灵图 PNG（多帧横向排列）+ 单帧 icon.png。
    /// </summary>
    /// <param name="sheetPath">精灵图输出路径</param>
    /// <param name="frames">各帧的字符网格（每帧是 string[]，每行长度 = 帧宽度）</param>
    /// <param name="palette">字符 → 颜色映射，'.' 表示透明</param>
    /// <param name="scale">每个像素放大倍数</param>
    public static void RenderSpriteSheet(string sheetPath, string[][] frames, Dictionary<char, Color> palette, int scale)
    {
        var dir = Path.GetDirectoryName(sheetPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        int framePx = frames[0].Length > 0 ? frames[0][0].Length : 0;
        int frameRows = frames[0].Length;

        // 精灵图
        if (!File.Exists(sheetPath))
        {
            var sheetWidth = framePx * frames.Length * scale;
            var sheetHeight = frameRows * scale;
            var bmp = new WriteableBitmap(sheetWidth, sheetHeight, 96, 96, PixelFormats.Bgra32, null);
            var pixels = new byte[sheetWidth * sheetHeight * 4];

            for (int f = 0; f < frames.Length; f++)
            {
                FillPixels(pixels, frames[f], palette, scale, framePx, frameRows,
                            offsetX: f * framePx * scale, sheetWidth: sheetWidth);
            }

            bmp.WritePixels(new System.Windows.Int32Rect(0, 0, sheetWidth, sheetHeight), pixels, sheetWidth * 4, 0);
            using var fs = File.Create(sheetPath);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bmp));
            encoder.Save(fs);
        }

        // icon.png（第 0 帧）
        var iconPath = Path.Combine(dir ?? "", "icon.png");
        if (!File.Exists(iconPath))
        {
            var iconSize = framePx * scale;
            // icon 取正方形：以帧宽度为边长，高度不足则补透明
            int iconH = frameRows * scale;
            var iconBmp = new WriteableBitmap(iconSize, iconSize, 96, 96, PixelFormats.Bgra32, null);
            var pixels = new byte[iconSize * iconSize * 4];
            FillPixels(pixels, frames[0], palette, scale, framePx, frameRows, 0, iconSize);
            iconBmp.WritePixels(new System.Windows.Int32Rect(0, 0, iconSize, iconSize), pixels, iconSize * 4, 0);
            using var fs = File.Create(iconPath);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(iconBmp));
            encoder.Save(fs);
        }
    }

    /// <summary>把一帧的像素数据填入字节数组。</summary>
    private static void FillPixels(byte[] pixels, string[] frame, Dictionary<char, Color> palette,
                                    int scale, int framePx, int frameRows, int offsetX, int sheetWidth)
    {
        for (int y = 0; y < frameRows; y++)
        {
            var row = y < frame.Length ? frame[y] : new string('.', framePx);
            for (int x = 0; x < framePx; x++)
            {
                char c = x < row.Length ? row[x] : '.';
                var color = palette.TryGetValue(c, out var col) ? col : palette['.'];
                for (int dy = 0; dy < scale; dy++)
                    for (int dx = 0; dx < scale; dx++)
                    {
                        int px = offsetX + x * scale + dx;
                        int py = y * scale + dy;
                        int offset = (py * sheetWidth + px) * 4;
                        pixels[offset] = color.B;
                        pixels[offset + 1] = color.G;
                        pixels[offset + 2] = color.R;
                        pixels[offset + 3] = color.A;
                    }
            }
        }
    }
}
