using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DesktopPetCSharp.Models;
using DesktopPetCSharp.Services;
using Hardcodet.Wpf.TaskbarNotification;
using Drawing = System.Drawing;

namespace DesktopPetCSharp.Windows;

/// <summary>
/// 桌面宠物主窗口。支持多角色（路飞/索隆），切换角色时像素图、BGM、配色同步切换。
/// 动画：精灵图切帧 + 程序化呼吸/漂浮 + 拖拽反应姿态。
/// 菜单：托盘菜单（人物切换/静音/显示/退出）；宠物右键菜单（聊天/待办/隐藏/退出）。
/// </summary>
public partial class PetWindow : Window
{
    private readonly PetConfig _config;
    private readonly AudioService _audio = new();
    private readonly TalkWindow _talk;
    private readonly TodoWindow _todo;

    private const int BaseSize = 300;

    private string _roleName = "路飞";
    private string _roleDir = "";          // 当前皮肤目录（static/<角色>/<皮肤>）
    private Models.Skin? _currentSkin;     // 当前装备的皮肤

    private List<BitmapSource> _frames = new();
    private enum AnimMode { None, SpriteSheet, MultiFile }
    private AnimMode _mode = AnimMode.None;

    private int _index = 0;
    private double _scale = 0.4;

    private DispatcherTimer? _timer;

    // 拖拽状态（增量法 + DPI 换算）
    private bool _isDragging;
    private Point _dragStartScreen;
    private double _dragStartLeft, _dragStartTop;

    // 系统托盘
    private TaskbarIcon? _trayIcon;
    private MenuItem? _miMute;

    // 宠物管理窗口（单例）
    private PetManagerWindow? _petManager;

    // P8: 双击抚摸检测（在 OnMouseLeftButtonDown 中判断 ClickCount）
    private DateTime _lastClickTime = DateTime.MinValue;
    private const int DoubleClickMs = 400;

    // 当前帧像素缓存（逐像素点击穿透命中测试；BGRA）
    private byte[]? _framePixels;
    private int _frameWidth;
    private int _frameHeight;

    public PetWindow()
    {
        InitializeComponent();
        _config = ConfigService.Load();
        _talk = new TalkWindow();
        _todo = new TodoWindow();

        PositionToDefaultCorner();
        SetupTray();
        ContextMenu = BuildContextMenu();
        SetupHitTestHook();
        StartProceduralAnimation();

        // 切换到配置中的角色（含加载像素图、播放 BGM、应用配色）
        Switch(_config.Role);

        Logger.Info("App", $"桌面宠物启动，角色={_config.Role}，模式={_mode}，位置=({Left:F0},{Top:F0})");
    }

    private void PositionToDefaultCorner()
    {
        var work = SystemParameters.WorkArea;
        double size = _config.GetScale(_config.Role) * BaseSize;
        Left = Math.Max(work.Left + 20, work.Right - size - 20);
        Top = Math.Max(work.Top + 20, work.Bottom - size - 20);
    }

    private void EnsureOnScreen()
    {
        var work = SystemParameters.WorkArea;
        if (Left < work.Left - Width || Left > work.Right || Top < work.Top - Height || Top > work.Bottom)
        {
            PositionToDefaultCorner();
            Logger.Info("Pet", $"窗口超出工作区，已重定位到 ({Left:F0},{Top:F0})");
        }
    }

    private void PositionNextToPet(Window child, int gap = 12)
    {
        var work = SystemParameters.WorkArea;
        double w = child.Width > 0 ? child.Width : 320;
        double h = child.Height > 0 ? child.Height : 320;
        double left = Left + Width + gap;
        if (left + w > work.Right) left = Left - w - gap;
        double top = Top;
        if (top + h > work.Bottom) top = work.Bottom - h - gap;
        if (top < work.Top) top = work.Top + gap;
        child.Left = Math.Max(work.Left + gap, left);
        child.Top = top;
    }

    // ===================== 资源加载 =====================

    /// <summary>切换角色：加载该角色当前皮肤，播放 BGM、应用配色到子窗口。</summary>
    private void Switch(string name)
    {
        _roleName = name;
        _config.Role = name;
        _scale = _config.GetScale(name);

        // 从 SkinService 获取当前皮肤（用户上次装备的或默认皮肤）
        _currentSkin = Services.SkinService.Instance.GetCurrentSkin(name);
        if (_currentSkin != null)
        {
            _roleDir = Services.SkinService.Instance.GetSkinDir(_currentSkin);
            // 皮肤主题覆盖角色主题，让所有 ApplyTheme 自动跟随皮肤
            _config.Themes[name] = _currentSkin.Theme;
        }
        else
        {
            _roleDir = App.GetRoleDir(name);  // 回退到角色根目录
        }

        LoadFrames();
        _index = 0;
        ShowFrame(0, true);
        SetTrayIconFromRole();
        SetupTimerForCurrentFrame();

        // 播放当前皮肤的背景音乐
        PlayRoleBgm();

        // 播放问候语音（从皮肤获取）
        if (_config.Audio && _currentSkin != null)
        {
            var greetPath = Services.SkinService.Instance.GetGreetingPath(_currentSkin);
            if (greetPath != null)
            {
                _audio.PlaySound(greetPath, 0.5f);
            }
        }

        // 应用配色到子窗口（皮肤主题优先，回退到角色主题）
        _talk.ApplyTheme(name);
        _todo.ApplyTheme(name);

        // 应用右键菜单主题
        ApplyContextMenuTheme(name);

        // 即时保存配置，记录当前角色（下次启动恢复上次使用的角色）
        try { ConfigService.Save(_config); } catch (Exception ex) { Logger.Error("Pet", "切换角色后保存配置失败", ex); }

        Show();
        Logger.Info("Pet", $"切换角色: {name}，皮肤={_currentSkin?.Id ?? "默认"}，帧数={_frames.Count}，模式={_mode}，缩放={_scale:F2}");
    }

    /// <summary>切换皮肤（不改变角色）：加载新皮肤的像素图、BGM、主题。</summary>
    public void SwitchSkin(string skinId)
    {
        var skin = Services.SkinService.Instance.GetSkin(skinId);
        if (skin == null || skin.Role != _roleName)
        {
            Logger.Warn("Pet", $"皮肤不存在或角色不匹配: {skinId}");
            return;
        }

        if (!Services.SkinService.Instance.IsUnlocked(skinId))
        {
            Logger.Warn("Pet", $"皮肤未解锁，无法切换: {skinId}");
            return;
        }

        _currentSkin = skin;
        _roleDir = Services.SkinService.Instance.GetSkinDir(skin);
        // 皮肤主题覆盖角色主题
        _config.Themes[_roleName] = skin.Theme;

        LoadFrames();
        _index = 0;
        ShowFrame(0, true);
        SetTrayIconFromRole();
        SetupTimerForCurrentFrame();

        PlayRoleBgm();

        // 皮肤主题应用到子窗口
        _talk.ApplyTheme(_roleName);
        _todo.ApplyTheme(_roleName);
        ApplyContextMenuTheme(_roleName);

        // 持久化当前皮肤
        Services.SkinService.Instance.Equip(skinId);

        Logger.Info("Pet", $"切换皮肤: {skinId}（角色={_roleName}）");
    }

    /// <summary>播放当前皮肤的 BGM。</summary>
    private void PlayRoleBgm()
    {
        if (!_config.BgMusic) return;
        if (_currentSkin == null)
        {
            Logger.Warn("Audio", $"无当前皮肤，跳过 BGM");
            return;
        }
        var bgmPath = Services.SkinService.Instance.GetBgmPath(_currentSkin);
        if (bgmPath != null)
        {
            _audio.PlayBgmLoop(bgmPath);
            Logger.Info("Audio", $"播放皮肤 BGM: {_currentSkin.Id}，路径={bgmPath}");
        }
        else
        {
            Logger.Warn("Audio", $"皮肤 {_currentSkin.Id} 无 BGM 文件");
        }
    }

    private void LoadFrames()
    {
        _frames.Clear();
        _mode = AnimMode.None;

        if (!Directory.Exists(_roleDir))
        {
            Logger.Warn("Pet", $"角色目录不存在: {_roleDir}");
            return;
        }

        var sheetPath = Path.Combine(_roleDir, _config.SpriteFile);
        if (File.Exists(sheetPath))
        {
            try
            {
                var sheet = LoadFrozenBitmap(sheetPath);
                int fw = sheet.PixelWidth / Math.Max(1, _config.SpriteCols);
                int fh = sheet.PixelHeight / Math.Max(1, _config.SpriteRows);
                for (int r = 0; r < _config.SpriteRows; r++)
                    for (int c = 0; c < _config.SpriteCols; c++)
                    {
                        var crop = new CroppedBitmap(sheet, new Int32Rect(c * fw, r * fh, fw, fh));
                        crop.Freeze();
                        _frames.Add(crop);
                    }
                _mode = AnimMode.SpriteSheet;
                Logger.Info("Pet", $"精灵图加载: {sheetPath}，单帧 {fw}×{fh}，共 {_frames.Count} 帧");
                return;
            }
            catch (Exception ex)
            {
                Logger.Error("Pet", $"精灵图加载失败，回退到多帧模式: {sheetPath}", ex);
            }
        }

        var files = Directory.GetFiles(_roleDir, "*.png")
            .Where(p => !string.Equals(Path.GetFileName(p), _config.SpriteFile, StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => Path.GetFileName(p))
            .ToList();
        foreach (var f in files)
        {
            try { _frames.Add(LoadFrozenBitmap(f)); }
            catch (Exception ex) { Logger.Warn("Pet", $"跳过帧文件 {f}: {ex.Message}"); }
        }
        if (_frames.Count > 0) _mode = AnimMode.MultiFile;
    }

    private static BitmapSource LoadFrozenBitmap(string path)
    {
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.UriSource = new Uri(path);
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    // ===================== 帧显示 =====================

    private void ShowFrame(int index, bool resizeWindow = false)
    {
        if (index < 0 || index >= _frames.Count) return;
        var frame = _frames[index];
        PetImage.Source = frame;
        CacheFramePixels(frame);

        int size = (int)(_scale * BaseSize);
        if (resizeWindow || Math.Abs(Width - size) > 0.5)
        {
            Width = size;
            Height = size;
        }
    }

    private void CacheFramePixels(BitmapSource src)
    {
        if (src.Format != PixelFormats.Bgra32)
        {
            src = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
            src.Freeze();
        }
        _frameWidth = src.PixelWidth;
        _frameHeight = src.PixelHeight;
        _framePixels = new byte[_frameWidth * _frameHeight * 4];
        src.CopyPixels(_framePixels, _frameWidth * 4, 0);
    }

    // ===================== 动画定时器 =====================

    private void SetupTimerForCurrentFrame()
    {
        _timer?.Stop();
        if (_mode == AnimMode.None || _frames.Count <= 1) { _timer = null; return; }

        int interval = GetCurrentFrameInterval();
        _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(interval) };
        _timer.Tick += (_, _) => AnimateFrame();
        _timer.Start();
    }

    private int GetCurrentFrameInterval()
    {
        if (_mode == AnimMode.SpriteSheet)
            return _index == 0 ? _config.BlinkIntervalMs : _config.GetFrameIntervalMs(_roleName);
        return _config.GetFrameIntervalMs(_roleName);
    }

    private void AnimateFrame()
    {
        if (_frames.Count == 0) return;
        if (_mode == AnimMode.SpriteSheet && _frames.Count >= 2)
            _index = (_index == 0) ? 1 : 0;
        else
            _index = (_index + 1) % _frames.Count;
        ShowFrame(_index);
        SetupTimerForCurrentFrame();
    }

    // ===================== 程序化动画（呼吸 + 漂浮）=====================

    private void StartProceduralAnimation()
    {
        var breath = new DoubleAnimation(1.0, 1.03, TimeSpan.FromSeconds(2.0))
        { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() };
        BreathScale.BeginAnimation(ScaleTransform.ScaleYProperty, breath);

        var bob = new DoubleAnimation(0, -3, TimeSpan.FromSeconds(2.0))
        { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() };
        BobTranslate.BeginAnimation(TranslateTransform.YProperty, bob);
    }

    private void EnterDragPose()
    {
        BreathScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        BobTranslate.BeginAnimation(TranslateTransform.YProperty, null);
        var sx = new DoubleAnimation(BreathScale.ScaleX, 0.96, TimeSpan.FromMilliseconds(120)) { EasingFunction = new SineEase() };
        var sy = new DoubleAnimation(BreathScale.ScaleY, 1.08, TimeSpan.FromMilliseconds(120)) { EasingFunction = new SineEase() };
        BreathScale.BeginAnimation(ScaleTransform.ScaleXProperty, sx);
        BreathScale.BeginAnimation(ScaleTransform.ScaleYProperty, sy);
        BobTranslate.Y = 0;
    }

    private void ExitDragPose()
    {
        BreathScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        BreathScale.ScaleX = 1;
        BreathScale.ScaleY = 1;
        StartProceduralAnimation();
    }

    // ===================== 系统托盘 =====================

    private void SetupTray()
    {
        _trayIcon = new TaskbarIcon { ToolTipText = "桌面宠物" };
        _trayIcon.ContextMenu = BuildTrayMenu();
    }

    private void SetTrayIconFromRole()
    {
        var candidates = new[]
        {
            Path.Combine(_roleDir, "icon.png"),
            Path.Combine(_roleDir, _config.SpriteFile),
        };
        foreach (var p in candidates)
        {
            if (File.Exists(p))
            {
                try
                {
                    using var bmp = new Drawing.Bitmap(p);
                    var hicon = bmp.GetHicon();
                    _trayIcon!.Icon = Drawing.Icon.FromHandle(hicon);
                    return;
                }
                catch (Exception ex) { Logger.Warn("Tray", $"托盘图标加载失败 {p}: {ex.Message}"); }
            }
        }
    }

    private ContextMenu BuildTrayMenu()
    {
        var menu = new ContextMenu();

        // 人物子菜单：从配置的 roles 动态生成
        var petsMenu = new MenuItem { Header = "人物" };
        foreach (var kv in _config.Roles)
        {
            var roleName = kv.Key;
            var mi = new MenuItem { Header = roleName };
            if (roleName == _config.Role) mi.Header += "～";
            mi.Click += (_, _) =>
            {
                Logger.Info("UI", $"托盘菜单：切换人物-{roleName}");
                Switch(roleName);
                RefreshTrayMenuState();
            };
            petsMenu.Items.Add(mi);
        }
        menu.Items.Add(petsMenu);

        // 静音（只保留在托盘菜单）
        _miMute = new MenuItem { Header = App.IsMuted ? "取消静音" : "静音" };
        _miMute.Click += (_, _) => ToggleMute();
        menu.Items.Add(_miMute);

        var miShow = new MenuItem { Header = "显示" };
        miShow.Click += (_, _) =>
        {
            Logger.Info("UI", "托盘菜单：显示宠物");
            EnsureOnScreen();
            Opacity = 1;
            Activate();
        };
        menu.Items.Add(miShow);

        var miQuit = new MenuItem { Header = "退出" };
        miQuit.Click += (_, _) => { Logger.Info("UI", "托盘菜单：退出"); Quit(); };
        menu.Items.Add(miQuit);

        return menu;
    }

    private void RefreshTrayMenuState()
    {
        // 更新人物子菜单的"～"标记
        if (_trayIcon?.ContextMenu?.Items[0] is MenuItem petsMenu)
        {
            foreach (var item in petsMenu.Items)
            {
                if (item is MenuItem mi)
                {
                    var name = mi.Header!.ToString().Replace("～", "");
                    mi.Header = name + (name == _config.Role ? "～" : "");
                }
            }
        }
        if (_miMute != null) _miMute.Header = App.IsMuted ? "取消静音" : "静音";
    }

    /// <summary>全局静音切换：静音时停止所有声音；取消静音时恢复当前角色 BGM。</summary>
    private void ToggleMute()
    {
        App.IsMuted = !App.IsMuted;
        Logger.Info("UI", $"全局静音切换 -> {(App.IsMuted ? "静音" : "有声")}");
        if (App.IsMuted)
        {
            _audio.StopBgm();
            _audio.StopSounds();
        }
        else
        {
            PlayRoleBgm();
        }
        RefreshTrayMenuState();
    }

    // ===================== 右键菜单（窗口级）=====================

    private ContextMenu BuildContextMenu()
    {
        var menu = new ContextMenu();

        var miTalk = new MenuItem { Header = "聊天" };
        miTalk.Click += (_, _) =>
        {
            Logger.Info("UI", "右键菜单：打开聊天");
            PositionNextToPet(_talk);
            _talk.Show();
            _talk.Activate();
        };

        var miTodo = new MenuItem { Header = "待办" };
        miTodo.Click += (_, _) =>
        {
            Logger.Info("UI", "右键菜单：打开待办");
            _todo.RefreshFromRepository();
            PositionNextToPet(_todo);
            _todo.Show();
            _todo.Activate();
        };

        // P5: 新增「宠物管理」入口（商店/互动/喂食/状态 统一入口）
        var miManage = new MenuItem { Header = "宠物管理" };
        miManage.Click += (_, _) =>
        {
            Logger.Info("UI", "右键菜单：打开宠物管理");
            OpenPetManager();
        };

        var miHide = new MenuItem { Header = "隐藏" };
        miHide.Click += (_, _) =>
        {
            Logger.Info("UI", "右键菜单：隐藏宠物");
            Opacity = 0;
        };

        var miQuit = new MenuItem { Header = "退出" };
        miQuit.Click += (_, _) =>
        {
            Logger.Info("UI", "右键菜单：退出");
            // 播放退出语音（从当前皮肤获取）
            if (_currentSkin != null)
            {
                var farewellPath = Services.SkinService.Instance.GetFarewellPath(_currentSkin);
                if (farewellPath != null) _audio.PlaySound(farewellPath, 0.5f);
            }
            Quit();
        };

        menu.Items.Add(miTalk);
        menu.Items.Add(miTodo);
        menu.Items.Add(new Separator());
        menu.Items.Add(miManage);
        menu.Items.Add(new Separator());
        menu.Items.Add(miHide);
        menu.Items.Add(miQuit);
        return menu;
    }

    /// <summary>打开宠物管理窗口（单例，重复点击聚焦）。</summary>
    private void OpenPetManager()
    {
        if (_petManager == null || !_petManager.IsVisible)
        {
            _petManager = new PetManagerWindow(this);
            PositionNextToPet(_petManager);
            _petManager.Closed += (_, _) => _petManager = null;
            _petManager.Show();
        }
        else
        {
            _petManager.Activate();
        }
    }

    /// <summary>
    /// 应用角色配色主题到右键菜单：菜单背景、边框、菜单项文字色、悬停高亮色。
    /// 由 Switch() 在切换角色时调用，实现"右键菜单风格跟随角色"。
    /// </summary>
    private void ApplyContextMenuTheme(string role)
    {
        if (ContextMenu == null) return;
        var theme = _config.GetTheme(role);

        // 菜单容器：背景色 + 主色边框
        ContextMenu.Background = new SolidColorBrush(WithAlpha(theme.BackgroundColor, 0xF5));
        ContextMenu.BorderBrush = new SolidColorBrush(theme.PrimaryColor);
        ContextMenu.Padding = new Thickness(2);

        // 通过 Resources 注入 MenuItem 样式（所有子项继承，含子菜单）
        var style = new Style(typeof(MenuItem));
        style.Setters.Add(new Setter(MenuItem.ForegroundProperty, new SolidColorBrush(theme.TextColor)));
        style.Setters.Add(new Setter(MenuItem.BackgroundProperty, Brushes.Transparent));
        style.Setters.Add(new Setter(MenuItem.BorderBrushProperty, new SolidColorBrush(theme.PrimaryColor)));
        style.Setters.Add(new Setter(MenuItem.PaddingProperty, new Thickness(8, 4, 8, 4)));

        // 悬停高亮：主色背景 + 深色文字
        var hover = new Trigger { Property = MenuItem.IsHighlightedProperty, Value = true };
        hover.Setters.Add(new Setter(MenuItem.BackgroundProperty, new SolidColorBrush(theme.PrimaryColor)));
        hover.Setters.Add(new Setter(MenuItem.ForegroundProperty, new SolidColorBrush(Darken(theme.BackgroundColor, 0.85))));
        style.Triggers.Add(hover);

        ContextMenu.Resources[typeof(MenuItem)] = style;
        Logger.Info("UI", $"右键菜单应用主题：角色={role}，主色={theme.Primary}");
    }

    /// <summary>给颜色叠加 alpha 通道（0~1），保留 RGB。</summary>
    private static Color WithAlpha(Color c, double alpha)
        => Color.FromArgb((byte)(alpha * 255), c.R, c.G, c.B);

    /// <summary>把颜色按系数变暗（0~1，越小越暗）。</summary>
    private static Color Darken(Color c, double factor)
        => Color.FromRgb((byte)(c.R * factor), (byte)(c.G * factor), (byte)(c.B * factor));

    // ===================== 鼠标交互 =====================

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        // P8: 双击触发抚摸（不进入拖拽）
        if (e.ClickCount >= 2)
        {
            Logger.Info("UI", "双击宠物：触发抚摸");
            QuickPat();
            e.Handled = true;
            return;
        }

        _isDragging = true;
        _dragStartScreen = PointToScreen(e.GetPosition(this));
        _dragStartLeft = Left;
        _dragStartTop = Top;
        CaptureMouse();
        Cursor = Cursors.SizeAll;
        EnterDragPose();
        Logger.Info("Pet", $"开始拖拽，窗口=({_dragStartLeft:F0},{_dragStartTop:F0})");
        base.OnMouseLeftButtonDown(e);
    }

    /// <summary>双击宠物触发的快速抚摸：调用 InteractionService 并播放反应动画。</summary>
    private void QuickPat()
    {
        var result = Services.InteractionService.Instance.Interact(_roleName, "pat");
        if (result.Success) PlayPatReaction();
        Logger.Info("Interact", $"双击抚摸：{result.Message}");
    }

    /// <summary>抚摸反应动画：短暂向上轻跳 + 缩放反馈。</summary>
    public void PlayPatReaction()
    {
        // 停止呼吸/漂浮的当前动画，避免冲突
        BreathScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        BobTranslate.BeginAnimation(TranslateTransform.YProperty, null);

        // 缩放反馈：先稍微压扁再回弹（被摸头的感觉）
        var sy = new DoubleAnimation(0.92, 1.05, TimeSpan.FromMilliseconds(180))
        {
            AutoReverse = true,
            EasingFunction = new SineEase()
        };
        sy.Completed += (_, _) => StartProceduralAnimation();
        BreathScale.BeginAnimation(ScaleTransform.ScaleYProperty, sy);

        // 轻跳：向上 6px 再回落
        var jump = new DoubleAnimation(0, -6, TimeSpan.FromMilliseconds(180))
        {
            AutoReverse = true,
            EasingFunction = new SineEase()
        };
        BobTranslate.BeginAnimation(TranslateTransform.YProperty, jump);
    }

    /// <summary>击掌反应动画：横向晃动 + 轻微放大。</summary>
    public void PlayHighfiveReaction()
    {
        BreathScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        BreathScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        BobTranslate.BeginAnimation(TranslateTransform.YProperty, null);

        // 横向晃动：左右轻摇
        var sx = new DoubleAnimation(1.0, 1.08, TimeSpan.FromMilliseconds(150))
        {
            AutoReverse = true,
            EasingFunction = new SineEase()
        };
        sx.Completed += (_, _) => StartProceduralAnimation();
        BreathScale.BeginAnimation(ScaleTransform.ScaleXProperty, sx);

        // 轻微放大
        var sy = new DoubleAnimation(1.0, 1.05, TimeSpan.FromMilliseconds(150))
        {
            AutoReverse = true,
            EasingFunction = new SineEase()
        };
        BreathScale.BeginAnimation(ScaleTransform.ScaleYProperty, sy);
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
        if (_isDragging)
        {
            Logger.Info("Pet", $"结束拖拽，落点=({Left:F0},{Top:F0})");
            _isDragging = false;
            ReleaseMouseCapture();
            Cursor = Cursors.Arrow;
            ExitDragPose();
        }
        base.OnMouseLeftButtonUp(e);
    }

    // ===================== 键盘 =====================

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Q && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            Logger.Info("UI", "快捷键 Ctrl+Q 退出");
            Quit(); e.Handled = true; return;
        }
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0 && e.Key == Key.OemPlus)
        {
            _scale += 0.01;
            if (!_config.FrameScale.ContainsKey(_roleName)) _config.FrameScale[_roleName] = new List<double> { 150, _scale };
            else _config.FrameScale[_roleName][1] = _scale;
            ShowFrame(_index, true);
            Logger.Info("Pet", $"放大，scale={_scale:F2}");
            e.Handled = true; return;
        }
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0 && e.Key == Key.OemMinus)
        {
            _scale -= 0.01;
            if (!_config.FrameScale.ContainsKey(_roleName)) _config.FrameScale[_roleName] = new List<double> { 150, _scale };
            else _config.FrameScale[_roleName][1] = _scale;
            ShowFrame(_index, true);
            Logger.Info("Pet", $"缩小，scale={_scale:F2}");
            e.Handled = true; return;
        }
        base.OnKeyDown(e);
    }

    // ===================== 逐像素点击穿透 =====================

    private void SetupHitTestHook()
    {
        SourceInitialized += (_, _) =>
        {
            var s = PresentationSource.FromVisual(this) as HwndSource;
            s?.AddHook(WndProc);
        };
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);
    private const int VK_RBUTTON = 0x02;

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_NCHITTEST = 0x0084;
        const int HTTRANSPARENT = -1;

        if (msg == WM_NCHITTEST && _framePixels != null && !_isDragging)
        {
            bool rightDown = (GetAsyncKeyState(VK_RBUTTON) & 0x8000) != 0;
            if (!rightDown)
            {
                var ct = PresentationSource.FromVisual(this)?.CompositionTarget;
                double dpiX = ct?.TransformToDevice.M11 ?? 1.0;
                double dpiY = ct?.TransformToDevice.M22 ?? 1.0;
                int x = (short)(lParam.ToInt32() & 0xFFFF);
                int y = (short)((lParam.ToInt32() >> 16) & 0xFFFF);
                double relX = x / dpiX - Left;
                double relY = y / dpiY - Top;
                int px = (int)(relX / Math.Max(1, Width) * _frameWidth);
                int py = (int)(relY / Math.Max(1, Height) * _frameHeight);
                if (px >= 0 && py >= 0 && px < _frameWidth && py < _frameHeight)
                {
                    int offset = (py * _frameWidth + px) * 4;
                    byte alpha = _framePixels[offset + 3];
                    if (alpha < 10) { handled = true; return (IntPtr)HTTRANSPARENT; }
                }
            }
        }
        return IntPtr.Zero;
    }

    // ===================== 退出 =====================

    private void Quit()
    {
        Logger.Info("App", "应用退出");
        try { ConfigService.Save(_config); } catch (Exception ex) { Logger.Error("App", "保存配置失败", ex); }
        _talk.Close();
        _todo.ForceClose();
        _audio.Dispose();
        _trayIcon?.Dispose();
        Close();
        Application.Current.Shutdown();
    }

    protected override void OnClosed(EventArgs e)
    {
        _timer?.Stop();
        _audio.Dispose();
        _trayIcon?.Dispose();
        // ShutdownMode=OnExplicitShutdown，需确保窗口关闭后应用退出
        if (Application.Current.ShutdownMode == ShutdownMode.OnExplicitShutdown)
        {
            Application.Current.Shutdown();
        }
        base.OnClosed(e);
    }
}
