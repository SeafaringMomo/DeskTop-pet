using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using DesktopPetCSharp.Models;
using DesktopPetCSharp.Services;

namespace DesktopPetCSharp.Windows;

/// <summary>
/// 对话历史窗口：列出当前用户的所有历史会话，点击查看消息内容，
/// 可"加载到聊天窗"恢复某次会话，或删除选中会话。
/// 配色跟随角色主题。
/// </summary>
public partial class ChatHistoryWindow : Window
{
    /// <summary>用户选定的会话（点击"加载到聊天窗"后读取），null 表示未选。</summary>
    public long? SelectedSessionId { get; private set; }

    private readonly ObservableCollection<ChatSession> _sessions = new();

    public ChatHistoryWindow()
    {
        InitializeComponent();
        SessionList.ItemsSource = _sessions;
        LoadSessions();
    }

    /// <summary>应用角色配色主题。</summary>
    public void ApplyTheme(string role)
    {
        var theme = ConfigService.Load().GetTheme(role);
        RootBorder.BorderBrush = new SolidColorBrush(theme.PrimaryColor);
        RootBorder.Background = new SolidColorBrush(WithAlpha(theme.BackgroundColor, 0xF2));

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
        CloseButton.Foreground = darkText;
        CloseButton.BorderBrush = darkText;

        LoadButton.Background = new SolidColorBrush(theme.PrimaryColor);
        LoadButton.Foreground = darkText;
        LoadButton.BorderBrush = darkText;

        DeleteButton.Background = new SolidColorBrush(theme.AccentColor);
        DeleteButton.Foreground = new SolidColorBrush(theme.TextColor);

        SessionList.Foreground = new SolidColorBrush(theme.TextColor);
        MessageView.Foreground = new SolidColorBrush(theme.TextColor);
    }

    private static Color WithAlpha(Color c, double alpha)
        => Color.FromArgb((byte)(alpha * 255), c.R, c.G, c.B);

    private static Color Darken(Color c, double factor)
        => Color.FromRgb((byte)(c.R * factor), (byte)(c.G * factor), (byte)(c.B * factor));

    private void LoadSessions()
    {
        _sessions.Clear();
        if (App.ChatHistory == null) return;
        foreach (var s in App.ChatHistory.ListSessions())
            _sessions.Add(s);
    }

    private void SessionList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (SessionList.SelectedItem is not ChatSession s) { MessageView.Clear(); return; }
        LoadMessages(s.Id);
    }

    private void LoadMessages(long sessionId)
    {
        MessageView.Clear();
        if (App.ChatHistory == null) return;
        foreach (var m in App.ChatHistory.GetMessages(sessionId))
        {
            var who = m.Role switch
            {
                "user" => "你",
                "assistant" => s_RoleFromSession(),
                "tool" => "[工具]",
                _ => m.Role
            };
            MessageView.AppendText($"{who}：{m.Content}{Environment.NewLine}");
        }
        MessageView.ScrollToEnd();
    }

    private string s_RoleFromSession()
    {
        if (SessionList.SelectedItem is ChatSession s) return s.Role;
        return "AI";
    }

    private void LoadButton_Click(object sender, RoutedEventArgs e)
    {
        if (SessionList.SelectedItem is not ChatSession s)
        {
            MessageBox.Show(this, "请先选择一个会话。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        SelectedSessionId = s.Id;
        Logger.Info("Chat", $"加载历史会话 #{s.Id} 到聊天窗");
        DialogResult = true;
        Close();
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (SessionList.SelectedItem is not ChatSession s) return;
        var ok = MessageBox.Show(this, $"确定删除会话「{s.Title}」？", "确认", MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (ok != MessageBoxResult.OK) return;
        App.ChatHistory?.DeleteSession(s.Id);
        LoadSessions();
        MessageView.Clear();
        Logger.Info("Chat", $"删除历史会话 #{s.Id}");
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    // ===== 标题栏拖拽 =====
    private bool _isDragging;
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
        _isDragging = false;
        ReleaseMouseCapture();
        base.OnMouseLeftButtonUp(e);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
    }
}
