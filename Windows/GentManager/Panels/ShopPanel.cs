using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DesktopGent.Models;
using DesktopGent.Services.Core;

namespace DesktopGent.Windows.GentManager.Panels;

/// <summary>
/// 商店面板：金币充值 + 食物购买。
/// 原皮肤商城已废弃，皮肤系统删除后本面板改为养成经济入口；
/// 喂食面板中的“购买食物”入口已移到这里。
/// </summary>
public class ShopPanel : UserControl, IGentManagerPanel
{
    private readonly GentWindow _gent;
    private RoleTheme _theme = RoleTheme.DefaultRole;
    private readonly StackPanel _listPanel = new();
    private TextBlock _coinsText = new();
    private TextBlock _hintText = new();

    public ShopPanel(GentWindow gent)
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

        // 顶部：金币显示
        var topPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        _coinsText = new TextBlock
        {
            FontFamily = new FontFamily("Microsoft YaHei UI"),
            FontSize = 13,
            FontWeight = FontWeights.Bold
        };
        topPanel.Children.Add(_coinsText);
        grid.Children.Add(topPanel);
        Grid.SetRow(topPanel, 0);

        // 中间：充值 + 食物列表（滚动）
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
            Text = "提示：金币用于购买食物；食物购买后进入背包，在「喂食」页使用。",
            FontFamily = new FontFamily("Microsoft YaHei UI"),
            FontSize = 11,
            Opacity = 0.7,
            TextWrapping = TextWrapping.Wrap
        };
        grid.Children.Add(_hintText);
        Grid.SetRow(_hintText, 2);

        Content = grid;
    }

    public void ApplyTheme(RoleTheme theme)
    {
        _theme = theme;
        _coinsText.Foreground = new SolidColorBrush(theme.TextColor);
        _hintText.Foreground = new SolidColorBrush(theme.TextColor);
        _listPanel.Background = new SolidColorBrush(Color.FromArgb(0x20, theme.SurfaceColor.R, theme.SurfaceColor.G, theme.SurfaceColor.B));
    }

    public void Refresh()
    {
        _listPanel.Children.Clear();
        var user = UserService.Instance.CurrentUser;
        if (user == null) return;

        _coinsText.Text = $"💰 金币：{user.Coins}";

        // 1) 金币充值
        var rechargeStack = new StackPanel();
        rechargeStack.Children.Add(SectionTitle("💰 金币充值"));
        var rechargeRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
        foreach (var amount in new[] { 100, 500, 1000 })
        {
            var btn = new Button
            {
                Content = $"+{amount}",
                Width = 70,
                Height = 28,
                Margin = new Thickness(0, 0, 6, 0),
                FontFamily = new FontFamily("Microsoft YaHei UI"),
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                Background = new SolidColorBrush(_theme.PrimaryColor),
                Foreground = new SolidColorBrush(_theme.BackgroundColor),
                BorderBrush = new SolidColorBrush(_theme.BorderColor),
                BorderThickness = new Thickness(1)
            };
            btn.Click += (_, _) => Recharge(amount);
            rechargeRow.Children.Add(btn);
        }
        rechargeStack.Children.Add(rechargeRow);
        _listPanel.Children.Add(SectionCard(rechargeStack));

        // 2) 食物购买
        var foodStack = new StackPanel();
        foodStack.Children.Add(SectionTitle("🍱 食物购买"));
        var foods = ConfigService.LoadFoods().Foods;
        foreach (var food in foods)
        {
            foodStack.Children.Add(CreateFoodItem(food));
        }
        _listPanel.Children.Add(SectionCard(foodStack));
    }

    private void Recharge(int amount)
    {
        var user = UserService.Instance.CurrentUser;
        if (user == null) return;
        user.Coins += amount;
        UserService.Instance.SaveCurrentProfile();
        Logger.Info("Shop", $"金币充值：+{amount} → {user.Coins}");
        Refresh();
    }

    private Border SectionCard(StackPanel inner)
    {
        return new Border
        {
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8),
            Margin = new Thickness(0, 0, 0, 8),
            Background = new SolidColorBrush(Color.FromArgb(0x30, _theme.SurfaceColor.R, _theme.SurfaceColor.G, _theme.SurfaceColor.B)),
            BorderBrush = new SolidColorBrush(_theme.BorderColor),
            BorderThickness = new Thickness(1),
            Child = inner
        };
    }

    private TextBlock SectionTitle(string text)
    {
        return new TextBlock
        {
            Text = text,
            FontFamily = new FontFamily("Microsoft YaHei UI"),
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(_theme.TextColor),
            Margin = new Thickness(0, 0, 0, 2)
        };
    }

    private Border CreateFoodItem(Food food)
    {
        var border = new Border
        {
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8),
            Margin = new Thickness(0, 0, 0, 6),
            Background = new SolidColorBrush(Color.FromArgb(0x20, _theme.SurfaceColor.R, _theme.SurfaceColor.G, _theme.SurfaceColor.B)),
            BorderBrush = new SolidColorBrush(_theme.BorderColor),
            BorderThickness = new Thickness(1)
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
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

        var info = new StackPanel { Margin = new Thickness(8, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        info.Children.Add(new TextBlock
        {
            Text = $"{food.Name}（{food.Price} 金币）· 饱食度 +{food.SatietyGain}",
            FontFamily = new FontFamily("Microsoft YaHei UI"),
            FontSize = 12,
            Foreground = new SolidColorBrush(_theme.TextColor)
        });
        grid.Children.Add(info);
        Grid.SetColumn(info, 1);

        var buyBtn = new Button
        {
            Content = "购买×1",
            Width = 64,
            Height = 28,
            FontFamily = new FontFamily("Microsoft YaHei UI"),
            FontSize = 11,
            Background = new SolidColorBrush(_theme.PrimaryColor),
            Foreground = new SolidColorBrush(_theme.BackgroundColor),
            BorderBrush = new SolidColorBrush(_theme.BorderColor),
            BorderThickness = new Thickness(1)
        };
        buyBtn.Click += (_, _) =>
        {
            if (FeedService.Instance.PurchaseFood(food.Id, 1, out var failReason))
            {
                MessageBox.Show($"购买成功：{food.Name} ×1", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                Refresh();
            }
            else
            {
                MessageBox.Show($"购买失败：{failReason}", "失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        };
        grid.Children.Add(buyBtn);
        Grid.SetColumn(buyBtn, 2);

        border.Child = grid;
        return border;
    }
}
