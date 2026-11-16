using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using DesktopGent.Services.Core;
using Microsoft.Win32;

namespace DesktopGent.Windows;

/// <summary>
/// 通用设置窗口：
/// - 开机自启动切换
/// - 用户档案导出/导入（ZIP）
/// - 导入助手角色（把包含 theme.yaml 的文件夹复制到 static/）
/// - 打开用户数据目录
/// 配色跟随角色主题。
/// </summary>
public partial class SettingsWindow : Window
{
    private string _roleName = "默认角色";

    /// <summary>角色导入成功后的回调（通常由 GentWindow 注入，用于刷新角色菜单）。</summary>
    public Action? OnRoleImported { get; set; }

    public SettingsWindow()
    {
        InitializeComponent();
        LoadSettings();
    }

    /// <summary>应用角色配色主题。由 GentWindow/TalkWindow 在打开窗口前调用。</summary>
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

            foreach (var btn in new[] { ExportButton, ImportButton, ImportRoleButton, OpenUserDirButton })
            {
                btn.Background = new SolidColorBrush(theme.SurfaceColor);
                btn.Foreground = new SolidColorBrush(theme.TextColor);
                btn.BorderBrush = new SolidColorBrush(theme.PrimaryColor);
            }
        }
        catch
        {
            // 容错
        }
    }

    private static Color WithAlpha(Color c, double alpha)
        => Color.FromArgb((byte)(alpha * 255), c.R, c.G, c.B);

    private static Color Darken(Color c, double factor)
        => Color.FromRgb((byte)(c.R * factor), (byte)(c.G * factor), (byte)(c.B * factor));

    private void LoadSettings()
    {
        AutoStartCheckBox.IsChecked = AutoStartService.IsEnabled();

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
            MessageBox.Show("修改开机自启动失败，请查看日志。",
                "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
            AutoStartCheckBox.IsChecked = !enabled;
        }
        else
        {
            Logger.Info("UI", $"设置：开机自启动 -> {(enabled ? "启用" : "禁用")}");
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

        var userName = UserService.Instance.CurrentUserName;
        var inputDlg = new InputDialog(
            "导入用户档案",
            "请输入目标用户名（导入到该用户的目录）：",
            userName);
        inputDlg.Owner = this;
        if (inputDlg.ShowDialog() != true || string.IsNullOrWhiteSpace(inputDlg.Result))
            return;
        var targetName = inputDlg.Result.Trim();

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

    /// <summary>
    /// 导入助手角色：用户选择包含 theme.yaml 的文件夹，程序复制到 static/&lt;文件夹名&gt;/。
    /// 成功后通知 GentWindow 刷新角色菜单。
    /// </summary>
    private void ImportRoleButton_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog
        {
            Title = "选择要导入的角色文件夹（需包含 theme.yaml）"
        };
        if (dlg.ShowDialog() != true) return;

        var source = dlg.FolderName;
        if (string.IsNullOrWhiteSpace(source)) return;

        var themePath = Path.Combine(source, ConfigService.ThemeFileName);
        if (!File.Exists(themePath))
        {
            MessageBox.Show("所选文件夹内没有 theme.yaml，无法导入。", "导入失败",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var folderName = Path.GetFileName(source.TrimEnd('\\', '/'));
        if (string.IsNullOrWhiteSpace(folderName))
        {
            MessageBox.Show("无法识别文件夹名，请重试。", "导入失败",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var target = Path.Combine(App.BaseDir, "static", folderName);
        if (Directory.Exists(target))
        {
            MessageBox.Show($"static/{folderName} 已存在，请先重命名或删除后再导入。", "导入失败",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            Directory.CreateDirectory(target);
            foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(source, file);
                var dest = Path.Combine(target, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.Copy(file, dest, overwrite: true);
            }

            Logger.Info("UI", $"导入角色：{source} -> static/{folderName}");
            ConfigService.InvalidateCache();
            OnRoleImported?.Invoke();
            MessageBox.Show("角色导入成功，请在托盘「角色」菜单中切换。", "导入成功",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Logger.Error("UI", "导入角色失败", ex);
            MessageBox.Show("导入失败，请查看日志。", "错误",
                MessageBoxButton.OK, MessageBoxImage.Error);
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
