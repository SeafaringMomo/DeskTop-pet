using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using DesktopPetCSharp.Models;
using DesktopPetCSharp.Services;

namespace DesktopPetCSharp.Windows;

/// <summary>
/// 待办窗口，移植自 todohelper20260629_csharp/MainWindow。
/// 配色随角色切换（路飞=草帽黄/红/棕；索隆=绿/棕等），由 <see cref="ApplyTheme"/> 应用。
/// 支持：输入回车/点 Add 添加；双击切换完成状态；选中后删除；一键清除已完成；标题栏拖动窗口。
/// 数据通过 <see cref="App.Todos"/> 单例仓储读写，与 LocalTodoMcpServer 共享同一份数据。
/// </summary>
public partial class TodoWindow : Window
{
    private readonly ObservableCollection<TodoItem> _items = new();
    private readonly SpeechRecognitionService _speech = new();
    private string _roleName = "";

    public TodoWindow()
    {
        InitializeComponent();
        TaskList.ItemsSource = _items;
        // 启动时应用当前角色配色
        _roleName = ConfigService.Load().Role;
        ApplyTheme(_roleName);
        // 语音识别回调：识别到文本时追加到输入框
        _speech.ResultReceived += text => Dispatcher.Invoke(() =>
        {
            if (string.IsNullOrEmpty(TaskInput.Text)) TaskInput.Text = text;
            else TaskInput.Text += text;
            TaskInput.CaretIndex = TaskInput.Text.Length;
            TaskInput.Focus();
        });
        _speech.ErrorOccurred += msg => Dispatcher.Invoke(() =>
        {
            Logger.Warn("Todo", $"语音识别错误：{msg}");
            ResetMicButton();
        });
        LoadTasks();
        Logger.Info("Todo", "待办窗口打开");
    }

    /// <summary>
    /// 应用角色配色主题。由 PetWindow.Switch() 在切换角色时调用，
    /// 实现"切换角色即切换配色"——配色与角色解耦，统一从 config 读取。
    /// </summary>
    public void ApplyTheme(string role)
    {
        _roleName = role;
        var theme = ConfigService.Load().GetTheme(role);

        // 外边框
        RootBorder.BorderBrush = new SolidColorBrush(theme.PrimaryColor);
        RootBorder.Background = new SolidColorBrush(WithAlpha(theme.BackgroundColor, 0xF2));

        // 标题栏渐变
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
        TitleText.Text = $"📋 {role}的待办本";
        CloseButton.Foreground = darkText;
        CloseButton.BorderBrush = darkText;

        // 输入区
        var surface = new SolidColorBrush(WithAlpha(theme.SurfaceColor, 0xF2));
        TaskInput.Background = surface;
        TaskInput.Foreground = new SolidColorBrush(theme.TextColor);
        TaskInput.BorderBrush = new SolidColorBrush(theme.PrimaryColor);
        TaskInput.CaretBrush = new SolidColorBrush(theme.PrimaryColor);

        // 麦克风按钮（默认态）
        MicButton.Background = surface;
        MicButton.Foreground = new SolidColorBrush(theme.PrimaryColor);
        MicButton.BorderBrush = new SolidColorBrush(theme.PrimaryColor);

        AddButton.Background = new SolidColorBrush(theme.PrimaryColor);
        AddButton.Foreground = darkText;
        AddButton.BorderBrush = darkText;

        // 列表
        TaskList.Foreground = new SolidColorBrush(theme.TextColor);

        // 底部按钮
        DeleteButton.Background = new SolidColorBrush(theme.AccentColor);
        DeleteButton.Foreground = new SolidColorBrush(theme.TextColor);
        DeleteButton.BorderBrush = darkText;
        ClearDoneButton.Background = new SolidColorBrush(theme.SurfaceColor);
        ClearDoneButton.Foreground = new SolidColorBrush(theme.TextColor);
        ClearDoneButton.BorderBrush = new SolidColorBrush(theme.PrimaryColor);

        Logger.Info("Todo", $"应用配色主题：角色={role}，主色={theme.Primary}，强调={theme.Accent}");
    }

    /// <summary>给颜色叠加 alpha 通道（0~255），保留 RGB。</summary>
    private static Color WithAlpha(Color c, double alpha)
        => Color.FromArgb((byte)(alpha * 255), c.R, c.G, c.B);

    /// <summary>把颜色按系数变暗（0~1，越小越暗），用于在浅色背景上保持文字对比。</summary>
    private static Color Darken(Color c, double factor)
        => Color.FromRgb((byte)(c.R * factor), (byte)(c.G * factor), (byte)(c.B * factor));

    /// <summary>外部数据变更后刷新窗口（例如 AI 通过 MCP 添加了待办）。</summary>
    public void RefreshFromRepository() => LoadTasks();

    private void LoadTasks()
    {
        _items.Clear();
        foreach (var item in App.Todos.GetAll())
            _items.Add(item);
    }

    private void AddButton_Click(object sender, RoutedEventArgs e) => AddTask();

    private void TaskInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            AddTask();
            e.Handled = true;
        }
    }

    private void AddTask()
    {
        var text = TaskInput.Text.Trim();
        if (string.IsNullOrEmpty(text)) return;
        var item = new TodoItem
        {
            Content = text,
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now,
            IsDone = false
        };
        App.Todos.Insert(item);
        _items.Add(item);
        TaskInput.Clear();
        TaskInput.Focus();
        Logger.Info("Todo", $"添加待办 #{item.Id}：{text}");
    }

    private void TaskList_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (TaskList.SelectedItem is TodoItem item)
        {
            item.IsDone = !item.IsDone;
            item.UpdatedAt = DateTime.Now;
            App.Todos.Update(item);
            // ObservableCollection 项属性变化需手动刷新显示
            TaskList.Items.Refresh();
            Logger.Info("Todo", $"切换待办 #{item.Id} 完成状态 -> {(item.IsDone ? "完成" : "未完成")}");

            // P9: 完成待办联动好感度 +2（仅切换到"完成"时奖励，带上饱食度加成）
            if (item.IsDone)
            {
                try { AffinityService.Instance.AddAffinity(_roleName, ConfigService.LoadPetManagement().Rewards.TodoComplete, applySatietyBonus: true); }
                catch (Exception ex) { Logger.Warn("Todo", $"完成待办好感度加成失败：{ex.Message}"); }
            }
        }
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (TaskList.SelectedItem is TodoItem item)
        {
            App.Todos.Delete(item.Id);
            _items.Remove(item);
            Logger.Info("Todo", $"删除待办 #{item.Id}：{item.Content}");
        }
    }

    private void ClearDoneButton_Click(object sender, RoutedEventArgs e)
    {
        App.Todos.ClearDone();
        LoadTasks();
        Logger.Info("Todo", "清除已完成待办");
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Logger.Info("Todo", "待办窗口隐藏");
        Hide();
    }

    // ===== 麦克风语音输入 =====

    /// <summary>麦克风按钮：点击切换开始/停止识别。识别中按钮高亮（强调色背景）。</summary>
    private void MicButton_Click(object sender, RoutedEventArgs e)
    {
        if (_speech.IsRunning)
        {
            _speech.Stop();
            ResetMicButton();
            Logger.Info("Todo", "停止语音输入");
        }
        else
        {
            var ok = _speech.Start();
            if (ok)
            {
                var theme = ConfigService.Load().GetTheme(_roleName);
                MicButton.Background = new SolidColorBrush(theme.AccentColor);
                MicButton.Foreground = new SolidColorBrush(Darken(theme.BackgroundColor, 0.85));
                MicButton.BorderBrush = new SolidColorBrush(theme.PrimaryColor);
                MicButton.Content = "🔴";
                Logger.Info("Todo", "开始语音输入");
            }
            else
            {
                Logger.Warn("Todo", "语音识别不可用，请检查 Windows 语音识别功能与中文语言包");
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
        if (_isDragging)
        {
            Logger.Info("Todo", $"待办窗口拖动结束，位置=({Left:F0},{Top:F0})");
        }
        _isDragging = false;
        ReleaseMouseCapture();
        base.OnMouseLeftButtonUp(e);
    }

    private bool _forceClose;

    /// <summary>强制关闭窗口（用于应用退出），跳过"隐藏代替关闭"逻辑。</summary>
    public void ForceClose()
    {
        _forceClose = true;
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // 隐藏而非关闭，便于从宠物菜单反复打开；应用退出时调用 ForceClose 跳过此逻辑
        if (!_forceClose)
        {
            e.Cancel = true;
            Hide();
        }
        else
        {
            _speech.Dispose();
        }
        base.OnClosing(e);
    }
}
