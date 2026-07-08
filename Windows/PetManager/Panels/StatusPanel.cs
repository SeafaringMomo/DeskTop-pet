using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DesktopPetCSharp.Models;
using DesktopPetCSharp.Services;

namespace DesktopPetCSharp.Windows;

/// <summary>
/// 状态面板（P9 完善）：显示当前角色的好感度/饱食度/金币/已解锁皮肤总览。
///
/// 内容分区：
/// 1. 角色基础信息：当前角色名 + 当前皮肤名
/// 2. 经济与状态：金币 / 好感度（含等级名与折扣）/ 饱食度（含状态标记）
/// 3. 已解锁皮肤：列出所有已解锁皮肤（角色当前皮肤 + 其他角色皮肤汇总）
/// 4. 提示：好感度等级影响（皮肤折扣、AI 对话风格）
/// </summary>
public class StatusPanel : UserControl, IPetManagerPanel
{
    private readonly PetWindow _pet;
    private RoleTheme _theme = RoleTheme.DefaultLuffy;

    private readonly TextBlock _roleText = new();
    private readonly TextBlock _skinText = new();
    private readonly TextBlock _coinsText = new();
    private readonly TextBlock _affinityText = new();
    private readonly TextBlock _satietyText = new();
    private readonly TextBlock _levelHintText = new();
    private readonly StackPanel _skinsPanel = new();

    public StatusPanel(PetWindow pet)
    {
        _pet = pet;
        BuildUi();
    }

    private void BuildUi()
    {
        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(4)
        };

        var root = new StackPanel();

        // 标题
        root.Children.Add(MakeTitle("📊 宠物状态总览"));

        // 1) 角色与皮肤
        root.Children.Add(MakeSection("🎭 当前角色", new[] { _roleText, _skinText }));

        // 2) 经济与状态
        root.Children.Add(MakeSection("💰 经济 & 状态", new[] { _coinsText, _affinityText, _satietyText }));

        // 3) 已解锁皮肤
        var skinsSection = MakeSection("👑 已解锁皮肤", new UIElement[] { _skinsPanel });
        root.Children.Add(skinsSection);

        // 4) 等级影响提示
        root.Children.Add(MakeSection("🌟 好感度等级影响", new[] { _levelHintText }));

        scroll.Content = root;
        Content = scroll;
    }

    private Border MakeTitle(string text)
    {
        var tb = new TextBlock
        {
            Text = text,
            FontFamily = new FontFamily("Microsoft YaHei UI"),
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 10)
        };
        return WrapBorder(new StackPanel { Children = { tb } });
    }

    private Border MakeSection(string title, UIElement[] contents)
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = title,
            FontFamily = new FontFamily("Microsoft YaHei UI"),
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Opacity = 0.85,
            Margin = new Thickness(0, 0, 0, 4)
        });
        foreach (var c in contents) stack.Children.Add(c);
        return WrapBorder(stack);
    }

    private Border WrapBorder(StackPanel inner)
    {
        return new Border
        {
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8),
            Margin = new Thickness(0, 0, 0, 8),
            BorderThickness = new Thickness(1),
            Child = inner
        };
    }

    public void ApplyTheme(RoleTheme theme)
    {
        _theme = theme;

        if (Content is ScrollViewer sv && sv.Content is StackPanel root)
        {
            foreach (var child in root.Children)
            {
                if (child is Border b)
                {
                    b.Background = new SolidColorBrush(Color.FromArgb(0x30, theme.SurfaceColor.R, theme.SurfaceColor.G, theme.SurfaceColor.B));
                    b.BorderBrush = new SolidColorBrush(theme.BorderColor);
                    if (b.Child is StackPanel sp)
                    {
                        foreach (var ic in sp.Children)
                        {
                            if (ic is TextBlock tb)
                                tb.Foreground = new SolidColorBrush(theme.TextColor);
                        }
                    }
                }
            }
        }

        // 刷新皮肤列表（主题切换后重新着色）
        RefreshSkinsList();
    }

    public void Refresh()
    {
        var user = UserService.Instance.CurrentUser;
        if (user == null) return;

        var role = ConfigService.Load().Role;
        var skinId = user.GetCurrentSkin(role);
        var skin = string.IsNullOrEmpty(skinId) ? null : SkinService.Instance.GetSkin(skinId);

        _roleText.Text = $"角色：{role}";
        _skinText.Text = $"当前皮肤：{(skin?.Name ?? "默认")}（{(skin?.Id ?? "default")}）";

        _coinsText.Text = $"💰 金币：{user.Coins}";

        var affinity = AffinityService.Instance.GetAffinity(role);
        var level = AffinityService.Instance.GetLevel(affinity);
        var discountPct = (int)((1.0 - level.SkinDiscount) * 100);
        _affinityText.Text = $"❤️ 好感度：{affinity}/100（{level.Name}）" +
                             (discountPct > 0 ? $" · 皮肤 {discountPct}% 折扣" : "");

        var satiety = user.GetSatiety(role);
        var satStatus = satiety < 20 ? " ⚠ 饥饿" : (satiety > 80 ? " 😋 饱腹" : "");
        _satietyText.Text = $"🍖 饱食度：{satiety}/100{satStatus}";

        _levelHintText.Text = $"当前等级「{level.Name}」：{level.Description}\n" +
                              $"皮肤折扣：{level.SkinDiscount:F2}（{(discountPct > 0 ? $"省 {discountPct}%" : "无折扣")}）\n" +
                              $"AI 对话风格：{AffinityService.Instance.GetSystemPromptHint(role)}";

        RefreshSkinsList();
    }

    /// <summary>列出所有已解锁皮肤（按角色分组）。</summary>
    private void RefreshSkinsList()
    {
        _skinsPanel.Children.Clear();
        var user = UserService.Instance.CurrentUser;
        if (user == null) return;

        var allSkins = SkinService.Instance.GetAllSkins();
        var byRole = allSkins.GroupBy(s => s.Role);

        foreach (var g in byRole)
        {
            var roleHeader = new TextBlock
            {
                Text = $"【{g.Key}】",
                FontWeight = FontWeights.Bold,
                FontFamily = new FontFamily("Microsoft YaHei UI"),
                FontSize = 12,
                Margin = new Thickness(0, 4, 0, 2),
                Foreground = new SolidColorBrush(_theme.TextColor)
            };
            _skinsPanel.Children.Add(roleHeader);

            foreach (var s in g)
            {
                var unlocked = user.UnlockedSkins.Contains(s.Id);
                var isCurrent = user.GetCurrentSkin(g.Key) == s.Id;
                var marker = isCurrent ? " ✓当前" : (unlocked ? " ✓" : " 🔒");
                var line = new TextBlock
                {
                    Text = $"  · {s.Name}{marker}" + (unlocked ? "" : $"（{ConfigService.LoadPetManagement().Constants.SkinPrice} 金币）"),
                    FontFamily = new FontFamily("Microsoft YaHei UI"),
                    FontSize = 11,
                    Opacity = unlocked ? 1.0 : 0.6,
                    Margin = new Thickness(8, 0, 0, 0),
                    Foreground = new SolidColorBrush(_theme.TextColor)
                };
                _skinsPanel.Children.Add(line);
            }
        }

        var summary = new TextBlock
        {
            Text = $"\n共解锁 {user.UnlockedSkins.Count} / {allSkins.Count} 个皮肤",
            FontFamily = new FontFamily("Microsoft YaHei UI"),
            FontSize = 11,
            Opacity = 0.7,
            Margin = new Thickness(0, 4, 0, 0),
            Foreground = new SolidColorBrush(_theme.TextColor)
        };
        _skinsPanel.Children.Add(summary);
    }
}
