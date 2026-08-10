using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using DesktopPetCSharp.Services;
using Microsoft.Win32;

namespace DesktopPetCSharp.Windows;

/// <summary>
/// 通用设置窗口（P4.1 + P4.2）：
/// - 开机自启切换（注册表 HKCU\...\Run）
/// - 当前用户档案导出/导入（ZIP）
/// - 打开用户数据目录
/// 配色跟随角色主题。
/// </summary>
public partial class SettingsWindow : Window
{
    private string _roleName = "路飞";

    public SettingsWindow()
    {
        InitializeComponent();
        LoadSettings();
    }

    /// <summary>应用角色配色主题。由 PetWindow/TalkWindow 在打开窗口前调用。</summary>
    public void ApplyTheme(string role)
    {
        _roleName = role;
        try
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

            AutoStartCheckBox.Foreground = new SolidColorBrush(theme.TextColor);
            UserInfoLabel.Foreground = new SolidColorBrush(theme.TextColor);

            ExportButton.Background = new SolidColorBrush(theme.SurfaceColor);
            ExportButton.Foreground = new SolidColorBrush(theme.TextColor);
            ExportButton.BorderBrush = new SolidColorBrush(theme.PrimaryColor);

            ImportButton.Background = new SolidColorBrush(theme.SurfaceColor);
            ImportButton.Foreground = new SolidColorBrush(theme.TextColor);
            ImportButton.BorderBrush = new SolidColorBrush(theme.PrimaryColor);

            OpenUserDirButton.Background = new SolidColorBrush(theme.SurfaceColor);
            OpenUserDirButton.Foreground = new SolidColorBrush(theme.TextColor);
            OpenUserDirButton.BorderBrush = new SolidColorBrush(theme.PrimaryColor);
        }
        catch { /* 容错 */ }
    }

    private static Color WithAlpha(Color c, double alpha)
        => Color.FromArgb((byte)(alpha * 255), c.R, c.G, c.B);

    private static Color Darken(Color c, double factor)
        => Color.FromRgb((byte)(c.R * factor), (byte)(c.G * factor), (byte)(c.B * factor));

    private void LoadSettings()
    {
        // 自启状态
        AutoStartCheckBox.IsChecked = AutoStartService.IsEnabled();

        // 当前用户信息
        var userName = UserService.Instance.CurrentUserName;
        UserInfoLabel.Text = string.IsNullOrEmpty(userName)
            ? "当前用户：未登录"
            : $"当前用户：{userName}";
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount >= 1) DragMove();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void AutoStartCheckBox_Click(object sender, RoutedEventArgs e)
    {
        var enabled = AutoStartCheckBox.IsChecked == true;
        bool ok = enabled ? AutoStartService.Enable() : AutoStartService.Disable();
        if (!ok)
        {
            MessageBox.Show($"修改开机自启失败，请查看日志。",
                "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
            AutoStartCheckBox.IsChecked = !enabled;
        }
        else
        {
            Logger.Info("UI", $"设置：开机自启 -> {(enabled ? "启用" : "禁用")}");
        }
    }

    private void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        var userName = UserService.Instance.CurrentUserName;
        if (string.IsNullOrEmpty(userName))
        {
            MessageBox.Show("请先登录用户再导出档案。", "提示");
            return;
        }

        var dlg = new SaveFileDialog
        {
            Filter = "ZIP 文件|*.zip",
            FileName = $"{userName}_archive_{DateTime.Now:yyyyMMdd}.zip",
            Title = "选择导出位置"
        };
        if (dlg.ShowDialog() != true) return;

        if (UserDataPorter.Export(dlg.FileName))
        {
            MessageBox.Show($"已导出用户档案到：\n{dlg.FileName}", "导出成功");
        }
        else
        {
            MessageBox.Show("导出失败，请查看日志。", "错误");
        }
    }

    private void ImportButton_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "ZIP 文件|*.zip",
            Title = "选择要导入的档案 ZIP"
        };
        if (dlg.ShowDialog() != true) return;

        // 询问目标用户名
        var userName = UserService.Instance.CurrentUserName;
        var inputDlg = new InputDialog(
            "导入用户档案",
            "请输入目标用户名（导入到该用户的目录）：",
            userName);
        inputDlg.Owner = this;
        if (inputDlg.ShowDialog() != true || string.IsNullOrWhiteSpace(inputDlg.Result))
            return;
        var targetName = inputDlg.Result.Trim();

        // 登出当前用户，释放数据库连接
        if (UserService.Instance.CurrentUser != null)
        {
            UserService.Instance.Logout();
        }

        if (UserDataPorter.Import(dlg.FileName, targetName))
        {
            MessageBox.Show($"已导入用户档案：{targetName}\n请重新登录该用户。",
                "导入成功");
            Close();
        }
        else
        {
            MessageBox.Show("导入失败，请查看日志。", "错误");
        }
    }

    private void OpenUserDirButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var userDir = UserService.Instance.CurrentUserDir;
            if (!Directory.Exists(userDir))
            {
                MessageBox.Show("用户数据目录不存在。", "提示");
                return;
            }
            Process.Start(new ProcessStartInfo("explorer.exe", userDir) { UseShellExecute = true });
            Logger.Info("UI", $"打开用户数据目录：{userDir}");
        }
        catch (Exception ex)
        {
            Logger.Error("UI", "打开用户数据目录失败", ex);
        }
    }
}
