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
    /// 适用于单行精灵图（cols=帧数, rows=1）。
    /// </summary>
    public static void RenderSpriteSheet(string sheetPath, string[][] frames, Dictionary<char, Color> palette, int scale)
    {
        RenderSpriteSheetGrid(sheetPath, frames, palette, scale, cols: frames.Length, rows: 1);
    }

    /// <summary>
    /// 生成精灵图 PNG（按 cols × rows 网格排列）+ 单帧 icon.png。
    /// 帧顺序按行优先：frames[0] 在左上，frames[cols-1] 在第一行末尾，frames[cols] 在第二行开头。
    /// 若帧数不足 cols×rows，剩余位置透明。
    /// </summary>
    public static void RenderSpriteSheetGrid(string sheetPath, string[][] frames, Dictionary<char, Color> palette,
                                              int scale, int cols, int rows)
    {
        var dir = Path.GetDirectoryName(sheetPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        int framePx = frames[0].Length > 0 ? frames[0][0].Length : 0;
        int frameRows = frames[0].Length;
        int totalSlots = cols * rows;

        // 精灵图（cols × rows 网格）
        if (!File.Exists(sheetPath))
        {
            var sheetWidth = framePx * cols * scale;
            var sheetHeight = frameRows * rows * scale;
            var bmp = new WriteableBitmap(sheetWidth, sheetHeight, 96, 96, PixelFormats.Bgra32, null);
            var pixels = new byte[sheetWidth * sheetHeight * 4];

            for (int i = 0; i < totalSlots; i++)
            {
                if (i >= frames.Length) break;
                int col = i % cols;
                int row = i / cols;
                int offsetX = col * framePx * scale;
                int offsetY = row * frameRows * scale;
                FillPixels(pixels, frames[i], palette, scale, framePx, frameRows,
                            offsetX, offsetY, sheetWidth);
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
            int iconH = frameRows * scale;
            var iconBmp = new WriteableBitmap(iconSize, iconSize, 96, 96, PixelFormats.Bgra32, null);
            var pixels = new byte[iconSize * iconSize * 4];
            FillPixels(pixels, frames[0], palette, scale, framePx, frameRows, 0, 0, iconSize);
            iconBmp.WritePixels(new System.Windows.Int32Rect(0, 0, iconSize, iconSize), pixels, iconSize * 4, 0);
            using var fs = File.Create(iconPath);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(iconBmp));
            encoder.Save(fs);
        }
    }

    /// <summary>把一帧的像素数据填入字节数组。</summary>
    private static void FillPixels(byte[] pixels, string[] frame, Dictionary<char, Color> palette,
                                    int scale, int framePx, int frameRows, int offsetX, int offsetY, int sheetWidth)
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
                        int py = offsetY + y * scale + dy;
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
