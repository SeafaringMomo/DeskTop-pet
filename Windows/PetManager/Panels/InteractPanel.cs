using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using DesktopPetCSharp.Models;
using DesktopPetCSharp.Services;

namespace DesktopPetCSharp.Windows;

/// <summary>
/// 互动面板（P8 完善）：抚摸/击掌/玩耍按钮 + 实时冷却显示。
/// - 抚摸（pat）：+1 好感度，冷却 5 秒
/// - 击掌（highfive）：+2 好感度，冷却 30 秒
/// - 玩耍（play）：预留按钮（小游戏暂不实现），点击提示"敬请期待"
///
/// 点击互动按钮 → InteractionService.Interact() → 联动 PetWindow 播放反应动画 → 显示结果。
/// 冷却中按钮禁用并显示剩余秒数；面板每秒刷新一次冷却状态。
/// </summary>
public class InteractPanel : UserControl, IPetManagerPanel
{
    private readonly PetWindow _pet;
    private RoleTheme _theme = RoleTheme.DefaultLuffy;

    private readonly TextBlock _hintText = new();
    private readonly TextBlock _statusText = new();
    private readonly Button _patBtn = new();
    private readonly Button _highfiveBtn = new();
    private readonly Button _playBtn = new();
    private readonly TextBlock _patCd = new();
    private readonly TextBlock _highfiveCd = new();
    private readonly TextBlock _playCd = new();

    /// <summary>冷却刷新计时器（1 秒/次）。</summary>
    private DispatcherTimer? _cdTimer;

    public InteractPanel(PetWindow pet)
    {
        _pet = pet;
        BuildUi();
        StartCooldownTimer();
    }

    private void BuildUi()
    {
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // 标题
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // 抚摸
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // 击掌
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // 玩耍
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // 状态文本
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // 提示

        // 0) 标题
        var title = new TextBlock
        {
            Text = "🤝 与宠物互动",
            FontFamily = new FontFamily("Microsoft YaHei UI"),
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 10)
        };
        grid.Children.Add(title);
        Grid.SetRow(title, 0);

        // 1) 抚摸
        var patRow = BuildActionRow("👋", "抚摸", "+1 好感度 · 冷却 5 秒", _patBtn, _patCd);
        _patBtn.Click += (_, _) => DoInteract("pat");
        grid.Children.Add(patRow);
        Grid.SetRow(patRow, 1);

        // 2) 击掌
        var highfiveRow = BuildActionRow("✋", "击掌", "+2 好感度 · 冷却 30 秒", _highfiveBtn, _highfiveCd);
        _highfiveBtn.Click += (_, _) => DoInteract("highfive");
        grid.Children.Add(highfiveRow);
        Grid.SetRow(highfiveRow, 2);

        // 3) 玩耍（预留）
        var playRow = BuildActionRow("🎮", "玩耍", "小游戏 · 敬请期待", _playBtn, _playCd);
        _playBtn.Click += (_, _) => DoInteract("play");
        grid.Children.Add(playRow);
        Grid.SetRow(playRow, 3);

        // 4) 状态文本
        _statusText.FontFamily = new FontFamily("Microsoft YaHei UI");
        _statusText.FontSize = 12;
        _statusText.Margin = new Thickness(0, 12, 0, 0);
        _statusText.TextWrapping = TextWrapping.Wrap;
        grid.Children.Add(_statusText);
        Grid.SetRow(_statusText, 4);

        // 5) 提示
        _hintText.Text = "提示：双击宠物即可快速抚摸。饱食度>80 时互动好感度有 ×1.2 加成。";
        _hintText.FontFamily = new FontFamily("Microsoft YaHei UI");
        _hintText.FontSize = 11;
        _hintText.Opacity = 0.7;
        _hintText.TextWrapping = TextWrapping.Wrap;
        _hintText.Margin = new Thickness(0, 6, 0, 0);
        grid.Children.Add(_hintText);
        Grid.SetRow(_hintText, 5);

        Content = grid;
    }

    /// <summary>构建一行互动按钮（图标 + 信息 + 冷却文本 + 按钮）。btn / cdText 由调用方传入字段引用。</summary>
    private Border BuildActionRow(string icon, string name, string desc, Button btn, TextBlock cdText)
    {
        var border = new Border
        {
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8),
            Margin = new Thickness(0, 0, 0, 6),
            BorderThickness = new Thickness(1)
        };

        var innerGrid = new Grid();
        innerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });   // 图标
        innerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // 信息
        innerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });  // 冷却文本
        innerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });  // 按钮

        // 图标
        var iconTb = new TextBlock
        {
            Text = icon,
            FontSize = 22,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        innerGrid.Children.Add(iconTb);
        Grid.SetColumn(iconTb, 0);

        // 信息
        var info = new StackPanel { Margin = new Thickness(8, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        var nameTb = new TextBlock
        {
            Text = name,
            FontWeight = FontWeights.Bold,
            FontSize = 13,
            FontFamily = new FontFamily("Microsoft YaHei UI")
        };
        info.Children.Add(nameTb);
        var descTb = new TextBlock
        {
            Text = desc,
            FontSize = 11,
            Opacity = 0.7,
            FontFamily = new FontFamily("Microsoft YaHei UI")
        };
        info.Children.Add(descTb);
        innerGrid.Children.Add(info);
        Grid.SetColumn(info, 1);

        // 冷却文本
        cdText.FontSize = 11;
        cdText.VerticalAlignment = VerticalAlignment.Center;
        cdText.Margin = new Thickness(0, 0, 8, 0);
        cdText.FontFamily = new FontFamily("Microsoft YaHei UI");
        innerGrid.Children.Add(cdText);
        Grid.SetColumn(cdText, 2);

        // 按钮
        btn.Content = name;
        btn.Width = 64;
        btn.Height = 30;
        btn.FontWeight = FontWeights.Bold;
        btn.FontFamily = new FontFamily("Microsoft YaHei UI");
        btn.FontSize = 11;
        innerGrid.Children.Add(btn);
        Grid.SetColumn(btn, 3);

        border.Child = innerGrid;
        return border;
    }

    private void DoInteract(string action)
    {
        var role = ConfigService.Load().Role;
        var result = InteractionService.Instance.Interact(role, action);

        _statusText.Text = result.Message;
        if (result.Success)
        {
            // 联动 PetWindow 播放反应动画
            if (action == "pat") _pet.PlayPatReaction();
            else if (action == "highfive") _pet.PlayHighfiveReaction();
        }

        RefreshCooldown();
    }

    private void StartCooldownTimer()
    {
        _cdTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _cdTimer.Tick += (_, _) => RefreshCooldown();
        _cdTimer.Start();
    }

    /// <summary>刷新所有按钮的冷却状态。</summary>
    private void RefreshCooldown()
    {
        var role = ConfigService.Load().Role;
        UpdateButtonCooldown(_patBtn, _patCd, role, "pat");
        UpdateButtonCooldown(_highfiveBtn, _highfiveCd, role, "highfive");
        UpdateButtonCooldown(_playBtn, _playCd, role, "play");
    }

    private void UpdateButtonCooldown(Button btn, TextBlock cdText, string role, string action)
    {
        var remaining = InteractionService.Instance.GetCooldownRemaining(role, action);
        if (remaining > 0)
        {
            btn.IsEnabled = false;
            cdText.Text = $"冷却 {remaining}s";
            cdText.Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0x66, 0x66));
        }
        else
        {
            // play 永远不可用（暂未实现）
            if (action == "play")
            {
                btn.IsEnabled = false;
                cdText.Text = "建设中";
                cdText.Foreground = new SolidColorBrush(_theme.TextColor);
            }
            else
            {
                btn.IsEnabled = true;
                cdText.Text = "";
            }
        }
    }

    public void ApplyTheme(RoleTheme theme)
    {
        _theme = theme;

        if (Content is Grid grid)
        {
            // 标题 + 状态 + 提示文字色
            foreach (var child in grid.Children)
            {
                if (child is TextBlock tb)
                    tb.Foreground = new SolidColorBrush(theme.TextColor);
                if (child is Border b && b.Child is Grid innerGrid)
                {
                    b.Background = new SolidColorBrush(Color.FromArgb(0x30, theme.SurfaceColor.R, theme.SurfaceColor.G, theme.SurfaceColor.B));
                    b.BorderBrush = new SolidColorBrush(theme.BorderColor);
                    foreach (var ic in innerGrid.Children)
                    {
                        if (ic is TextBlock itb) itb.Foreground = new SolidColorBrush(theme.TextColor);
                    }
                }
            }
        }

        // 按钮配色
        foreach (var btn in new[] { _patBtn, _highfiveBtn, _playBtn })
        {
            btn.Background = new SolidColorBrush(theme.PrimaryColor);
            btn.Foreground = new SolidColorBrush(theme.BackgroundColor);
            btn.BorderBrush = new SolidColorBrush(theme.BorderColor);
            btn.BorderThickness = new Thickness(1);
        }
    }

    public void Refresh()
    {
        RefreshCooldown();
        // 显示当前好感度等级
        var role = ConfigService.Load().Role;
        var affinity = AffinityService.Instance.GetAffinity(role);
        var level = AffinityService.Instance.GetLevel(affinity);
        _statusText.Text = $"当前 {role} 好感度：{affinity}（{level.Name}）";
    }
}
