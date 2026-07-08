using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using DesktopPetCSharp.Models;
using DesktopPetCSharp.Services;
using DesktopPetCSharp.Services.AI;
using DesktopPetCSharp.Services.AI.Providers;

namespace DesktopPetCSharp.Windows;

/// <summary>
/// 模型设置窗口（P7 增强）。支持：
/// - 列出所有已注册 AI 提供方，切换默认
/// - 新增/编辑/删除 provider（编辑 API_KEY、BaseUrl、Model 等）
/// - 修改保存到 config/secrets.yaml，下次启动仍生效
/// - 配色跟随角色主题
/// </summary>
public partial class ModelSettingsWindow : Window
{
    /// <summary>列表项视图模型，包装提供方信息。</summary>
    public class ProviderItem
    {
        public string Name { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string Description { get; set; } = "";
    }

    private string _roleName = "路飞";

    public ModelSettingsWindow()
    {
        InitializeComponent();
        LoadProviders();
    }

    /// <summary>
    /// 应用角色配色主题。由 LauncherWindow / TalkWindow 调用。
    /// </summary>
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

            CurrentLabel.Foreground = new SolidColorBrush(theme.TextColor);

            ProviderList.Foreground = new SolidColorBrush(theme.TextColor);

            AddButton.Background = new SolidColorBrush(theme.SurfaceColor);
            AddButton.Foreground = new SolidColorBrush(theme.TextColor);
            AddButton.BorderBrush = new SolidColorBrush(theme.PrimaryColor);

            EditButton.Background = new SolidColorBrush(theme.SurfaceColor);
            EditButton.Foreground = new SolidColorBrush(theme.TextColor);
            EditButton.BorderBrush = new SolidColorBrush(theme.PrimaryColor);

            DeleteButton.Background = new SolidColorBrush(theme.AccentColor);
            DeleteButton.Foreground = new SolidColorBrush(theme.TextColor);

            ApplyButton.Background = new SolidColorBrush(theme.PrimaryColor);
            ApplyButton.Foreground = darkText;
            ApplyButton.BorderBrush = darkText;
        }
        catch { /* 容错 */ }
    }

    private static Color WithAlpha(Color c, double alpha)
        => Color.FromArgb((byte)(alpha * 255), c.R, c.G, c.B);

    private static Color Darken(Color c, double factor)
        => Color.FromRgb((byte)(c.R * factor), (byte)(c.G * factor), (byte)(c.B * factor));

    private void LoadProviders()
    {
        var registry = AiProviderRegistry.Instance;
        var items = new List<ProviderItem>();
        foreach (var name in registry.Names)
        {
            var p = registry.Get(name);
            items.Add(new ProviderItem
            {
                Name = name,
                DisplayName = name + (p?.SupportsToolCalling == true ? "  (支持工具调用)" : "  (纯对话)"),
                Description = Describe(p)
            });
        }
        ProviderList.ItemsSource = items;
        CurrentLabel.Text = $"当前默认提供方：{registry.DefaultName ?? "(无)"}";

        // 默认选中当前默认提供方
        var cur = items.FirstOrDefault(x => x.Name == registry.DefaultName);
        if (cur != null) ProviderList.SelectedItem = cur;
    }

    private static string Describe(IAiProvider? p)
    {
        if (p == null) return "未注册";
        if (p is OpenAiCompatibleProvider oai)
            return $"OpenAI 兼容：{oai.BaseUrl}，模型={oai.Model}";
        if (p is LocalKleeProvider)
            return "本地占位实现：关键词匹配，无需联网，主角名跟随配置";
        return p.GetType().Name;
    }

    private void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        if (ProviderList.SelectedItem is not ProviderItem item)
        {
            MessageBox.Show(this, "请先选择一个提供方。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var ok = AiProviderRegistry.Instance.TrySetDefault(item.Name);
        if (ok)
        {
            Logger.Info("AI", $"切换默认 AI 提供方 -> {item.Name}");
            CurrentLabel.Text = $"当前默认提供方：{item.Name}";
            // 持久化到用户档案
            try { Services.UserService.Instance.SaveCurrentProfile(); } catch { }
            // 同步 secrets.yaml 的默认 provider（如有需要可扩展）
            MessageBox.Show(this, $"已切换为 {item.Name}", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show(this, $"切换失败：{item.Name} 不存在", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ===== 新增/编辑/删除 provider =====

    private void AddButton_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new ProviderEditDialog(this, "新增 AI 提供方", null);
        if (dlg.ShowDialog() != true) return;
        var cfg = dlg.Result;
        if (cfg == null || string.IsNullOrWhiteSpace(cfg.Name)) return;

        // 保存到 secrets.yaml
        var secrets = ConfigService.LoadSecrets();
        if (secrets.FindAiProvider(cfg.Name) != null)
        {
            MessageBox.Show(this, $"提供方「{cfg.Name}」已存在。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        secrets.AiProviders.Add(cfg);
        ConfigService.SaveSecrets(secrets);

        // 注册到运行时注册表
        RegisterOneProvider(cfg);
        LoadProviders();
        Logger.Info("AI", $"新增 AI 提供方：{cfg.Name}");
    }

    private void EditButton_Click(object sender, RoutedEventArgs e)
    {
        if (ProviderList.SelectedItem is not ProviderItem item) return;

        // local-klee 不允许编辑（内置）
        if (item.Name == "local-klee")
        {
            MessageBox.Show(this, "local-klee 是内置占位实现，不可编辑。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var secrets = ConfigService.LoadSecrets();
        var cfg = secrets.FindAiProvider(item.Name);
        if (cfg == null)
        {
            MessageBox.Show(this, $"找不到 {item.Name} 的配置。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var dlg = new ProviderEditDialog(this, $"编辑 {cfg.Name}", cfg);
        if (dlg.ShowDialog() != true) return;
        var updated = dlg.Result;
        if (updated == null) return;

        // 更新 secrets.yaml
        var idx = secrets.AiProviders.IndexOf(cfg);
        if (idx >= 0) secrets.AiProviders[idx] = updated;
        ConfigService.SaveSecrets(secrets);

        // 重新注册（同名覆盖）
        RegisterOneProvider(updated);
        LoadProviders();
        Logger.Info("AI", $"编辑 AI 提供方：{updated.Name}");
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (ProviderList.SelectedItem is not ProviderItem item) return;
        if (item.Name == "local-klee")
        {
            MessageBox.Show(this, "local-klee 是内置兜底实现，不可删除。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var ok = MessageBox.Show(this, $"确定删除提供方「{item.Name}」？", "确认", MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (ok != MessageBoxResult.OK) return;

        // 从 secrets.yaml 删除
        var secrets = ConfigService.LoadSecrets();
        var cfg = secrets.FindAiProvider(item.Name);
        if (cfg != null) secrets.AiProviders.Remove(cfg);
        ConfigService.SaveSecrets(secrets);

        // 从运行时注册表删除
        AiProviderRegistry.Instance.Unregister(item.Name);
        // 若删除的是当前默认，回退到第一个
        if (AiProviderRegistry.Instance.DefaultName == null)
        {
            var firstName = AiProviderRegistry.Instance.Names.FirstOrDefault();
            if (firstName != null) AiProviderRegistry.Instance.TrySetDefault(firstName);
        }
        LoadProviders();
        Logger.Info("AI", $"删除 AI 提供方：{item.Name}");
    }

    /// <summary>把单个 provider 配置注册到运行时注册表。</summary>
    private static void RegisterOneProvider(AiProviderConfig p)
    {
        if (p.Type == "local")
        {
            AiProviderRegistry.Instance.Register(new LocalKleeProvider());
        }
        else if (p.Type == "openai-compatible")
        {
            AiProviderRegistry.Instance.Register(new OpenAiCompatibleProvider
            {
                Name = p.Name,
                BaseUrl = p.BaseUrl,
                ApiKey = p.ApiKey,
                Model = p.Model
            });
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    // ===== 标题栏拖拽 =====
    private bool _isDragging;
    private Point _dragStartScreen;
    private double _dragStartLeft, _dragStartTop;

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _isDragging = true;
        _dragStartScreen = PointToScreen(e.GetPosition(this));
        _dragStartLeft = Left;
        _dragStartTop = Top;
        CaptureMouse();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_isDragging)
        {
            var now = PointToScreen(e.GetPosition(this));
            var ct = PresentationSource.FromVisual(this)?.CompositionTarget;
            double dpiX = ct?.TransformFromDevice.M11 ?? 1.0;
            double dpiY = ct?.TransformFromDevice.M22 ?? 1.0;
            Left = _dragStartLeft + (now.X - _dragStartScreen.X) * dpiX;
            Top = _dragStartTop + (now.Y - _dragStartScreen.Y) * dpiY;
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        _isDragging = false;
        ReleaseMouseCapture();
        base.OnMouseLeftButtonUp(e);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
    }
}

/// <summary>
/// AI 提供方编辑对话框（新增/编辑共用）。
/// 字段：名称、类型、BaseUrl、API_KEY（PasswordBox）、模型、描述、启用。
/// </summary>
public class ProviderEditDialog : Window
{
    public AiProviderConfig? Result { get; private set; }

    public ProviderEditDialog(Window owner, string title, AiProviderConfig? existing)
    {
        Title = title;
        Width = 420;
        Height = 360;
        Owner = owner;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        WindowStyle = WindowStyle.SingleBorderWindow;

        var panel = new System.Windows.Controls.StackPanel { Margin = new Thickness(12) };

        var nameBox = LabeledTextBox(panel, "名称：", existing?.Name ?? "", "如 deepseek / openai");
        var typeBox = LabeledTextBox(panel, "类型：", existing?.Type ?? "openai-compatible", "local / openai-compatible");
        var baseUrlBox = LabeledTextBox(panel, "Base URL：", existing?.BaseUrl ?? "https://api.openai.com/v1", "");
        var modelBox = LabeledTextBox(panel, "模型：", existing?.Model ?? "gpt-4o-mini", "");

        var keyLabel = new System.Windows.Controls.TextBlock { Text = "API Key：", Margin = new Thickness(0, 6, 0, 2), FontSize = 12 };
        var keyBox = new System.Windows.Controls.PasswordBox
        {
            Password = existing?.ApiKey ?? "",
            FontSize = 13,
            Margin = new Thickness(0, 0, 0, 12)
        };
        panel.Children.Add(keyLabel);
        panel.Children.Add(keyBox);

        var descBox = LabeledTextBox(panel, "描述：", existing?.Description ?? "", "（可选）");

        var btnPanel = new System.Windows.Controls.StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };
        var okBtn = new System.Windows.Controls.Button { Content = "确定", Width = 72, Margin = new Thickness(4, 0, 0, 0), IsDefault = true };
        var cancelBtn = new System.Windows.Controls.Button { Content = "取消", Width = 72, Margin = new Thickness(4, 0, 0, 0), IsCancel = true };
        okBtn.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(nameBox.Text))
            {
                MessageBox.Show(this, "名称不能为空。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            Result = new AiProviderConfig
            {
                Name = nameBox.Text.Trim(),
                Type = string.IsNullOrWhiteSpace(typeBox.Text) ? "openai-compatible" : typeBox.Text.Trim(),
                BaseUrl = baseUrlBox.Text.Trim(),
                ApiKey = keyBox.Password,
                Model = modelBox.Text.Trim(),
                Description = descBox.Text.Trim(),
                Enabled = true
            };
            DialogResult = true;
            Close();
        };
        btnPanel.Children.Add(okBtn);
        btnPanel.Children.Add(cancelBtn);
        panel.Children.Add(btnPanel);

        Content = panel;
    }

    private static System.Windows.Controls.TextBox LabeledTextBox(
        System.Windows.Controls.Panel panel, string label, string value, string placeholder)
    {
        var lbl = new System.Windows.Controls.TextBlock { Text = label, Margin = new Thickness(0, 6, 0, 2), FontSize = 12 };
        var box = new System.Windows.Controls.TextBox
        {
            Text = value,
            FontSize = 13,
            Margin = new Thickness(0, 0, 0, 8)
        };
        panel.Children.Add(lbl);
        panel.Children.Add(box);
        return box;
    }
}
