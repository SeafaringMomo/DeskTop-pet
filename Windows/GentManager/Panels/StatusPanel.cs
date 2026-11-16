using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DesktopGent.Models;
using DesktopGent.Services.Core;

namespace DesktopGent.Windows.GentManager.Panels;

/// <summary>
/// 状态面板：展示当前角色的经济与养成状态。
/// 皮肤系统已取消，此处仅展示金币、好感度、饱食度与等级影响。
/// </summary>
public class StatusPanel : UserControl, IGentManagerPanel
{
    private readonly GentWindow _gent;
    private RoleTheme _theme = RoleTheme.DefaultRole;

    private readonly TextBlock _roleText = new();
    private readonly TextBlock _coinsText = new();
    private readonly TextBlock _affinityText = new();
    private readonly TextBlock _satietyText = new();
    private readonly TextBlock _levelHintText = new();

    public StatusPanel(GentWindow gent)
    {
        _gent = gent;
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
        root.Children.Add(MakeTitle("📊 助手状态总览"));
        root.Children.Add(MakeSection("🧭 当前角色", new[] { _roleText }));
        root.Children.Add(MakeSection("💰 经济与状态", new[] { _coinsText, _affinityText, _satietyText }));
        root.Children.Add(MakeSection("✨ 等级影响", new[] { _levelHintText }));

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
    }

    public void Refresh()
    {
        var user = UserService.Instance.CurrentUser;
        if (user == null) return;

        var role = ConfigService.Load().Role;
        _roleText.Text = $"角色：{role}";
        _coinsText.Text = $"💰 金币：{user.Coins}";

        var affinity = AffinityService.Instance.GetAffinity(role);
        var level = AffinityService.Instance.GetLevel(affinity);
        _affinityText.Text = $"❤️ 好感度：{affinity}/100（{level.Name}）";

        var satiety = user.GetSatiety(role);
        var satStatus = satiety < 20 ? " 🆘 饥饿" : satiety > 80 ? " 😋 饱腹" : "";
        _satietyText.Text = $"🍖 饱食度：{satiety}/100{satStatus}";

        _levelHintText.Text = $"当前等级「{level.Name}」：{level.Description}\n" +
                              $"AI 对话风格：{AffinityService.Instance.GetSystemPromptHint(role)}";
    }
}
