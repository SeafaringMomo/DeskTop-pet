using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DesktopPetCSharp.Models;
using DesktopPetCSharp.Windows;

namespace DesktopPetCSharp.Services;

/// <summary>
/// 动作系统服务（P3）：独立于皮肤，驱动 PetWindow 的动画目标。
///
/// 动作类型：
/// - sprite_sheet：帧动画（按 frames 索引切换 PetImage.Source）
/// - procedural：程序化动画（驱动 BreathScale / BobTranslate）
///
/// 状态驱动：根据饱食度/互动状态切换 idle / hungry / sleep 循环。
/// 一次性动作：pat / highfive / feed 播放后自动回到循环状态。
/// </summary>
public sealed class AnimationService
{
    private readonly PetWindow _pet;
    private readonly List<BitmapSource> _frames;
    private readonly Func<AnimationConfig> _configLoader;

    private DispatcherTimer? _idleTimer;        // 眨眼循环
    private DispatcherTimer? _oneShotTimer;     // 一次性动作帧切换
    private DoubleAnimation? _breathAnim;       // 呼吸动画
    private DoubleAnimation? _floatAnim;        // 漂浮动画

    private int _idleIndex = 0;
    private int _oneShotFrameIndex = 0;
    private List<int> _oneShotFrames = new();
    private bool _oneShotActive = false;

    private PetState _state = PetState.Idle;

    public AnimationService(PetWindow pet, List<BitmapSource> frames, Func<AnimationConfig> configLoader)
    {
        _pet = pet;
        _frames = frames;
        _configLoader = configLoader;
    }

    /// <summary>启动默认循环（idle + breath + float）。</summary>
    public void StartDefaultLoop()
    {
        StartBreathAndFloat();
        StartIdleBlink();
    }

    /// <summary>停止所有循环动画。</summary>
    public void StopAll()
    {
        _idleTimer?.Stop();
        _oneShotTimer?.Stop();
        _idleTimer = null;
        _oneShotTimer = null;
        _pet.BreathScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        _pet.BreathScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        _pet.BobTranslate.BeginAnimation(TranslateTransform.YProperty, null);
    }

    /// <summary>
    /// 根据宠物状态切换循环动作。
    /// </summary>
    public void SetState(PetState state)
    {
        if (_state == state && _oneShotActive) return;
        _state = state;
        if (_oneShotActive) return; // 一次性动作进行中，等结束再切换

        // 停止现有循环
        _idleTimer?.Stop();
        _idleTimer = null;

        switch (state)
        {
            case PetState.Idle:
                StartBreathAndFloat();
                StartIdleBlink();
                break;
            case PetState.Hungry:
            case PetState.Sleep:
                StartStateLoop(state);
                break;
        }
        Logger.Info("Anim", $"宠物状态切换: {state}");
    }

    /// <summary>播放一次性互动动作（pat/highfive/feed），结束后回到当前状态循环。</summary>
    public void Play(string animationId)
    {
        var cfg = _configLoader();
        if (!cfg.Animations.TryGetValue(animationId, out var def))
        {
            Logger.Warn("Anim", $"动作未定义: {animationId}");
            return;
        }

        // 中断当前循环
        _idleTimer?.Stop();
        _idleTimer = null;

        if (def.Type == "procedural")
        {
            PlayProceduralOneShot(def, animationId);
        }
        else
        {
            PlaySpriteSheetOneShot(def, animationId);
        }
    }

    // ===== 循环动画 =====

    private void StartBreathAndFloat()
    {
        var cfg = _configLoader();
        // 呼吸：ScaleY 1.0 ↔ 1.03
        if (cfg.Animations.TryGetValue("breath", out var breath) && breath.Type == "procedural")
        {
            _breathAnim = new DoubleAnimation(breath.From, breath.To, TimeSpan.FromSeconds(breath.DurationS))
            {
                AutoReverse = breath.AutoReverse,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase()
            };
            _pet.BreathScale.BeginAnimation(ScaleTransform.ScaleYProperty, _breathAnim);
        }

        // 漂浮：TranslateY 0 ↔ -3
        if (cfg.Animations.TryGetValue("float", out var fl) && fl.Type == "procedural")
        {
            _floatAnim = new DoubleAnimation(fl.From, fl.To, TimeSpan.FromSeconds(fl.DurationS))
            {
                AutoReverse = fl.AutoReverse,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase()
            };
            _pet.BobTranslate.BeginAnimation(TranslateTransform.YProperty, _floatAnim);
        }
    }

    private void StartIdleBlink()
    {
        var cfg = _configService;
        var interval = 3000;
        if (cfg.Animations.TryGetValue("idle", out var idle))
        {
            interval = idle.IntervalMs > 0 ? idle.IntervalMs : 3000;
        }

        _idleTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(interval)
        };
        _idleTimer.Tick += (_, _) =>
        {
            if (_oneShotActive || _frames.Count < 2) return;
            _idleIndex = (_idleIndex == 0) ? 1 : 0;
            _pet.PetImage.Source = _frames[_idleIndex];
        };
        _idleTimer.Start();
    }

    private AnimationConfig _configService => _configLoader();

    private void StartStateLoop(PetState state)
    {
        var animId = state == PetState.Hungry ? "hungry" : "sleep";
        var cfg = _configLoader();
        if (!cfg.Animations.TryGetValue(animId, out var def) || def.Type != "sprite_sheet") return;

        // 持续显示闭眼帧
        if (def.Frames.Count > 0 && _frames.Count > def.Frames[0])
        {
            _pet.PetImage.Source = _frames[def.Frames[0]];
        }
        // 停止呼吸/漂浮的视觉（保留轻微浮动更自然，这里保留 breath）
    }

    // ===== 一次性动作 =====

    private void PlayProceduralOneShot(AnimationDef def, string animId)
    {
        var duration = def.GetDurationMs();
        var target = def.Target;

        if (target == "translate_y")
        {
            var anim = new DoubleAnimation(def.From, def.To, TimeSpan.FromMilliseconds(duration))
            {
                AutoReverse = def.AutoReverse,
                EasingFunction = new SineEase()
            };
            anim.Completed += (_, _) => OnOneShotCompleted();
            _pet.BobTranslate.BeginAnimation(TranslateTransform.YProperty, anim);
            _oneShotActive = true;
        }
        else if (target == "scale")
        {
            var sx = new DoubleAnimation(_pet.BreathScale.ScaleX, def.ScaleX, TimeSpan.FromMilliseconds(duration))
            { EasingFunction = new SineEase() };
            var sy = new DoubleAnimation(_pet.BreathScale.ScaleY, def.ScaleY, TimeSpan.FromMilliseconds(duration))
            { EasingFunction = new SineEase() };
            sy.Completed += (_, _) => OnOneShotCompleted();
            _pet.BreathScale.BeginAnimation(ScaleTransform.ScaleXProperty, sx);
            _pet.BreathScale.BeginAnimation(ScaleTransform.ScaleYProperty, sy);
            _oneShotActive = true;
        }
        else
        {
            // 未知目标，直接结束
            OnOneShotCompleted();
        }

        Logger.Info("Anim", $"播放一次性动作: {animId}（procedural, {duration}ms）");
    }

    private void PlaySpriteSheetOneShot(AnimationDef def, string animId)
    {
        if (def.Frames.Count == 0 || _frames.Count == 0)
        {
            OnOneShotCompleted();
            return;
        }

        _oneShotFrames = def.Frames.Where(i => i < _frames.Count).ToList();
        if (_oneShotFrames.Count == 0)
        {
            OnOneShotCompleted();
            return;
        }

        _oneShotFrameIndex = 0;
        _oneShotActive = true;

        var interval = def.IntervalMs > 0 ? def.IntervalMs : 200;
        _oneShotTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(interval)
        };
        _oneShotTimer.Tick += (_, _) =>
        {
            if (_oneShotFrameIndex >= _oneShotFrames.Count)
            {
                _oneShotTimer.Stop();
                _oneShotTimer = null;
                OnOneShotCompleted();
                return;
            }
            _pet.PetImage.Source = _frames[_oneShotFrames[_oneShotFrameIndex]];
            _oneShotFrameIndex++;
        };

        // 立即显示第一帧
        _pet.PetImage.Source = _frames[_oneShotFrames[0]];
        _oneShotFrameIndex = 1;
        _oneShotTimer.Start();

        Logger.Info("Anim", $"播放一次性动作: {animId}（sprite_sheet, {def.Frames.Count}帧）");
    }

    private void OnOneShotCompleted()
    {
        _oneShotActive = false;
        _oneShotTimer = null;

        // 恢复呼吸/漂浮
        StartBreathAndFloat();

        // 恢复状态循环
        switch (_state)
        {
            case PetState.Idle:
                StartIdleBlink();
                break;
            case PetState.Hungry:
            case PetState.Sleep:
                StartStateLoop(_state);
                break;
        }
    }
}

/// <summary>宠物状态枚举，驱动动作循环切换。</summary>
public enum PetState
{
    /// <summary>正常待机（眨眼 + 呼吸 + 漂浮）</summary>
    Idle,
    /// <summary>饥饿（饱食度低于阈值，持续闭眼）</summary>
    Hungry,
    /// <summary>睡眠（长时间无互动，持续闭眼）</summary>
    Sleep
}
