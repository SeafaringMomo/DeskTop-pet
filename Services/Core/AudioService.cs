using System.Collections.Concurrent;
using NAudio.Wave;

namespace DesktopPetCSharp.Services;

/// <summary>
/// 音频服务，对应原 Python 项目中 pygame.mixer 的功能。
/// 区分背景音乐（循环播放）和音效（一次性播放），二者独立控制。
/// </summary>
public sealed class AudioService : IDisposable
{
    private WaveOutEvent? _bgmOut;
    private WaveStream? _bgmReader;
    private LoopStream? _bgmLoop;
    private float _bgmVolume = 0.5f;

    // 一次性音效：每个 WaveOutEvent 在播放停止后自动释放
    private readonly ConcurrentDictionary<WaveOutEvent, WaveStream> _sounds = new();

    /// <summary>背景音乐是否正在播放</summary>
    public bool IsBgmPlaying => _bgmOut?.PlaybackState == PlaybackState.Playing;

    /// <summary>是否有音效正在播放</summary>
    public bool IsSoundPlaying => !_sounds.IsEmpty;

    /// <summary>播放背景音乐（循环）。若已有音乐播放会先停止。支持 mp3/wav。</summary>
    public void PlayBgmLoop(string path)
    {
        StopBgm();
        if (App.IsMuted) return;            // 全局静音时不播放
        if (!System.IO.File.Exists(path))
        {
            Logger.Warn("Audio", $"背景音乐文件不存在: {path}");
            return;
        }

        _bgmReader = CreateReader(path);
        _bgmLoop = new LoopStream(_bgmReader) { EnableLooping = true };
        _bgmOut = new WaveOutEvent();
        _bgmOut.Volume = _bgmVolume;
        _bgmOut.Init(_bgmLoop);
        _bgmOut.Play();
    }

    /// <summary>停止背景音乐</summary>
    public void StopBgm()
    {
        if (_bgmOut != null)
        {
            _bgmOut.Stop();
            _bgmOut.Dispose();
            _bgmOut = null;
        }
        _bgmLoop?.Dispose();
        _bgmLoop = null;
        _bgmReader?.Dispose();
        _bgmReader = null;
    }

    /// <summary>设置背景音乐音量（0~1）</summary>
    public void SetBgmVolume(float volume)
    {
        _bgmVolume = Math.Clamp(volume, 0f, 1f);
        if (_bgmOut != null) _bgmOut.Volume = _bgmVolume;
    }

    /// <summary>播放一次性音效。volume 范围 0~1，默认 0.5。支持 mp3/wav。</summary>
    public void PlaySound(string path, float volume = 0.5f)
    {
        if (App.IsMuted) return;            // 全局静音时不播放
        if (!System.IO.File.Exists(path))
        {
            Logger.Warn("Audio", $"音效文件不存在: {path}");
            return;
        }

        var reader = CreateReader(path);
        var waveOut = new WaveOutEvent { Volume = Math.Clamp(volume, 0f, 1f) };
        waveOut.Init(reader);
        waveOut.PlaybackStopped += (_, _) =>
        {
            waveOut.Dispose();
            reader.Dispose();
            _sounds.TryRemove(waveOut, out _);
        };
        _sounds[waveOut] = reader;
        waveOut.Play();
    }

    /// <summary>根据扩展名创建对应的音频读取器（mp3 用 Mp3FileReader，wav 用 WaveFileReader）。</summary>
    private static WaveStream CreateReader(string path)
    {
        var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
        return ext == ".wav"
            ? new WaveFileReader(path)
            : new Mp3FileReader(path);
    }

    /// <summary>停止所有音效</summary>
    public void StopSounds()
    {
        foreach (var pair in _sounds)
        {
            try { pair.Key.Stop(); } catch { }
        }
    }

    public void Dispose()
    {
        StopBgm();
        StopSounds();
    }

    /// <summary>
    /// 简单的循环流包装：到达末尾时重置位置以实现循环播放。
    /// </summary>
    private sealed class LoopStream : WaveStream
    {
        private readonly WaveStream _source;
        public bool EnableLooping { get; set; }

        public LoopStream(WaveStream source) { _source = source; }
        public override WaveFormat WaveFormat => _source.WaveFormat;
        public override long Length => _source.Length;
        public override long Position
        {
            get => _source.Position;
            set => _source.Position = value;
        }
        public override int Read(byte[] buffer, int offset, int count)
        {
            int totalRead = 0;
            while (totalRead < count)
            {
                int read = _source.Read(buffer, offset + totalRead, count - totalRead);
                if (read == 0)
                {
                    if (EnableLooping)
                    {
                        _source.Position = 0;
                        if (_source.Position == 0 && totalRead == 0) break; // 空源，避免死循环
                    }
                    else break;
                }
                else totalRead += read;
            }
            return totalRead;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _source.Dispose();
            base.Dispose(disposing);
        }
    }
}
