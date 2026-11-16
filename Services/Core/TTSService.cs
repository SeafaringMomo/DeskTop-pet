using System.Speech.Synthesis;

namespace DesktopGent.Services.Core;

/// <summary>
/// 文本转语音服务，对应原 Python 项目 txtToVoice_1.py 中的 TxtToVoice1（基于 pyttsx3）。
/// 使用 System.Speech.Synthesis（Windows 内置），无需外部依赖。
/// 原代码 rate=200, volume=1.0。System.Speech Rate 范围 -10~10，200 大约对应 5。
/// </summary>
public sealed class TTSService : IDisposable
{
    private readonly SpeechSynthesizer _synth;
    private bool _disposed;

    public TTSService()
    {
        _synth = new SpeechSynthesizer();
        _synth.Rate = 5;          // 对应 pyttsx3 rate=200 的较快语速
        _synth.Volume = 100;      // 对应 volume=1.0
        SelectChineseVoice();
    }

    /// <summary>尝试选择中文语音，找不到则使用系统默认。</summary>
    private void SelectChineseVoice()
    {
        foreach (var info in _synth.GetInstalledVoices())
        {
            var ci = info.VoiceInfo.Culture;
            if (ci != null && ci.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
            {
                _synth.SelectVoice(info.VoiceInfo.Name);
                return;
            }
        }
    }

    /// <summary>同步朗读文本。调用方应放在后台线程。</summary>
    public void Speak(string content)
    {
        if (App.IsMuted) return;            // 全局静音时不朗读
        if (string.IsNullOrWhiteSpace(content) || _disposed) return;
        try
        {
            _synth.Speak(content);
        }
        catch (ObjectDisposedException) { }
    }

    /// <summary>异步朗读文本。</summary>
    public Task SpeakAsync(string content) => Task.Run(() => Speak(content));

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _synth.Dispose();
    }
}
