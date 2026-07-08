using System.IO;
using NAudio.Wave;

namespace DesktopPetCSharp.Generators;

/// <summary>
/// 程序化生成默认背景音乐（WAV 格式）。
/// 用于没有现成 BGM 文件的角色（如索隆），生成一段简短可循环的旋律作为占位。
/// 用户可自行替换为真实音乐文件。
/// </summary>
public static class BgmGenerator
{
    /// <summary>
    /// 生成一段简单的循环旋律 WAV 文件。若已存在则跳过。
    /// 旋律：用正弦波合成的简短音阶序列，带淡入淡出避免爆音。
    /// </summary>
    public static void EnsureGenerated(string path)
    {
        if (File.Exists(path)) return;
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        const int sampleRate = 22050;
        const int channels = 1;
        // 音阶频率（Hz），一段简短的武士风小调旋律
        var notes = new[]
        {
            (freq: 330.0, dur: 0.3),   // E4
            (freq: 392.0, dur: 0.3),   // G4
            (freq: 440.0, dur: 0.3),   // A4
            (freq: 494.0, dur: 0.3),   // B4
            (freq: 440.0, dur: 0.3),   // A4
            (freq: 392.0, dur: 0.3),   // G4
            (freq: 330.0, dur: 0.6),   // E4（长音）
            (freq: 0.0,   dur: 0.2),   // 静音
            (freq: 294.0, dur: 0.3),   // D4
            (freq: 330.0, dur: 0.3),   // E4
            (freq: 392.0, dur: 0.6),   // G4（长音）
            (freq: 0.0,   dur: 0.4),   // 静音
        };

        var format = new WaveFormat(sampleRate, channels);
        using var writer = new WaveFileWriter(path, format);

        for (int loop = 0; loop < 3; loop++) // 重复 3 次凑够长度
        {
            foreach (var (freq, dur) in notes)
            {
                int samples = (int)(sampleRate * dur);
                var buffer = new short[samples];
                for (int i = 0; i < samples; i++)
                {
                    double t = (double)i / sampleRate;
                    double sample = 0;
                    if (freq > 0)
                    {
                        // 正弦波 + 简单的谐波，模拟器乐音色
                        sample = 0.4 * Math.Sin(2 * Math.PI * freq * t)
                               + 0.15 * Math.Sin(2 * Math.PI * freq * 2 * t);
                        // 淡入淡出（前 50ms 淡入，后 80ms 淡出），避免爆音
                        double fadeSamples = sampleRate * 0.05;
                        double fadeOutSamples = sampleRate * 0.08;
                        if (i < fadeSamples)
                            sample *= i / fadeSamples;
                        else if (i > samples - fadeOutSamples)
                            sample *= (samples - i) / fadeOutSamples;
                    }
                    buffer[i] = (short)(sample * 32767 * 0.6); // 降音量避免削波
                }
                // short[] → byte[]
                var bytes = new byte[samples * 2];
                for (int i = 0; i < samples; i++)
                {
                    bytes[i * 2] = (byte)(buffer[i] & 0xFF);
                    bytes[i * 2 + 1] = (byte)((buffer[i] >> 8) & 0xFF);
                }
                writer.Write(bytes, 0, bytes.Length);
            }
        }
    }
}
