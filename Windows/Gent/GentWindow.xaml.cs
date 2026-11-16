using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DesktopGent.Models;
using DesktopGent.Services.Core;
using Hardcodet.Wpf.TaskbarNotification;
using Drawing = System.Drawing;

namespace DesktopGent.Windows;

/// <summary>
/// 桌面助手主窗口。
/// 角色外观来自 static/&lt;角色目录&gt;/theme.yaml 与子文件夹帧动画。
/// 目前仅保留两种动作：原地呼吸循环（01_idle_breath/）与随机扣鼻孔（02_idle_nose_pick/）。
/// 喂食/互动等操作只更新状态，不再播放响应动画。
/// </summary>
public partial class GentWindow : Window
{
    private const int BaseSize = 300;

    private readonly TalkWindow _talk;
    private readonly TodoWindow _todo;

    private GentConfig _config;
    private string _roleName = "默认角色";
    private string _roleDir = "";

    /// <summary>帧组：动作目录名 → 帧列表（按文件名排序）</summary>
    private readonly Dictionary<string, List<BitmapSource>> _frameSets = new();

    private string _idleAnim = "01_idle_breath";
    private string _randomAnim = "02_idle_nose_pick";
    private int _idleIndex;
    private int _randomIndex;
    private bool _inRandomAction;
    private double _scale = 0.4;

    private DispatcherTimer? _animTimer;
    private DispatcherTimer? _randomTimer;

    // 拖拽状态（增量法 + DPI 换算）
    private bool _isDragging;
    private Point _dragStartScreen;
    private double _dragStartLeft, _dragStartTop;

    // 系统托盘
    private TaskbarIcon? _trayIcon;
    private MenuItem? _miMute;

    // 助手管理窗口（单例）
    private GentManagerWindow? _gentManager;

    private static readonly Random _rng = new();

    // 当前帧像素缓存（逐像素点击穿透；BGRA）
    private byte[]? _framePixels;
    private int _frameWidth;
    private int _frameHeight;

    public GentWindow()
    {
        InitializeComponent();
        _config = ConfigService.Load();
        _talk = new TalkWindow();
        _todo = new TodoWindow();

        PositionToDefaultCorner();
        SetupTray();
        ContextMenu = BuildContextMenu();
        SetupHitTestHook();

        // 切换到配置/用户偏好中的当前角色
        Switch(_config.Role);

        Logger.Info("App", $"桌面助手启动，角色={_config.Role}，位置=({Left:F0},{Top:F0})");
    }

    private void PositionToDefaultCorner()
    {
        var work = SystemParameters.WorkArea;
        double w = _scale * BaseSize;
        double h = w * 1.25;
        Left = Math.Max(work.Left + 20, work.Right - w - 20);
        Top = Math.Max(work.Top + 20, work.Bottom - h - 20);
    }

    private void EnsureOnScreen()
    {
        var work = SystemParameters.WorkArea;
        if (Left < work.Left || Top < work.Top ||
            Left + Width > work.Right || Top + Height > work.Bottom ||
            Left > work.Right || Top > work.Bottom)
        {
            PositionToDefaultCorner();
            Logger.Info("Gent", $"窗口超出工作区，已重定位到 ({Left:F0},{Top:F0})");
        }
    }

    private void PositionNextToGent(Window child, int gap = 12)
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

    // ===================== 角色切换与资源加载 =====================

    /// <summary>切换角色：扫描角色目录下的动作子文件夹并启动动画。</summary>
    public void Switch(string name)
    {
        _animTimer?.Stop();
        _randomTimer?.Stop();

        _roleName = name;
        _config.Role = name;
        _scale = _config.GetScale(name);
        _roleDir = App.GetRoleDir(name);

        LoadFrameSets();
        ApplyWindowSize();
        StartIdleLoop();
        StartRandomTimer();
        EnsureOnScreen();
        SetTrayIconFromRole();

        _talk.ApplyTheme(name);
        _todo.ApplyTheme(name);
        ApplyContextMenuTheme(name);

        try { ConfigService.Save(_config); }
        catch (Exception ex) { Logger.Error("Gent", "切换角色后保存配置失败", ex); }

        Show();
        Logger.Info("Gent", $"切换角色: {name}，动画帧组={_frameSets.Count}，缩放={_scale:F2}");
    }

    /// <summary>
    /// 加载角色目录下每个子文件夹中的 PNG 作为独立帧组。
    /// 约定：01_idle_breath/ = 原地呼吸循环；02_idle_nose_pick/ = 随机扣鼻孔。
    /// </summary>
    private void LoadFrameSets()
    {
        _frameSets.Clear();
        _idleIndex = 0;
        _randomIndex = 0;
        _inRandomAction = false;

        if (!Directory.Exists(_roleDir))
        {
            Logger.Warn("Gent", $"角色目录不存在: {_roleDir}");
            _idleAnim = "";
            _randomAnim = "";
            return;
        }

        foreach (var dir in Directory.GetDirectories(_roleDir)
                     .OrderBy(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase))
        {
            var files = Directory.GetFiles(dir, "*.png")
                .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
                .ToList();

            var frames = new List<BitmapSource>();
            foreach (var file in files)
            {
                try { frames.Add(LoadFrozenBitmap(file)); }
                catch (Exception ex) { Logger.Warn("Gent", $"跳过帧文件 {file}: {ex.Message}"); }
            }
            if (frames.Count > 0)
                _frameSets[Path.GetFileName(dir)] = frames;
        }

        var keys = _frameSets.Keys.ToList();
        _idleAnim = keys.Contains("01_idle_breath")
            ? "01_idle_breath"
            : (keys.Count > 0 ? keys[0] : "");
        _randomAnim = keys.Contains("02_idle_nose_pick")
            ? "02_idle_nose_pick"
            : (keys.Count > 1 ? keys[1] : "");

        Logger.Info("Gent", $"加载角色帧：{string.Join(", ", keys)}");
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

    /// <summary>窗口尺寸按呼吸帧长宽比固定，所有帧用 Uniform 居中缩放。</summary>
    private void ApplyWindowSize()
    {
        if (_frameSets.TryGetValue(_idleAnim, out var frames) && frames.Count > 0)
        {
            var f = frames[0];
            var size = _scale * BaseSize;
            Width = Math.Max(40, size);
            Height = Math.Max(40, size * f.PixelHeight / (double)f.PixelWidth);
        }
    }

    // ===================== 帧显示与动画 =====================

    private void ShowFrame(List<BitmapSource>? frames, int index)
    {
        if (frames == null || frames.Count == 0) return;
        index = ((index % frames.Count) + frames.Count) % frames.Count;
        var frame = frames[index];
        GentImage.Source = frame;
        CacheFramePixels(frame);
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

    /// <summary>开启一个循环定时器，tick 为空闲/随机动作的业务逻辑。</summary>
    private void StartLoop(int intervalMs, Action tick)
    {
        _animTimer?.Stop();
        _animTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(Math.Max(30, intervalMs))
        };
        _animTimer.Tick += (_, _) => tick();
        _animTimer.Start();
    }

    /// <summary>原地呼吸循环：按帧间隔循环播放 01_idle_breath/ 中的帧。</summary>
    private void StartIdleLoop()
    {
        _inRandomAction = false;
        if (_frameSets.TryGetValue(_idleAnim, out var frames) && frames.Count > 0)
            ShowFrame(frames, 0);

        StartLoop(_config.GetFrameIntervalMs(_roleName), () =>
        {
            if (_inRandomAction) return;
            if (_frameSets.TryGetValue(_idleAnim, out var f) && f.Count > 1)
            {
                _idleIndex = (_idleIndex + 1) % f.Count;
                ShowFrame(f, _idleIndex);
            }
        });
    }

    /// <summary>随机动作计时器：以随机间隔触发一次扣鼻孔动画。</summary>
    private void StartRandomTimer()
    {
        _randomTimer?.Stop();
        var baseMs = Math.Max(3000, _config.RandomActionIntervalMs);
        var jitter = (_rng.NextDouble() - 0.5) * 0.4;
        var interval = Math.Max(2000, (int)(baseMs * (1.0 + jitter)));
        _randomTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(interval)
        };
        _randomTimer.Tick += (_, _) =>
        {
            PlayRandomAction();
            StartRandomTimer();
        };
        _randomTimer.Start();
    }

    /// <summary>播放一次随机动作（扣鼻孔），结束后回到原位呼吸循环。</summary>
    private void PlayRandomAction()
    {
        if (_inRandomAction) return;
        if (string.IsNullOrEmpty(_randomAnim)) return;
        if (!_frameSets.TryGetValue(_randomAnim, out var frames) || frames.Count == 0) return;

        _inRandomAction = true;
        _randomIndex = 0;
        ShowFrame(frames, 0);

        StartLoop(_config.GetFrameIntervalMs(_roleName), () =>
        {
            if (!_inRandomAction) return;
            _randomIndex++;
            if (_randomIndex >= frames.Count)
            {
                StartIdleLoop();
                return;
            }
            ShowFrame(frames, _randomIndex);
        });

        Logger.Info("Gent", $"随机动作播放：{_randomAnim}");
    }

    // ===================== 系统托盘 =====================

    private void SetupTray()
    {
        _trayIcon = new TaskbarIcon { ToolTipText = "桌面助手" };
        _trayIcon.ContextMenu = BuildTrayMenu();
    }

    private void SetTrayIconFromRole()
    {
        var iconPath = Path.Combine(_roleDir, "icon.png");
        if (!File.Exists(iconPath)) return;
        try
        {
            using var bmp = new Drawing.Bitmap(iconPath);
            var hicon = bmp.GetHicon();
            _trayIcon!.Icon = Drawing.Icon.FromHandle(hicon);
        }
        catch (Exception ex)
        {
            Logger.Warn("Tray", $"托盘图标加载失败 {iconPath}: {ex.Message}");
        }
    }

    private ContextMenu BuildTrayMenu()
    {
        var menu = new ContextMenu();

        // 角色子菜单：从扫描到的 static/ 角色动态生成
        var rolesMenu = new MenuItem { Header = "角色" };
        foreach (var kv in _config.Roles)
        {
            var roleName = kv.Key;
            var mi = new MenuItem { Header = roleName };
            if (roleName == _config.Role) mi.Header += " ✓";
            mi.Click += (_, _) =>
            {
                Logger.Info("UI", $"托盘菜单：切换角色 {roleName}");
                Switch(roleName);
                RefreshTrayMenuState();
            };
            rolesMenu.Items.Add(mi);
        }
        menu.Items.Add(rolesMenu);

        _miMute = new MenuItem { Header = App.IsMuted ? "取消静音" : "静音" };
        _miMute.Click += (_, _) => ToggleMute();
        menu.Items.Add(_miMute);

        var miShow = new MenuItem { Header = "显示" };
        miShow.Click += (_, _) =>
        {
            Logger.Info("UI", "托盘菜单：显示助手");
            EnsureOnScreen();
            Opacity = 1;
            Activate();
        };
        menu.Items.Add(miShow);

        var miSettings = new MenuItem { Header = "设置" };
        miSettings.Click += (_, _) =>
        {
            Logger.Info("UI", "托盘菜单：打开设置");
            try
            {
                var win = new SettingsWindow();
                win.ApplyTheme(_roleName);
                win.OnRoleImported = () =>
                {
                    ConfigService.InvalidateCache();
                    _config = ConfigService.Load();
                    _trayIcon!.ContextMenu = BuildTrayMenu();
                };
                win.ShowDialog();
            }
            catch (Exception ex)
            {
                Logger.Error("UI", "打开设置窗口失败", ex);
            }
        };
        menu.Items.Add(miSettings);

        var miQuit = new MenuItem { Header = "退出" };
        miQuit.Click += (_, _) =>
        {
            Logger.Info("UI", "托盘菜单：退出");
            Quit();
        };
        menu.Items.Add(miQuit);

        return menu;
    }

    private void RefreshTrayMenuState()
    {
        if (_trayIcon?.ContextMenu?.Items[0] is MenuItem rolesMenu)
        {
            foreach (var item in rolesMenu.Items)
            {
                if (item is MenuItem mi)
                {
                    var name = mi.Header!.ToString()!.Replace(" ✓", "");
                    mi.Header = name + (name == _config.Role ? " ✓" : "");
                }
            }
        }
        if (_miMute != null) _miMute.Header = App.IsMuted ? "取消静音" : "静音";
    }

    /// <summary>全局静音切换：仅影响语音等系统声音（已移除皮肤 BGM 逻辑）。</summary>
    private void ToggleMute()
    {
        App.IsMuted = !App.IsMuted;
        Logger.Info("UI", $"全局静音切换 -> {(App.IsMuted ? "静音" : "有声")}");
        RefreshTrayMenuState();
    }

    // ===================== 右键菜单（窗口级） =====================

    private ContextMenu BuildContextMenu()
    {
        var menu = new ContextMenu();

        var miTalk = new MenuItem { Header = "聊天" };
        miTalk.Click += (_, _) =>
        {
            Logger.Info("UI", "右键菜单：打开聊天");
            PositionNextToGent(_talk);
            _talk.Show();
            _talk.Activate();
        };

        var miTodo = new MenuItem { Header = "待办" };
        miTodo.Click += (_, _) =>
        {
            Logger.Info("UI", "右键菜单：打开待办");
            _todo.RefreshFromRepository();
            PositionNextToGent(_todo);
            _todo.Show();
            _todo.Activate();
        };

        var miManage = new MenuItem { Header = "助手管理" };
        miManage.Click += (_, _) =>
        {
            Logger.Info("UI", "右键菜单：打开助手管理");
            OpenGentManager();
        };

        var miHide = new MenuItem { Header = "隐藏" };
        miHide.Click += (_, _) =>
        {
            Logger.Info("UI", "右键菜单：隐藏助手");
            Opacity = 0;
        };

        var miQuit = new MenuItem { Header = "退出" };
        miQuit.Click += (_, _) =>
        {
            Logger.Info("UI", "右键菜单：退出");
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

    private void OpenGentManager()
    {
        if (_gentManager == null || !_gentManager.IsVisible)
        {
            _gentManager = new GentManagerWindow(this);
            PositionNextToGent(_gentManager);
            _gentManager.Closed += (_, _) => _gentManager = null;
            _gentManager.Show();
        }
        else
        {
            _gentManager.Activate();
        }
    }

    /// <summary>把角色主题应用到窗口右键菜单。</summary>
    private void ApplyContextMenuTheme(string role)
    {
        if (ContextMenu == null) return;
        var theme = _config.GetTheme(role);

        ContextMenu.Background = new SolidColorBrush(WithAlpha(theme.BackgroundColor, 0xF5));
        ContextMenu.BorderBrush = new SolidColorBrush(theme.PrimaryColor);
        ContextMenu.Padding = new Thickness(2);

        var style = new Style(typeof(MenuItem));
        style.Setters.Add(new Setter(MenuItem.ForegroundProperty, new SolidColorBrush(theme.TextColor)));
        style.Setters.Add(new Setter(MenuItem.BackgroundProperty, Brushes.Transparent));
        style.Setters.Add(new Setter(MenuItem.BorderBrushProperty, new SolidColorBrush(theme.PrimaryColor)));
        style.Setters.Add(new Setter(MenuItem.PaddingProperty, new Thickness(8, 4, 8, 4)));

        var hover = new Trigger { Property = MenuItem.IsHighlightedProperty, Value = true };
        hover.Setters.Add(new Setter(MenuItem.BackgroundProperty, new SolidColorBrush(theme.PrimaryColor)));
        hover.Setters.Add(new Setter(MenuItem.ForegroundProperty, new SolidColorBrush(Darken(theme.BackgroundColor, 0.85))));
        style.Triggers.Add(hover);

        ContextMenu.Resources[typeof(MenuItem)] = style;
        Logger.Info("UI", $"右键菜单应用主题：角色={role}，主色={theme.Primary}");
    }

    private static Color WithAlpha(Color c, double alpha)
        => Color.FromArgb((byte)(alpha * 255), c.R, c.G, c.B);

    private static Color Darken(Color c, double factor)
        => Color.FromRgb((byte)(c.R * factor), (byte)(c.G * factor), (byte)(c.B * factor));

    // ===================== 鼠标交互 =====================

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        // 双击触发抚摸：只更新好感度状态，不播放响应动画
        if (e.ClickCount >= 2)
        {
            Logger.Info("UI", "双击助手：触发抚摸");
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
        Logger.Info("Gent", $"开始拖拽，窗口=({_dragStartLeft:F0},{_dragStartTop:F0})");
        base.OnMouseLeftButtonDown(e);
    }

    private void QuickPat()
    {
        var result = InteractionService.Instance.Interact(_roleName, "pat");
        Logger.Info("Interact", $"双击抚摸：{result.Message}");
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
            Logger.Info("Gent", $"结束拖拽，落点=({Left:F0},{Top:F0})");
            _isDragging = false;
            ReleaseMouseCapture();
            Cursor = Cursors.Arrow;
        }
        base.OnMouseLeftButtonUp(e);
    }

    // ===================== 键盘 =====================

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Q && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            Logger.Info("UI", "快捷键 Ctrl+Q 退出");
            Quit();
            e.Handled = true;
            return;
        }
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0 && e.Key == Key.OemPlus)
        {
            _scale += 0.01;
            SaveScale();
            ApplyWindowSize();
            Logger.Info("Gent", $"放大，scale={_scale:F2}");
            e.Handled = true;
            return;
        }
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0 && e.Key == Key.OemMinus)
        {
            _scale = Math.Max(0.1, _scale - 0.01);
            SaveScale();
            ApplyWindowSize();
            Logger.Info("Gent", $"缩小，scale={_scale:F2}");
            e.Handled = true;
            return;
        }
        base.OnKeyDown(e);
    }

    private void SaveScale()
    {
        if (!_config.FrameScale.ContainsKey(_roleName))
            _config.FrameScale[_roleName] = new List<double> { _config.GetFrameIntervalMs(_roleName), _scale };
        else
            _config.FrameScale[_roleName][1] = _scale;
        try { ConfigService.Save(_config); }
        catch (Exception ex) { Logger.Error("Gent", "保存缩放配置失败", ex); }
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
        try { ConfigService.Save(_config); }
        catch (Exception ex) { Logger.Error("App", "保存配置失败", ex); }
        _talk.Close();
        _todo.ForceClose();
        _trayIcon?.Dispose();
        Close();
        Application.Current.Shutdown();
    }

    protected override void OnClosed(EventArgs e)
    {
        _animTimer?.Stop();
        _randomTimer?.Stop();
        _trayIcon?.Dispose();
        if (Application.Current.ShutdownMode == ShutdownMode.OnExplicitShutdown)
        {
            Application.Current.Shutdown();
        }
        base.OnClosed(e);
    }
}
