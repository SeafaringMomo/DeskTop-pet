using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using DesktopGent.Models;
using DesktopGent.Services.AI;
using DesktopGent.Services.Core;

namespace DesktopGent.Windows;

/// <summary>
/// 聊天窗口，对应原 Python 项目 talkwindow.py 的 TalkWindow 类。
/// 配色随角色切换（默认角色=草帽黄/红/棕；索隆=绿/棕等），由 <see cref="ApplyTheme"/> 应用。
/// 输入回车后通过 <see cref="ChatOrchestrator"/> 调用当前默认 AI 提供方，
/// 若提供方支持工具调用，会自动经由 MCP 服务器管理待办等外部能力。
/// 标题栏右侧"⚙ 模型"按钮可打开模型设置窗口切换 AI 提供方。
/// </summary>
public partial class TalkWindow : Window
{
    private readonly ChatOrchestrator _chat = new();
    private readonly TTSService _tts = new();
    private readonly SpeechRecognitionService _speech = new();
    private string _roleName;
    private bool _isDragging;

    /// <summary>当前会话 ID（对话记录持久化用）。null 表示尚未创建会话（首次发消息时创建）。</summary>
    private long? _sessionId;

    /// <summary>当前进行中的流式请求取消源。再次发送或关窗时取消。</summary>
    private CancellationTokenSource? _streamCts;

    /// <summary>流式输出中：当前行是否已有角色前缀（避免每个 chunk 都加）。</summary>
    private bool _streamRowOpen;

    public TalkWindow()
    {
        InitializeComponent();
        _roleName = ConfigService.Load().Role;
        // 启动时应用一次当前角色配色
        ApplyTheme(_roleName);
        // 语音识别回调：识别到文本时追加到输入框
        _speech.ResultReceived += text => Dispatcher.Invoke(() =>
        {
            if (string.IsNullOrEmpty(InputBox.Text)) InputBox.Text = text;
            else InputBox.Text += text;
            InputBox.CaretIndex = InputBox.Text.Length;
            InputBox.Focus();
        });
        _speech.ErrorOccurred += msg => Dispatcher.Invoke(() =>
        {
            AppendOutput($"[语音识别] {msg}");
            ResetMicButton();
        });
        InputBox.Focus();
        AppendOutput($"{_roleName}：嘿嘿，要和我聊点什么呀？输入后按回车发送～");
        AppendOutput($"（当前 AI 提供方：{GetCurrentProviderName()}）");
        Logger.Info("Talk", $"聊天窗口打开，主角={_roleName}，AI={GetCurrentProviderName()}");
    }

    /// <summary>
    /// 应用角色配色主题。由 GentWindow.Switch() 在切换角色时调用，
    /// 实现"切换角色即切换配色"——配色与角色解耦，统一从 config 读取。
    /// </summary>
    public void ApplyTheme(string role)
    {
        _roleName = role;
        var theme = ConfigService.Load().GetTheme(role);

        // 外边框：主色边框 + 背景色（带透明度）
        RootBorder.BorderBrush = new SolidColorBrush(theme.PrimaryColor);
        RootBorder.Background = new SolidColorBrush(WithAlpha(theme.BackgroundColor, 0xF2));

        // 标题栏：主色 → 强调色的水平渐变；文字用深背景色保证对比
        var grad = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 0)
        };
        grad.GradientStops.Add(new GradientStop(theme.PrimaryColor, 0));
        grad.GradientStops.Add(new GradientStop(theme.AccentColor, 1));
        TitleBar.Background = grad;

        var darkText = new SolidColorBrush(Darken(theme.BackgroundColor, 0.85));
        TitleText.Foreground = darkText;
        HistoryButton.Foreground = darkText;
        HistoryButton.BorderBrush = darkText;
        ModelSettingsButton.Foreground = darkText;
        ModelSettingsButton.BorderBrush = darkText;
        CloseButton.Foreground = darkText;
        CloseButton.BorderBrush = darkText;
        TitleText.Text = $"💬 {_roleName}的聊天";

        // 输出区背景（表面色 + 透明度）+ 文字色
        var surface = new SolidColorBrush(WithAlpha(theme.SurfaceColor, 0xF2));
        OutputBox.Background = surface;
        OutputBox.Foreground = new SolidColorBrush(theme.TextColor);
        OutputBox.CaretBrush = new SolidColorBrush(theme.PrimaryColor);

        // 输入框
        InputBox.Background = surface;
        InputBox.Foreground = new SolidColorBrush(theme.TextColor);
        InputBox.BorderBrush = new SolidColorBrush(theme.PrimaryColor);
        InputBox.CaretBrush = new SolidColorBrush(theme.PrimaryColor);

        // 麦克风按钮（默认态）
        MicButton.Background = surface;
        MicButton.Foreground = new SolidColorBrush(theme.PrimaryColor);
        MicButton.BorderBrush = new SolidColorBrush(theme.PrimaryColor);

        Logger.Info("Talk", $"应用配色主题：角色={role}，主色={theme.Primary}，强调={theme.Accent}");
    }

    /// <summary>给颜色叠加 alpha 通道（0~255），保留 RGB。</summary>
    private static Color WithAlpha(Color c, double alpha)
        => Color.FromArgb((byte)(alpha * 255), c.R, c.G, c.B);

    /// <summary>把颜色按系数变暗（0~1，越小越暗），用于在浅色背景上保持文字对比。</summary>
    private static Color Darken(Color c, double factor)
        => Color.FromRgb((byte)(c.R * factor), (byte)(c.G * factor), (byte)(c.B * factor));

    private static string GetCurrentProviderName()
        => Services.AI.AiProviderRegistry.Instance.Default?.Name ?? "(无)";

    private void InputBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            var text = InputBox.Text;
            if (string.IsNullOrWhiteSpace(text)) return;
            InputBox.Clear();
            AppendOutput($"你：{text}");
            Logger.Info("Talk", $"用户发送消息：{text}");
            // 持久化用户消息到对话记录
            PersistMessage("user", text);
            // P9: 聊天联动好感度 +1（带上饱食度加成）
            try { AffinityService.Instance.AddAffinity(_roleName, ConfigService.LoadGentManagement().Rewards.ChatPerMessage, applySatietyBonus: true); }
            catch (Exception ex) { Logger.Warn("Talk", $"聊天好感度加成失败：{ex.Message}"); }
            // 后台线程处理，避免阻塞 UI
            _ = Task.Run(() => ProcessAsync(text));
            e.Handled = true;
        }
    }

    /// <summary>
    /// 处理一条用户消息：优先流式（Python Agent），否则回退到一次性返回。
    /// 对应原 Python async_process() 方法。
    /// </summary>
    private async Task ProcessAsync(string text)
    {
        _streamCts?.Cancel();
        _streamCts = new CancellationTokenSource();
        var ct = _streamCts.Token;

        var provider = AiProviderRegistry.Instance.Default;
        if (provider is IStreamingAiProvider)
        {
            await ProcessStreamAsync(text, ct);
            return;
        }

        // 回退：旧的一次性路径（C# 内部工具循环）
        try
        {
            var reply = await _chat.TalkAsync(text, ct);
            Dispatcher.Invoke(() => AppendOutput($"{_roleName}：{reply}"));
            Logger.Info("Talk", $"AI 回复：{reply}");
            PersistMessage("assistant", reply);
            _tts.Speak(reply);
        }
        catch (OperationCanceledException)
        {
            Logger.Info("Talk", "请求已取消");
        }
        catch (Exception ex)
        {
            Dispatcher.Invoke(() => AppendOutput($"[出错] {ex.Message}"));
            Logger.Error("Talk", "处理消息失败", ex);
        }
    }

    /// <summary>
    /// 流式处理：边收边渲染，工具调用以状态行提示。
    /// </summary>
    private async Task ProcessStreamAsync(string text, CancellationToken ct)
    {
        var sessionId = EnsureSession();
        var final = new System.Text.StringBuilder();
        var errored = false;

        try
        {
            Dispatcher.Invoke(() => SetStatus("思考中…", visible: true));

            await foreach (var ev in _chat.TalkStreamAsync(text, sessionId.ToString(), ct))
            {
                switch (ev.Kind)
                {
                    case "chunk":
                        final.Append(ev.Text);
                        var chunk = ev.Text;
                        Dispatcher.Invoke(() => AppendChunk(chunk));
                        break;

                    case "tool_start":
                        Logger.Info("Agent", $"工具开始：{ev.ToolName}");
                        Dispatcher.Invoke(() => SetStatus($"🔧 {ev.ToolName}…", true));
                        break;

                    case "tool_finish":
                        Logger.Info("Agent", $"工具完成：{ev.ToolName}（is_error={ev.IsError}）");
                        Dispatcher.Invoke(() => SetStatus(
                            ev.IsError ? $"⚠ {ev.ToolName} 失败" : $"✅ {ev.ToolName} 完成", true));
                        break;

                    case "step_start":
                        Dispatcher.Invoke(() => SetStatus($"第 {ev.Step} 步…", true));
                        break;

                    case "error":
                        errored = true;
                        Logger.Error("Agent", $"Agent 返回错误：{ev.ErrorMessage}");
                        Dispatcher.Invoke(() => AppendOutput($"[出错] {ev.ErrorMessage}"));
                        break;

                    case "done":
                        if (final.Length == 0 && !string.IsNullOrEmpty(ev.FinalContent))
                            final.Append(ev.FinalContent);
                        break;
                }
            }

            var reply = final.ToString();
            Dispatcher.Invoke(() =>
            {
                CloseStreamRow();
                SetStatus("", visible: false);
            });

            if (!errored && !string.IsNullOrEmpty(reply))
            {
                Logger.Info("Talk", $"AI 流式回复完成，长度={reply.Length}");
                PersistMessage("assistant", reply);
                _tts.Speak(reply);
            }
        }
        catch (OperationCanceledException)
        {
            Logger.Info("Talk", "流式请求已取消");
            Dispatcher.Invoke(() => { CloseStreamRow(); SetStatus("", false); });
        }
        catch (Exception ex)
        {
            Logger.Error("Talk", "流式处理失败", ex);
            Dispatcher.Invoke(() =>
            {
                CloseStreamRow();
                SetStatus("", false);
                AppendOutput($"[出错] {ex.Message}");
            });
        }
    }

    /// <summary>确保当前会话存在，返回会话 ID。</summary>
    private long EnsureSession()
    {
        if (_sessionId != null) return _sessionId.Value;
        var repo = App.ChatHistory;
        if (repo == null) return 0;
        var session = repo.CreateSession(_roleName, GetCurrentProviderName());
        _sessionId = session.Id;
        return session.Id;
    }

    /// <summary>追加一段流式文本：首段带角色前缀，后续直接续写。</summary>
    private void AppendChunk(string chunk)
    {
        if (!_streamRowOpen)
        {
            OutputBox.AppendText($"{_roleName}：");
            _streamRowOpen = true;
        }
        OutputBox.AppendText(chunk);
        OutputBox.ScrollToEnd();
    }

    /// <summary>结束当前流式行（换行）。</summary>
    private void CloseStreamRow()
    {
        if (!_streamRowOpen) return;
        OutputBox.AppendText(Environment.NewLine);
        OutputBox.ScrollToEnd();
        _streamRowOpen = false;
    }

    /// <summary>更新状态行。</summary>
    private void SetStatus(string text, bool visible)
    {
        StatusText.Text = text;
        StatusText.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>把消息写入对话记录数据库（若 ChatHistory 不可用则忽略）。</summary>
    private void PersistMessage(string role, string content)
    {
        try
        {
            var repo = App.ChatHistory;
            if (repo == null) return;
            // 首次发消息时创建会话
            if (_sessionId == null || _sessionId.Value == 0)
            {
                var session = repo.CreateSession(_roleName, GetCurrentProviderName());
                _sessionId = session.Id;
            }
            repo.AddMessage(_sessionId.Value, role, content);
        }
        catch (Exception ex)
        {
            Logger.Error("Talk", "写入对话记录失败", ex);
        }
    }

    /// <summary>打开历史对话窗口。</summary>
    private void HistoryButton_Click(object sender, RoutedEventArgs e)
    {
        Logger.Info("Talk", "打开历史对话窗口");
        var win = new ChatHistoryWindow { Owner = this };
        win.ApplyTheme(_roleName);
        if (win.ShowDialog() == true && win.SelectedSessionId is long sid)
        {
            // 加载选中会话的消息到当前聊天窗
            LoadSession(sid);
        }
    }

    /// <summary>加载指定会话的消息到输出区，并切换当前会话 ID。</summary>
    private void LoadSession(long sessionId)
    {
        var repo = App.ChatHistory;
        if (repo == null) return;
        OutputBox.Clear();
        AppendOutput($"（已加载历史会话 #{sessionId}）");
        var messages = repo.GetMessages(sessionId);
        foreach (var m in messages)
        {
            var who = m.Role switch
            {
                "user" => "你",
                "assistant" => _roleName,
                "tool" => "[工具]",
                _ => m.Role
            };
            AppendOutput($"{who}：{m.Content}");
        }

        // 同步到编排器历史，使后续流式对话具备上下文
        // 工具消息不进入 C# 历史（Python 侧每轮会重新归一化，tool 消息缺 ID 时会被丢弃）
        _chat.LoadHistory(messages
            .Where(m => m.Role is "user" or "assistant")
            .Select(m => new ChatMessage { Role = m.Role, Content = m.Content }));

        _sessionId = sessionId;
        Logger.Info("Talk", $"加载历史会话 #{sessionId}，消息数={OutputBox.LineCount}");
    }

    private void AppendOutput(string line)
    {
        OutputBox.AppendText(line + Environment.NewLine);
        OutputBox.ScrollToEnd();
    }

    private void ModelSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        Logger.Info("Talk", "打开模型设置窗口");
        var win = new ModelSettingsWindow { Owner = this };
        win.ShowDialog();
        // 关闭后提示当前提供方
        AppendOutput($"（已切换 AI 提供方：{GetCurrentProviderName()}）");
    }

    // ===== 麦克风语音输入 =====

    /// <summary>麦克风按钮：点击切换开始/停止识别。识别中按钮高亮（强调色背景）。</summary>
    private void MicButton_Click(object sender, RoutedEventArgs e)
    {
        if (_speech.IsRunning)
        {
            _speech.Stop();
            ResetMicButton();
            Logger.Info("Talk", "停止语音输入");
        }
        else
        {
            var ok = _speech.Start();
            if (ok)
            {
                // 识别中：按钮变为强调色背景 + 深色文字
                var theme = ConfigService.Load().GetTheme(_roleName);
                MicButton.Background = new SolidColorBrush(theme.AccentColor);
                MicButton.Foreground = new SolidColorBrush(Darken(theme.BackgroundColor, 0.85));
                MicButton.BorderBrush = new SolidColorBrush(theme.PrimaryColor);
                MicButton.Content = "🔴";
                AppendOutput("[语音输入已开启，再次点击停止]");
                Logger.Info("Talk", "开始语音输入");
            }
            else
            {
                AppendOutput("[语音识别不可用，请检查 Windows 语音识别功能与中文语言包]");
            }
        }
    }

    /// <summary>恢复麦克风按钮为默认态。</summary>
    private void ResetMicButton()
    {
        var theme = ConfigService.Load().GetTheme(_roleName);
        MicButton.Background = new SolidColorBrush(WithAlpha(theme.SurfaceColor, 0xF2));
        MicButton.Foreground = new SolidColorBrush(theme.PrimaryColor);
        MicButton.BorderBrush = new SolidColorBrush(theme.PrimaryColor);
        MicButton.Content = "🎤";
    }

    // ===== 标题栏拖拽（增量法 + DPI 换算，避免高分屏错位）=====
    private Point _dragStartScreen;
    private double _dragStartLeft, _dragStartTop;

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _isDragging = true;
        _dragStartScreen = PointToScreen(e.GetPosition(this));
        _dragStartLeft = Left;
        _dragStartTop = Top;
        CaptureMouse();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_isDragging)
        {
            var now = PointToScreen(e.GetPosition(this));
            var ct = PresentationSource.FromVisual(this)?.CompositionTarget;
            double dpiX = ct?.TransformFromDevice.M11 ?? 1.0;
            double dpiY = ct?.TransformFromDevice.M22 ?? 1.0;
            Left = _dragStartLeft + (now.X - _dragStartScreen.X) * dpiX;
            Top = _dragStartTop + (now.Y - _dragStartScreen.Y) * dpiY;
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (_isDragging)
        {
            Logger.Info("Talk", $"聊天窗口拖动结束，位置=({Left:F0},{Top:F0})");
        }
        _isDragging = false;
        ReleaseMouseCapture();
        base.OnMouseLeftButtonUp(e);
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Logger.Info("Talk", "聊天窗口隐藏（关闭按钮）");
        Hide();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        try { _streamCts?.Cancel(); } catch { /* 已释放 */ }
        _streamCts?.Dispose();
        _streamCts = null;
        _speech.Dispose();
        _tts.Dispose();
        Logger.Info("Talk", "聊天窗口关闭");
        base.OnClosing(e);
    }
}
