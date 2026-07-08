using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DesktopPetCSharp.Models;
using DesktopPetCSharp.Services;

namespace DesktopPetCSharp.Windows;

/// <summary>
/// 宠物管理窗口（P5）：Tab 容器，顶部菜单 4 个选项卡（商店/互动/喂食/状态）。
/// 从宠物右键菜单「宠物管理」打开。主题跟随当前角色。
/// </summary>
public partial class PetManagerWindow : Window
{
    private readonly PetWindow _pet;
    private ShopPanel? _shopPanel;
    private InteractPanel? _interactPanel;
    private FeedPanel? _feedPanel;
    private StatusPanel? _statusPanel;

    /// <summary>当前激活的 Tab：shop / interact / feed / status</summary>
    private string _activeTab = "shop";

    public PetManagerWindow(PetWindow pet)
    {
        InitializeComponent();
        _pet = pet;
        var role = ConfigService.Load().Role;
        ApplyTheme(role);
        ShowTab("shop");
    }

    /// <summary>应用角色主题到窗口。</summary>
    public void ApplyTheme(string role)
    {
        var theme = ConfigService.Load().GetTheme(role);
        RootBorder.BorderBrush = new SolidColorBrush(theme.BorderColor);
        RootBorder.Background = new SolidColorBrush(Color.FromArgb(0xF2, theme.BackgroundColor.R, theme.BackgroundColor.G, theme.BackgroundColor.B));
        TitleBar.Background = new LinearGradientBrush(theme.PrimaryColor, theme.AccentColor, 0);
        TitleText.Foreground = new SolidColorBrush(theme.PrimaryColor);
        CloseButton.Foreground = new SolidColorBrush(theme.PrimaryColor);
        CloseButton.BorderBrush = new SolidColorBrush(theme.PrimaryColor);

        // Tab 按钮配色（激活的用主色，其他用表面色）
        UpdateTabButtons(theme);

        // 同步到子 Panel
        _shopPanel?.ApplyTheme(theme);
        _interactPanel?.ApplyTheme(theme);
        _feedPanel?.ApplyTheme(theme);
        _statusPanel?.ApplyTheme(theme);
    }

    private void UpdateTabButtons(RoleTheme theme)
    {
        var activeBg = new SolidColorBrush(theme.PrimaryColor);
        var activeFg = new SolidColorBrush(theme.BackgroundColor);
        var inactiveBg = new SolidColorBrush(theme.SurfaceColor);
        var inactiveFg = new SolidColorBrush(theme.TextColor);
        var border = new SolidColorBrush(theme.BorderColor);

        foreach (var btn in new[] { TabShop, TabInteract, TabFeed, TabStatus })
        {
            bool active = btn == ActiveTabButton;
            btn.Background = active ? activeBg : inactiveBg;
            btn.Foreground = active ? activeFg : inactiveFg;
            btn.BorderBrush = border;
            btn.FontWeight = active ? FontWeights.Bold : FontWeights.Normal;
        }
    }

    private Button ActiveTabButton => _activeTab switch
    {
        "shop" => TabShop,
        "interact" => TabInteract,
        "feed" => TabFeed,
        "status" => TabStatus,
        _ => TabShop
    };

    private void ShowTab(string tab)
    {
        _activeTab = tab;
        ContentArea.Children.Clear();

        var role = ConfigService.Load().Role;
        var theme = ConfigService.Load().GetTheme(role);

        UIElement panel = tab switch
        {
            "shop" => _shopPanel ??= new ShopPanel(_pet),
            "interact" => _interactPanel ??= new InteractPanel(_pet),
            "feed" => _feedPanel ??= new FeedPanel(_pet),
            "status" => _statusPanel ??= new StatusPanel(_pet),
            _ => _shopPanel ??= new ShopPanel(_pet)
        };

        // 通用接口 ApplyTheme
        if (panel is IPetManagerPanel p) p.ApplyTheme(theme);

        ContentArea.Children.Add(panel);
        UpdateTabButtons(theme);

        // 刷新数据
        if (panel is IPetManagerPanel pp) pp.Refresh();
    }

    // ===== Tab 切换事件 =====
    private void TabShop_Click(object sender, RoutedEventArgs e) => ShowTab("shop");
    private void TabInteract_Click(object sender, RoutedEventArgs e) => ShowTab("interact");
    private void TabFeed_Click(object sender, RoutedEventArgs e) => ShowTab("feed");
    private void TabStatus_Click(object sender, RoutedEventArgs e) => ShowTab("status");

    // ===== 标题栏拖动 + 关闭 =====
    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}

/// <summary>宠物管理 Panel 通用接口。</summary>
public interface IPetManagerPanel
{
    /// <summary>应用主题配色。</summary>
    void ApplyTheme(RoleTheme theme);
    /// <summary>刷新数据（切换到该 Tab 或外部触发时调用）。</summary>
    void Refresh();
}
