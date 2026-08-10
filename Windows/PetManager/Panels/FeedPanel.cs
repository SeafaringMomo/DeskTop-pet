using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DesktopPetCSharp.Models;
using DesktopPetCSharp.Services;

namespace DesktopPetCSharp.Windows;

/// <summary>
/// 喂食面板（P7）：背包食物网格 + 当前角色饱食度。
/// 选中食物点「喂食」→ 扣背包 → 加饱食度 → 加好感度（按偏好）→ 动画/音效。
/// </summary>
public class FeedPanel : UserControl, IPetManagerPanel
{
    private readonly PetWindow _pet;
    private RoleTheme _theme = RoleTheme.DefaultLuffy;
    private StackPanel _listPanel = new();
    private TextBlock _satietyText = new();
    private TextBlock _hintText = new();

    public FeedPanel(PetWindow pet)
    {
        _pet = pet;
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

        // 底部：购买食物 + 提示
        var bottomPanel = new StackPanel();
        var buyButton = new Button
        {
            Content = "🛒 购买食物",
            Width = 100,
            Height = 28,
            Margin = new Thickness(0, 4, 0, 4),
            HorizontalAlignment = HorizontalAlignment.Left,
            FontFamily = new FontFamily("Microsoft YaHei UI"),
            FontSize = 11
        };
        buyButton.Click += (_, _) => ShowBuyDialog();
        bottomPanel.Children.Add(buyButton);

        _hintText = new TextBlock
        {
            FontFamily = new FontFamily("Microsoft YaHei UI"),
            FontSize = 11,
            Opacity = 0.7,
            Text = "提示：选中食物后点「喂食」。饱食度>80 有好感度加成。"
        };
        bottomPanel.Children.Add(_hintText);
        grid.Children.Add(bottomPanel);
        Grid.SetRow(bottomPanel, 2);

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
        var user = Services.UserService.Instance.CurrentUser;
        if (user == null) return;

        var role = ConfigService.Load().Role;
        var satiety = user.GetSatiety(role);
        var status = satiety < 20 ? " ⚠ 饥饿" : (satiety > 80 ? " 😋 饱腹" : "");
        _satietyText.Text = $"🍖 {role} 饱食度：{satiety}/100{status}";

        var foods = ConfigService.LoadFoods().Foods;
        foreach (var food in foods)
        {
            var count = user.GetInventory(food.Id);
            if (count <= 0) continue;  // 只显示背包中有的食物

            var item = CreateFoodItem(food, count, role);
            _listPanel.Children.Add(item);
        }

        if (_listPanel.Children.Count == 0)
        {
            _listPanel.Children.Add(new TextBlock
            {
                Text = "背包为空，请先购买食物",
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
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });   // 图标
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // 信息
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });  // 数量
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });  // 按钮

        // 1) 图标
        var iconText = new TextBlock
        {
            Text = food.Icon,
            FontSize = 24,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        grid.Children.Add(iconText);
        Grid.SetColumn(iconText, 0);

        // 2) 信息
        var infoPanel = new StackPanel { Margin = new Thickness(8, 0, 8, 0) };
        var nameText = new TextBlock
        {
            Text = $"{food.Name}",
            FontWeight = FontWeights.Bold,
            FontSize = 13,
            Foreground = new SolidColorBrush(_theme.TextColor),
            FontFamily = new FontFamily("Microsoft YaHei UI")
        };
        infoPanel.Children.Add(nameText);

        var preference = food.GetPreference(role);
        var prefDesc = preference switch
        {
            FoodPreferenceLevel.Loved => "❤️ 喜欢",
            FoodPreferenceLevel.Disliked => "💔 讨厌",
            _ => "😐 普通"
        };
        var descText = new TextBlock
        {
            Text = $"{prefDesc} · 饱食度 +{food.SatietyGain}",
            FontSize = 11,
            Opacity = 0.7,
            Foreground = new SolidColorBrush(_theme.TextColor),
            FontFamily = new FontFamily("Microsoft YaHei UI")
        };
        infoPanel.Children.Add(descText);
        grid.Children.Add(infoPanel);
        Grid.SetColumn(infoPanel, 1);

        // 3) 数量
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

        // 4) 喂食按钮
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
            var result = Services.FeedService.Instance.Feed(role, food.Id);
            MessageBox.Show(result.Message, result.Success ? "喂食成功" : "喂食失败",
                MessageBoxButton.OK, result.Success ? MessageBoxImage.Information : MessageBoxImage.Warning);
            if (result.Success) Refresh();
        };
        grid.Children.Add(btn);
        Grid.SetColumn(btn, 3);

        border.Child = grid;
        return border;
    }

    private void ShowBuyDialog()
    {
        var role = ConfigService.Load().Role;
        var foods = ConfigService.LoadFoods().Foods;
        var user = Services.UserService.Instance.CurrentUser;
        if (user == null) return;

        // 简单购买对话框：列出食物，点击购买
        var dlg = new Window
        {
            Title = "购买食物",
            Width = 360,
            Height = 400,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = Window.GetWindow(this)
        };

        var stack = new StackPanel { Margin = new Thickness(8) };
        stack.Children.Add(new TextBlock
        {
            Text = $"💰 金币：{user.Coins}",
            FontFamily = new FontFamily("Microsoft YaHei UI"),
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 8)
        });

        foreach (var food in foods)
        {
            var itemPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
            itemPanel.Children.Add(new TextBlock
            {
                Text = $"{food.Icon} {food.Name}（{food.Price} 金币）",
                Width = 200,
                FontFamily = new FontFamily("Microsoft YaHei UI"),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center
            });

            var buyBtn = new Button
            {
                Content = "购买×1",
                Width = 70,
                Height = 26,
                FontFamily = new FontFamily("Microsoft YaHei UI"),
                FontSize = 11
            };
            buyBtn.Click += (_, _) =>
            {
                if (Services.FeedService.Instance.PurchaseFood(food.Id, 1, out var failReason))
                {
                    MessageBox.Show($"购买成功：{food.Name} ×1", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                    // 刷新金币显示
                    if (stack.Children[0] is TextBlock t) t.Text = $"💰 金币：{user.Coins}";
                    Refresh();
                }
                else
                {
                    MessageBox.Show($"购买失败：{failReason}", "失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            };
            itemPanel.Children.Add(buyBtn);
            stack.Children.Add(itemPanel);
        }

        var closeBtn = new Button
        {
            Content = "关闭",
            Width = 70,
            Height = 28,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 0, 0),
            FontFamily = new FontFamily("Microsoft YaHei UI")
        };
        closeBtn.Click += (_, _) => dlg.Close();
        stack.Children.Add(closeBtn);

        dlg.Content = stack;
        dlg.ShowDialog();
    }
}
