using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DesktopGent.Models;
using DesktopGent.Services.Core;

namespace DesktopGent.Windows.GentManager.Panels;

/// <summary>
/// 喂食面板：展示当前角色的饱食度与背包食物，点击「喂食」扣背包、加饱食度、按偏好加好感度。
/// 食物购买已移到商店面板；喂食只更新状态，不播放响应动画。
/// </summary>
public class FeedPanel : UserControl, IGentManagerPanel
{
    private readonly GentWindow _gent;
    private RoleTheme _theme = RoleTheme.DefaultRole;
    private StackPanel _listPanel = new();
    private TextBlock _satietyText = new();
    private TextBlock _hintText = new();

    public FeedPanel(GentWindow gent)
    {
        _gent = gent;
        BuildUi();
    }

    private void BuildUi()
    {
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // 顶部：饱食度显示
        var topPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        _satietyText = new TextBlock
        {
            FontFamily = new FontFamily("Microsoft YaHei UI"),
            FontSize = 13,
            FontWeight = FontWeights.Bold
        };
        topPanel.Children.Add(_satietyText);
        grid.Children.Add(topPanel);
        Grid.SetRow(topPanel, 0);

        // 中间：背包食物列表（滚动）
        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = _listPanel,
            Padding = new Thickness(4)
        };
        grid.Children.Add(scroll);
        Grid.SetRow(scroll, 1);

        // 底部：提示
        _hintText = new TextBlock
        {
            FontFamily = new FontFamily("Microsoft YaHei UI"),
            FontSize = 11,
            Opacity = 0.7,
            Text = "提示：选中食物后点「喂食」。饱食度>80 有好感度加成。",
            TextWrapping = TextWrapping.Wrap
        };
        grid.Children.Add(_hintText);
        Grid.SetRow(_hintText, 2);

        Content = grid;
    }

    public void ApplyTheme(RoleTheme theme)
    {
        _theme = theme;
        _satietyText.Foreground = new SolidColorBrush(theme.TextColor);
        _hintText.Foreground = new SolidColorBrush(theme.TextColor);
        _listPanel.Background = new SolidColorBrush(Color.FromArgb(0x20, theme.SurfaceColor.R, theme.SurfaceColor.G, theme.SurfaceColor.B));
    }

    public void Refresh()
    {
        _listPanel.Children.Clear();
        var user = UserService.Instance.CurrentUser;
        if (user == null) return;

        var role = ConfigService.Load().Role;
        var satiety = user.GetSatiety(role);
        var status = satiety < 20 ? " 🆘 饥饿" : satiety > 80 ? " 😋 饱腹" : "";
        _satietyText.Text = $"🍖 {role} 饱食度：{satiety}/100{status}";

        var foods = ConfigService.LoadFoods().Foods;
        foreach (var food in foods)
        {
            var count = user.GetInventory(food.Id);
            if (count <= 0) continue;
            _listPanel.Children.Add(CreateFoodItem(food, count, role));
        }

        if (_listPanel.Children.Count == 0)
        {
            _listPanel.Children.Add(new TextBlock
            {
                Text = "背包为空，请到商店购买食物。",
                FontFamily = new FontFamily("Microsoft YaHei UI"),
                FontSize = 12,
                Opacity = 0.6,
                Margin = new Thickness(8),
                Foreground = new SolidColorBrush(_theme.TextColor)
            });
        }
    }

    private Border CreateFoodItem(Food food, int count, string role)
    {
        var border = new Border
        {
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8),
            Margin = new Thickness(0, 0, 0, 6),
            Background = new SolidColorBrush(Color.FromArgb(0x30, _theme.SurfaceColor.R, _theme.SurfaceColor.G, _theme.SurfaceColor.B)),
            BorderBrush = new SolidColorBrush(_theme.BorderColor),
            BorderThickness = new Thickness(1)
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var iconText = new TextBlock
        {
            Text = food.Icon,
            FontSize = 24,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        grid.Children.Add(iconText);
        Grid.SetColumn(iconText, 0);

        var infoPanel = new StackPanel { Margin = new Thickness(8, 0, 8, 0) };
        var preference = food.GetPreference(role);
        var prefDesc = preference switch
        {
            FoodPreferenceLevel.Loved => "❤️ 喜欢",
            FoodPreferenceLevel.Disliked => "💩 讨厌",
            _ => "🙂 普通"
        };
        infoPanel.Children.Add(new TextBlock
        {
            Text = $"{food.Name}",
            FontWeight = FontWeights.Bold,
            FontSize = 13,
            Foreground = new SolidColorBrush(_theme.TextColor),
            FontFamily = new FontFamily("Microsoft YaHei UI")
        });
        infoPanel.Children.Add(new TextBlock
        {
            Text = $"{prefDesc} · 饱食度 +{food.SatietyGain}",
            FontSize = 11,
            Opacity = 0.7,
            Foreground = new SolidColorBrush(_theme.TextColor),
            FontFamily = new FontFamily("Microsoft YaHei UI")
        });
        grid.Children.Add(infoPanel);
        Grid.SetColumn(infoPanel, 1);

        var countText = new TextBlock
        {
            Text = $"×{count}",
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
            Foreground = new SolidColorBrush(_theme.TextColor),
            FontFamily = new FontFamily("Microsoft YaHei UI")
        };
        grid.Children.Add(countText);
        Grid.SetColumn(countText, 2);

        var btn = new Button
        {
            Content = "喂食",
            Width = 56,
            Height = 28,
            Background = new SolidColorBrush(_theme.PrimaryColor),
            Foreground = new SolidColorBrush(_theme.BackgroundColor),
            FontWeight = FontWeights.Bold,
            BorderBrush = new SolidColorBrush(_theme.BorderColor),
            BorderThickness = new Thickness(1),
            FontFamily = new FontFamily("Microsoft YaHei UI"),
            FontSize = 11
        };
        btn.Click += (_, _) =>
        {
            var result = FeedService.Instance.Feed(role, food.Id);
            MessageBox.Show(result.Message, result.Success ? "喂食成功" : "喂食失败",
                MessageBoxButton.OK, result.Success ? MessageBoxImage.Information : MessageBoxImage.Warning);
            if (result.Success) Refresh();
        };
        grid.Children.Add(btn);
        Grid.SetColumn(btn, 3);

        border.Child = grid;
        return border;
    }
}
