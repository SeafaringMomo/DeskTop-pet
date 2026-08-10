using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using DesktopPetCSharp.Services;

namespace DesktopPetCSharp.Windows;

/// <summary>
/// 简易输入对话框：用于在用户档案导入等场景获取用户输入。
/// 提供 Prompt、DefaultValue 与 OK/Cancel 按钮。
/// </summary>
public partial class InputDialog : Window
{
    /// <summary>用户最终输入的文本（点击确定时为非空，取消时为 null）。</summary>
    public string? Result { get; private set; }

    public InputDialog(string title, string prompt, string defaultValue = "")
    {
        InitializeComponent();
        Title = title;
        PromptText.Text = prompt;
        InputBox.Text = defaultValue;
        InputBox.SelectAll();
        InputBox.Focus();
    }

    public new bool? ShowDialog()
    {
        base.ShowDialog();
        return Result != null ? true : false;
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        Result = InputBox.Text;
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        Result = null;
        DialogResult = false;
        Close();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount >= 1) DragMove();
    }

    /// <summary>应用角色配色主题。可选。</summary>
    public void ApplyTheme(string role)
    {
        try
        {
            var theme = ConfigService.Load().GetTheme(role);
            RootBorder.BorderBrush = new SolidColorBrush(theme.PrimaryColor);
            RootBorder.Background = new SolidColorBrush(Color.FromArgb(0xF2, theme.BackgroundColor.R, theme.BackgroundColor.G, theme.BackgroundColor.B));
            PromptText.Foreground = new SolidColorBrush(theme.TextColor);
            InputBox.BorderBrush = new SolidColorBrush(theme.PrimaryColor);
            OkButton.Background = new SolidColorBrush(theme.PrimaryColor);
            OkButton.Foreground = new SolidColorBrush(theme.BackgroundColor);
            CancelButton.Background = new SolidColorBrush(theme.SurfaceColor);
            CancelButton.Foreground = new SolidColorBrush(theme.TextColor);
        }
        catch { }
    }
}
