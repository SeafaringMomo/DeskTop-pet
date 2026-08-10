using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DesktopPetCSharp.Models;
using DesktopPetCSharp.Services;

namespace DesktopPetCSharp.Windows;

/// <summary>
/// 商店面板（P5）：显示当前角色的皮肤列表，支持购买/装备/试穿。
/// 皮肤价格统一 400 金币（从 pet_management.yaml 读取），好感度等级影响折扣。
/// </summary>
public class ShopPanel : UserControl, IPetManagerPanel
{
    private readonly PetWindow _pet;
    private RoleTheme _theme = RoleTheme.DefaultLuffy;
    private StackPanel _listPanel = new();
    private TextBlock _coinsText = new();
    private TextBlock _hintText = new();

    public ShopPanel(PetWindow pet)
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

        // 中间：皮肤列表（滚动）
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
            Margin = new Thickness(0, 6, 0, 0),
            Text = "提示：皮肤价格统一 400 金币（好感度等级有折扣）。点击「装备」切换皮肤。"
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
        var user = Services.UserService.Instance.CurrentUser;
        if (user == null) return;

        _coinsText.Text = $"💰 金币：{user.Coins}";

        var role = ConfigService.Load().Role;
        var skins = Services.SkinService.Instance.GetSkinsByRole(role);
        var currentSkinId = Services.SkinService.Instance.GetCurrentSkin(role)?.Id;
        var mgmt = ConfigService.LoadPetManagement();
        var price = mgmt.Constants.SkinPrice;
        var affinity = user.GetAffinity(role);
        var level = Services.AffinityService.Instance.GetLevel(affinity);
        var finalPrice = (int)System.Math.Round(price * level.SkinDiscount);

        foreach (var skin in skins)
        {
            var item = CreateSkinItem(skin, user, currentSkinId, price, finalPrice, level);
            _listPanel.Children.Add(item);
        }
    }

    private Border CreateSkinItem(Skin skin, UserProfile user, string? currentSkinId, int basePrice, int finalPrice, AffinityLevel level)
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
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });  // 预览图
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });  // 信息
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });  // 按钮

        // 1) 预览图（加载皮肤 icon.png）
        var img = new Image
        {
            Width = 48,
            Height = 48,
            Stretch = Stretch.UniformToFill
        };
        try
        {
            var iconPath = System.IO.Path.Combine(Services.SkinService.Instance.GetSkinDir(skin), "icon.png");
            if (System.IO.File.Exists(iconPath))
            {
                var bmp = new BitmapImage(new System.Uri(iconPath));
                bmp.Freeze();
                img.Source = bmp;
            }
        }
        catch { /* 加载失败留空 */ }
        grid.Children.Add(img);
        Grid.SetColumn(img, 0);

        // 2) 信息
        var infoPanel = new StackPanel { Margin = new Thickness(8, 0, 8, 0) };
        var nameText = new TextBlock
        {
            Text = skin.Name,
            FontWeight = FontWeights.Bold,
            FontSize = 13,
            Foreground = new SolidColorBrush(_theme.TextColor),
            FontFamily = new FontFamily("Microsoft YaHei UI")
        };
        infoPanel.Children.Add(nameText);

        var descText = new TextBlock
        {
            Text = skin.Description,
            FontSize = 11,
            Opacity = 0.7,
            Foreground = new SolidColorBrush(_theme.TextColor),
            FontFamily = new FontFamily("Microsoft YaHei UI"),
            TextWrapping = TextWrapping.Wrap
        };
        infoPanel.Children.Add(descText);

        bool unlocked = user.UnlockedSkins.Contains(skin.Id);
        bool isCurrent = skin.Id == currentSkinId;

        var statusText = new TextBlock
        {
            FontSize = 11,
            Margin = new Thickness(0, 2, 0, 0),
            FontFamily = new FontFamily("Microsoft YaHei UI")
        };
        if (isCurrent)
        {
            statusText.Text = "✓ 当前装备";
            statusText.Foreground = new SolidColorBrush(_theme.PrimaryColor);
            statusText.FontWeight = FontWeights.Bold;
        }
        else if (unlocked)
        {
            statusText.Text = "已解锁";
            statusText.Foreground = new SolidColorBrush(_theme.AccentColor);
        }
        else
        {
            statusText.Text = $"💰 {finalPrice} 金币（{level.Name} {level.SkinDiscount:P0}）";
            statusText.Foreground = new SolidColorBrush(_theme.TextColor);
        }
        infoPanel.Children.Add(statusText);
        grid.Children.Add(infoPanel);
        Grid.SetColumn(infoPanel, 1);

        // 3) 操作按钮
        var btn = new Button
        {
            Width = 64,
            Height = 28,
            FontFamily = new FontFamily("Microsoft YaHei UI"),
            FontSize = 11,
            BorderBrush = new SolidColorBrush(_theme.BorderColor),
            BorderThickness = new Thickness(1)
        };

        if (isCurrent)
        {
            btn.Content = "已装备";
            btn.IsEnabled = false;
            btn.Background = new SolidColorBrush(_theme.SurfaceColor);
            btn.Foreground = new SolidColorBrush(_theme.TextColor);
        }
        else if (unlocked)
        {
            btn.Content = "装备";
            btn.Background = new SolidColorBrush(_theme.PrimaryColor);
            btn.Foreground = new SolidColorBrush(_theme.BackgroundColor);
            btn.FontWeight = FontWeights.Bold;
            btn.Click += (_, _) =>
            {
                _pet.SwitchSkin(skin.Id);
                Refresh();
            };
        }
        else
        {
            btn.Content = "购买";
            btn.Background = new SolidColorBrush(_theme.AccentColor);
            btn.Foreground = new SolidColorBrush(_theme.TextColor);
            btn.Click += (_, _) =>
            {
                if (user.Coins < finalPrice)
                {
                    MessageBox.Show($"金币不足！需要 {finalPrice}，当前 {user.Coins}。", "购买失败",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                var result = MessageBox.Show(
                    $"确认购买「{skin.Name}」？\n价格：{finalPrice} 金币（原价 {basePrice}，{level.Name} {level.SkinDiscount:P0}）\n购买后金币：{user.Coins - finalPrice}",
                    "确认购买", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result != MessageBoxResult.Yes) return;

                if (Services.SkinService.Instance.Purchase(skin.Id, out var failReason))
                {
                    MessageBox.Show($"购买成功！已解锁「{skin.Name}」", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                    Refresh();
                }
                else
                {
                    MessageBox.Show($"购买失败：{failReason}", "失败", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            };
        }
        grid.Children.Add(btn);
        Grid.SetColumn(btn, 2);

        border.Child = grid;
        return border;
    }
}
