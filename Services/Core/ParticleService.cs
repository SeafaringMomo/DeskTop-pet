using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using DesktopPetCSharp.Models;

namespace DesktopPetCSharp.Services;

/// <summary>
/// 粒子服务（P4）：在 PetWindow 的 ParticleLayer Canvas 上生成、动画、清理粒子。
///
/// 粒子用于增强动作反馈（抚摸出爱心、击掌出 ✨、喂食喜欢出 ❤、讨厌出 💧 等）。
/// 粒子类型在 config/animations.yaml 顶层 particles 段定义：
///   symbol（符号）/ font_size / color / rise_speed / drift / lifetime_ms
///
/// 粒子生命周期：
/// 1. Spawn(type, count, offsetX, offsetY)：从 PetImage 顶部偏移位置生成 count 个粒子
/// 2. 每个粒子用 TextBlock 渲染，添加到 ParticleLayer
/// 3. 同时启动两条动画：
///    - 上升动画：Y 从起始位置向上移动 rise_speed * lifetime/1000 像素
///    - 透明度动画：1.0 → 0.0（淡出）
/// 4. 动画结束后从 Canvas 移除粒子
///
/// 性能：粒子数量小（每次互动 3-8 个），不需要对象池。
/// 线程：必须在 UI 线程调用（DispatcherTimer 自动在 UI 线程）。
/// </summary>
public sealed class ParticleService
{
    public static ParticleService Instance { get; } = new();

    private Canvas? _layer;
    private static readonly Random _rng = new();

    private ParticleService() { }

    /// <summary>绑定粒子渲染层（PetWindow.ParticleLayer）。</summary>
    public void Attach(Canvas layer)
    {
        _layer = layer;
    }

    /// <summary>解绑渲染层（窗口关闭时调用）。</summary>
    public void Detach()
    {
        _layer = null;
    }

    /// <summary>生成粒子。
    /// <param name="type">粒子类型（heart/sweat/zzz/question/exclamation/sparkle 等，对应 animations.yaml.particles 段）</param>
    /// <param name="count">粒子数量</param>
    /// <param name="offsetX">起始 X 偏移（相对 PetImage 中心，像素）</param>
    /// <param name="offsetY">起始 Y 偏移（相对 PetImage 顶部，像素，负值向上）</param>
    public void Spawn(string type, int count, int offsetX, int offsetY)
    {
        if (_layer == null || count <= 0) return;
        if (!TryGetParticleDef(type, out var def)) return;

        // PetImage 中心点（相对 Canvas）
        double centerX = _layer.ActualWidth > 0 ? _layer.ActualWidth / 2.0 : 120;
        double topY = 0;

        for (int i = 0; i < count; i++)
        {
            SpawnOne(def, centerX + offsetX, topY + offsetY);
        }

        Logger.Info("Particle", $"生成粒子 {type} ×{count} @ ({offsetX},{offsetY})");
    }

    /// <summary>清除所有正在显示的粒子。</summary>
    public void Clear()
    {
        if (_layer == null) return;
        var children = _layer.Children.OfType<TextBlock>().ToList();
        foreach (var c in children)
        {
            c.BeginAnimation(System.Windows.UIElement.OpacityProperty, null);
            c.RenderTransform = null;
        }
        _layer.Children.Clear();
    }

    // ===== 内部 =====

    private void SpawnOne(ParticleDef def, double x, double y)
    {
        var tb = new TextBlock
        {
            Text = def.Symbol,
            FontSize = def.FontSize,
            Opacity = 1.0,
            IsHitTestVisible = false,
            RenderTransformOrigin = new Point(0.5, 0.5)
        };

        // 颜色（ARGB 十六进制字符串如 #FFFF4D6E）
        if (!string.IsNullOrEmpty(def.Color))
        {
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(def.Color);
                tb.Foreground = new SolidColorBrush(color);
            }
            catch
            {
                // 颜色解析失败 → 用符号原色
            }
        }

        // 随机起始位置（围绕 offsetX/Y 抖动）
        var jitterX = (_rng.NextDouble() - 0.5) * def.Drift * 2;
        var jitterY = (_rng.NextDouble() - 0.5) * 6;
        Canvas.SetLeft(tb, x + jitterX);
        Canvas.SetTop(tb, y + jitterY);

        _layer!.Children.Add(tb);

        // 启动上升 + 漂移 + 淡出动画
        var lifetimeMs = Math.Max(200, def.LifetimeMs);
        var rise = def.RiseSpeed * (lifetimeMs / 1000.0);
        var drift = (_rng.NextDouble() - 0.5) * 2 * def.Drift;
        var currentTop = Canvas.GetTop(tb);

        // Y 上升动画
        var yAnim = new DoubleAnimation
        {
            From = currentTop,
            To = currentTop - rise,
            Duration = TimeSpan.FromMilliseconds(lifetimeMs),
            EasingFunction = new QuadraticEase()
        };
        // X 漂移动画
        var xCurrent = Canvas.GetLeft(tb);
        var xAnim = new DoubleAnimation
        {
            From = xCurrent,
            To = xCurrent + drift,
            Duration = TimeSpan.FromMilliseconds(lifetimeMs),
            EasingFunction = new SineEase()
        };
        // 透明度淡出
        var opAnim = new DoubleAnimation
        {
            From = 1.0,
            To = 0.0,
            Duration = TimeSpan.FromMilliseconds(lifetimeMs),
            EasingFunction = new QuadraticEase()
        };

        // 完成后从 Canvas 移除
        opAnim.Completed += (_, _) =>
        {
            try
            {
                _layer?.Children.Remove(tb);
            }
            catch { /* 已被 Clear 或窗口关闭 */ }
        };

        tb.BeginAnimation(Canvas.TopProperty, yAnim);
        tb.BeginAnimation(Canvas.LeftProperty, xAnim);
        tb.BeginAnimation(System.Windows.UIElement.OpacityProperty, opAnim);
    }

    private bool TryGetParticleDef(string type, out ParticleDef def)
    {
        var cfg = ConfigService.LoadAnimations();
        if (cfg.Particles.TryGetValue(type, out var d))
        {
            def = d;
            return true;
        }
        // 未知类型 → 用默认
        def = new ParticleDef { Symbol = "*", FontSize = 14 };
        Logger.Warn("Particle", $"未知粒子类型: {type}，使用默认");
        return true;
    }
}
