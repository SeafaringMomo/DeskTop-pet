using System.Speech.Recognition;
using System.Globalization;

namespace DesktopGent.Services.Core;

/// <summary>
/// 麦克风语音识别服务，基于 System.Speech.Recognition（Windows 内置）。
/// 提供"按一下开始识别、再按一下停止并返回文本"的简易交互模式。
/// 优先尝试中文（zh-CN）识别引擎；若系统未安装中文语音识别语言包，回退到默认引擎。
/// </summary>
public sealed class SpeechRecognitionService : IDisposable
{
    private SpeechRecognitionEngine? _engine;
    private DictationGrammar? _grammar;
    private bool _disposed;
    private bool _running;

    /// <summary>是否正在识别</summary>
    public bool IsRunning => _running;

    /// <summary>识别到文本时触发（在识别引擎后台线程触发，调用方需自行 Dispatcher.Invoke）。</summary>
    public event Action<string>? ResultReceived;

    /// <summary>识别出错时触发（如未安装语音识别语言包）。</summary>
    public event Action<string>? ErrorOccurred;

    /// <summary>
    /// 开始识别。若已在识别则忽略。返回 true 表示成功启动。
    /// 失败原因：未安装语音识别功能、无麦克风权限等。
    /// </summary>
    public bool Start()
    {
        if (_disposed) return false;
        if (_running) return true;

        try
        {
            if (_engine == null)
            {
                _engine = CreateEngine();
                _grammar = new DictationGrammar();
                _engine.LoadGrammar(_grammar);
                _engine.SetInputToDefaultAudioDevice();
                _engine.SpeechRecognized += OnSpeechRecognized;
                _engine.RecognizeCompleted += OnRecognizeCompleted;
            }
            _engine.RecognizeAsync(RecognizeMode.Multiple);
            _running = true;
            Logger.Info("Speech", "麦克风语音识别已启动");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("Speech", "启动语音识别失败", ex);
            ErrorOccurred?.Invoke("启动语音识别失败：" + ex.Message
                + "\n请确认 Windows 已启用语音识别功能并安装中文语言包。");
            _running = false;
            return false;
        }
    }

    /// <summary>停止识别。返回当前已识别的累积文本（若有）。</summary>
    public void Stop()
    {
        if (!_running || _engine == null) return;
        try
        {
            _engine.RecognizeAsyncStop();
        }
        catch { }
        _running = false;
        Logger.Info("Speech", "麦克风语音识别已停止");
    }

    private void OnSpeechRecognized(object? sender, SpeechRecognizedEventArgs e)
    {
        var text = e.Result?.Text?.Trim();
        if (!string.IsNullOrEmpty(text))
        {
            Logger.Info("Speech", $"识别到文本：{text}");
            ResultReceived?.Invoke(text);
        }
    }

    private void OnRecognizeCompleted(object? sender, RecognizeCompletedEventArgs e)
    {
        _running = false;
        if (e.Error != null)
        {
            Logger.Error("Speech", "识别完成但有错误", e.Error);
        }
    }

    /// <summary>创建识别引擎：优先 zh-CN，失败则用默认。</summary>
    private static SpeechRecognitionEngine CreateEngine()
    {
        try
        {
            var zh = new CultureInfo("zh-CN");
            var installed = SpeechRecognitionEngine.InstalledRecognizers();
            var match = installed.FirstOrDefault(r => r.Culture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase));
            if (match != null) return new SpeechRecognitionEngine(match);
            // 找不到中文识别器，尝试用 zh-CN 构造（可能抛异常）
            try { return new SpeechRecognitionEngine(zh); }
            catch { /* 回退到默认 */ }
        }
        catch { }
        return new SpeechRecognitionEngine(); // 系统默认（通常是英文）
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _engine?.Dispose(); } catch { }
        _engine = null;
        _grammar = null;
    }
}
