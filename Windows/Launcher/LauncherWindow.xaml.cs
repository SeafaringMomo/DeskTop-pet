using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using DesktopGent.Models;
using DesktopGent.Services.Core;

namespace DesktopGent.Windows;

/// <summary>
/// 启动页窗口：应用启动后第一个显示的窗口。
/// 用户在此选择/新建用户，或配置模型后进入主界面。
///
/// 流程：
/// 1) 启动时显示用户列表
/// 2) 双击用户或选中后回车 → 登录 → 关闭启动页 → 打开 GentWindow
/// 3) 点「新建用户」→ 弹窗输入用户名 → 创建后自动登录
/// 4) 点「更换模型」→ 打开 ModelSettingsWindow（可在不进入主界面的情况下配置 AI）
/// 5) 点「退出」→ 关闭应用
///
/// 配色跟随默认角色主题（启动前尚无当前用户/角色，用 theme.yaml 扫描出的默认角色）。
/// </summary>
public partial class LauncherWindow : Window
{
    /// <summary>启动结果：是否选择了用户进入主界面（false 表示用户取消退出）。</summary>
    public bool Entered { get; private set; }

    private readonly ObservableCollection<UserItem> _users = new();

    public LauncherWindow()
    {
        InitializeComponent();
        UserList.ItemsSource = _users;
        LoadUsers();
        // 应用默认角色主题
        var defaultRole = ConfigService.Load().Role;
        ApplyTheme(defaultRole);
        Logger.Info("Launcher", $"启动页打开，默认角色={defaultRole}");
    }

    /// <summary>用户列表项视图模型</summary>
    public class UserItem
    {
        public string DisplayName { get; set; } = "";
        public string SubLine { get; set; } = "";
        public string Name { get; set; } = "";
    }

    private void LoadUsers()
    {
        _users.Clear();
        foreach (var u in UserService.Instance.ListUsers())
        {
            var lastLogin = u.LastLoginAt;
            var sub = $"最后登录：{FormatTime(lastLogin)}";
            _users.Add(new UserItem
            {
                DisplayName = u.Name,
                SubLine = sub,
                Name = u.Name
            });
        }
        if (_users.Count > 0)
        {
            UserList.SelectedIndex = 0;
        }
    }

    private static string FormatTime(string iso)
    {
        if (string.IsNullOrEmpty(iso)) return "无";
        try
        {
            var dt = DateTime.Parse(iso);
            return dt.ToString("MM-dd HH:mm");
        }
        catch { return iso; }
    }

    // ===== 主题 =====

    /// <summary>应用角色配色主题（启动页用默认角色主题）。</summary>
    private void ApplyTheme(string role)
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

        NewUserButton.Background = new SolidColorBrush(theme.PrimaryColor);
        NewUserButton.Foreground = darkText;
        NewUserButton.BorderBrush = darkText;

        QuitButton.Background = new SolidColorBrush(theme.AccentColor);
        QuitButton.Foreground = new SolidColorBrush(theme.TextColor);
        QuitButton.BorderBrush = darkText;

        UserList.Foreground = new SolidColorBrush(theme.TextColor);
    }

    private static Color WithAlpha(Color c, double alpha)
        => Color.FromArgb((byte)(alpha * 255), c.R, c.G, c.B);

    private static Color Darken(Color c, double factor)
        => Color.FromRgb((byte)(c.R * factor), (byte)(c.G * factor), (byte)(c.B * factor));

    // ===== 按钮事件 =====

    private void NewUserButton_Click(object sender, RoutedEventArgs e)
    {
        var name = SimpleInputDialog.ShowDialog(this, "新建用户", "请输入用户名：", "");
        if (string.IsNullOrWhiteSpace(name)) return;
        name = name.Trim();

        if (!UserService.Instance.CreateUser(name))
        {
            MessageBox.Show(this, $"用户「{name}」已存在，请直接选择。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        LoadUsers();
        // 选中刚创建的用户
        for (int i = 0; i < _users.Count; i++)
        {
            if (_users[i].Name == name) { UserList.SelectedIndex = i; break; }
        }
        Logger.Info("Launcher", $"新建用户：{name}");
    }

    private void UserList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        => LoginAndEnter();

    /// <summary>登录选中用户并进入主界面。</summary>
    private void LoginAndEnter()
    {
        if (UserList.SelectedItem is not UserItem item) return;
        var profile = UserService.Instance.Login(item.Name);
        if (profile == null)
        {
            MessageBox.Show(this, $"用户「{item.Name}」登录失败。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        Entered = true;
        Logger.Info("Launcher", $"用户 {item.Name} 登录，进入主界面");
        Close();
    }

    private void QuitButton_Click(object sender, RoutedEventArgs e)
        => CloseButton_Click(sender, e);

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Entered = false;
        Logger.Info("Launcher", "启动页关闭（退出应用）");
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

    // ===== 简易输入对话框 =====

    /// <summary>简易单行输入对话框（避免为单个输入框单独建 XAML 窗口）。</summary>
    private static class SimpleInputDialog
    {
        public static string? ShowDialog(Window owner, string title, string prompt, string defaultValue)
        {
            var dlg = new Window
            {
                Title = title,
                Width = 360,
                Height = 160,
                Owner = owner,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize,
                WindowStyle = WindowStyle.SingleBorderWindow
            };
            var panel = new System.Windows.Controls.StackPanel { Margin = new Thickness(12) };
            var lbl = new System.Windows.Controls.TextBlock
            {
                Text = prompt,
                Margin = new Thickness(0, 0, 0, 6),
                FontSize = 13
            };
            var input = new System.Windows.Controls.TextBox
            {
                Text = defaultValue,
                FontSize = 13,
                Margin = new Thickness(0, 0, 0, 12)
            };
            input.SelectAll();
            input.Focus();
            var btnPanel = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            var okBtn = new System.Windows.Controls.Button { Content = "确定", Width = 72, Margin = new Thickness(4, 0, 0, 0), IsDefault = true };
            var cancelBtn = new System.Windows.Controls.Button { Content = "取消", Width = 72, Margin = new Thickness(4, 0, 0, 0), IsCancel = true };
            string? result = null;
            okBtn.Click += (_, _) => { result = input.Text; dlg.Close(); };
            btnPanel.Children.Add(okBtn);
            btnPanel.Children.Add(cancelBtn);
            panel.Children.Add(lbl);
            panel.Children.Add(input);
            panel.Children.Add(btnPanel);
            dlg.Content = panel;
            dlg.ShowDialog();
            return result;
        }
    }
}
