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
/// - procedural：程序化动画（驱动 BreathScale / BobTranslate / PetRotate / PetImage.Opacity）
///
/// 程序化动画支持两种模式：
/// - 简单 from→to 模式：设置 from/to + target，单属性单段动画
/// - 关键帧序列模式：设置 keyframes，可同时驱动 scale/translate/rotate/opacity 多属性
///
/// 动作链（chain）：设置 chain 字段后，会按顺序串行播放每个子动作。
/// 粒子触发：动作播放时若 particles 非空，会通过 OnParticleTrigger 回调通知上层生成粒子。
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
    private DispatcherTimer? _idleActionTimer;  // 随机 idle 动作计时器（look_around/happy_bounce 等）
    private DoubleAnimation? _breathAnim;       // 呼吸动画
    private DoubleAnimation? _floatAnim;        // 漂浮动画

    private int _idleIndex = 0;
    private int _oneShotFrameIndex = 0;
    private List<int> _oneShotFrames = new();
    private bool _oneShotActive = false;

    private PetState _state = PetState.Idle;
    private PetMood _mood = PetMood.Neutral;
    private static readonly Random _rng = new();

    /// <summary>粒子触发回调：参数为 (type, count, offsetX, offsetY)。
    /// 由 PetWindow 注册，把粒子生成委托给 ParticleService。</summary>
    public Action<string, int, int, int>? OnParticleTrigger { get; set; }

    public AnimationService(PetWindow pet, List<BitmapSource> frames, Func<AnimationConfig> configLoader)
    {
        _pet = pet;
        _frames = frames;
        _configLoader = configLoader;

        // 订阅情绪变化：mood 改变时重启 idle 循环（让新的情绪帧生效）
        try
        {
            MoodService.Instance.OnMoodChanged += OnMoodChanged;
        }
        catch { /* MoodService 异常不影响动画系统初始化 */ }
    }

    /// <summary>情绪变化回调：更新当前 mood 并重启 idle 循环。</summary>
    private void OnMoodChanged(PetMood newMood)
    {
        if (_mood == newMood) return;
        _mood = newMood;

        // 仅在 Idle 状态且无一次性动作时重启 blink，让新情绪的帧立即生效
        if (_state == PetState.Idle && !_oneShotActive)
        {
            _idleTimer?.Stop();
            _idleTimer = null;
            StartIdleBlink();
        }
    }

    /// <summary>根据当前 mood 返回主 idle 帧（睁眼状态下的表情）。
    /// 帧索引约定见 config/app.yaml 的 sprite 段注释。</summary>
    private int GetMoodIdleFrame()
        => _mood switch
        {
            PetMood.Happy => 2,    // 微笑
            PetMood.Excited => 6,  // 大笑
            PetMood.Sad => 5,      // 半眯眼
            PetMood.Angry => 4,    // 撇眉
            PetMood.Curious => 3,  // 张嘴（惊讶/思考）
            PetMood.Shy => 7,      // 红脸
            _ => 0                  // Neutral：睁眼
        };

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
        _idleActionTimer?.Stop();
        _idleTimer = null;
        _oneShotTimer = null;
        _idleActionTimer = null;
        _pet.BreathScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        _pet.BreathScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        _pet.BobTranslate.BeginAnimation(TranslateTransform.YProperty, null);
        _pet.BobTranslate.BeginAnimation(TranslateTransform.XProperty, null);
        _pet.PetRotate.BeginAnimation(RotateTransform.AngleProperty, null);
        _pet.PetImage.BeginAnimation(System.Windows.UIElement.OpacityProperty, null);
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
        _idleActionTimer?.Stop();
        _idleTimer = null;
        _idleActionTimer = null;

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

    /// <summary>播放一次性互动动作（pat/highfive/feed），结束后回到当前状态循环。
    /// 支持 chain（动作链）、keyframes（关键帧）、简单 from→to、sprite_sheet 四种模式。</summary>
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

        // 触发粒子（立即触发的那部分，延迟触发的由动作内部调度）
        TriggerParticles(def, immediateOnly: true);

        // 优先级 1：动作链 → 串行播放
        if (def.Chain.Count > 0)
        {
            PlayChain(def, animationId);
            return;
        }

        // 优先级 2：关键帧序列 → 同步驱动多属性
        if (def.Keyframes.Count > 0 && def.Type == "procedural")
        {
            PlayKeyframes(def, animationId);
            return;
        }

        // 优先级 3：按 type 分发
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
                EasingFunction = GetEasing(breath.Easing)
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
                EasingFunction = GetEasing(fl.Easing)
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
            // 用 mood 对应帧替换默认睁眼帧（帧 1 仍是闭眼）
            var openFrame = GetMoodIdleFrame();
            if (openFrame >= _frames.Count) openFrame = 0;
            _idleIndex = (_idleIndex == 0) ? 1 : 0;
            _pet.PetImage.Source = _idleIndex == 0 ? _frames[openFrame] : _frames[1];
        };
        _idleTimer.Start();

        // 启动随机 idle 动作计时器（每 8 秒按 mood 权重随机播一个动作）
        StartIdleActionTimer();
    }

    /// <summary>按 mood_overrides 权重随机选择并播放一个 idle 动作。
    /// 仅在 Idle 状态、无一次性动作进行时触发。</summary>
    private void StartIdleActionTimer()
    {
        _idleActionTimer?.Stop();
        _idleActionTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(8)
        };
        _idleActionTimer.Tick += (_, _) =>
        {
            _idleActionTimer?.Stop();
            if (_oneShotActive || _state != PetState.Idle)
            {
                _idleActionTimer?.Start();
                return;
            }

            var cfg = _configLoader();
            var moodKey = _mood.ToString().ToLowerInvariant();
            if (!cfg.MoodOverrides.TryGetValue(moodKey, out var weights) || weights.Count == 0)
            {
                _idleActionTimer?.Start();
                return;
            }

            // 过滤权重 > 0 且已定义的动作，按权重加权随机选择
            var candidates = new List<(string id, double weight)>();
            foreach (var kv in weights)
            {
                if (kv.Value <= 0) continue;
                if (!cfg.Animations.ContainsKey(kv.Key)) continue;
                candidates.Add((kv.Key, kv.Value));
            }
            if (candidates.Count == 0)
            {
                _idleActionTimer?.Start();
                return;
            }

            var totalWeight = candidates.Sum(c => c.weight);
            var r = _rng.NextDouble() * totalWeight;
            double acc = 0;
            string? picked = null;
            foreach (var c in candidates)
            {
                acc += c.weight;
                if (r <= acc) { picked = c.id; break; }
            }
            picked ??= candidates[0].id;

            // 跳过 blink（已由 _idleTimer 处理），其他动作立即播放
            if (picked != "blink")
            {
                Logger.Info("Anim", $"随机 idle 动作: {picked}（mood={_mood}）");
                Play(picked);
            }

            _idleActionTimer?.Start();
        };
        _idleActionTimer.Start();
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

    /// <summary>动作链：串行播放子动作。</summary>
    private void PlayChain(AnimationDef def, string animId)
    {
        var queue = new Queue<string>(def.Chain);
        Logger.Info("Anim", $"播放动作链: {animId}（{queue.Count} 段）");
        // 触发链动作的延迟粒子（at_ms > 0）
        TriggerParticles(def, immediateOnly: false);
        PlayNextInChain(queue, animId);
    }

    private void PlayNextInChain(Queue<string> queue, string parentAnimId)
    {
        if (queue.Count == 0)
        {
            OnOneShotCompleted();
            return;
        }

        var nextId = queue.Dequeue();
        var cfg = _configLoader();
        if (!cfg.Animations.TryGetValue(nextId, out var nextDef))
        {
            Logger.Warn("Anim", $"动作链 {parentAnimId} 中子动作 {nextId} 未定义，跳过");
            PlayNextInChain(queue, parentAnimId);
            return;
        }

        // 子动作不再独立触发粒子（粒子在父动作统一触发）
        // 子动作完成后递归播放下一个
        _oneShotActive = true;

        if (nextDef.Keyframes.Count > 0 && nextDef.Type == "procedural")
        {
            PlayKeyframesInternal(nextDef, () => PlayNextInChain(queue, parentAnimId));
        }
        else if (nextDef.Type == "procedural")
        {
            PlayProceduralOneShotInternal(nextDef, () => PlayNextInChain(queue, parentAnimId));
        }
        else
        {
            PlaySpriteSheetOneShotInternal(nextDef, () => PlayNextInChain(queue, parentAnimId));
        }
    }

    /// <summary>关键帧序列：使用 DoubleAnimationUsingKeyFrames 同时驱动多个属性。</summary>
    private void PlayKeyframes(AnimationDef def, string animId)
    {
        var duration = def.GetDurationMs();
        PlayKeyframesInternal(def, OnOneShotCompleted);
        Logger.Info("Anim", $"播放关键帧动作: {animId}（{def.Keyframes.Count} 帧，{duration}ms）");
    }

    private void PlayKeyframesInternal(AnimationDef def, Action onComplete)
    {
        // 清空当前动画，避免冲突
        _pet.BreathScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        _pet.BreathScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        _pet.BobTranslate.BeginAnimation(TranslateTransform.XProperty, null);
        _pet.BobTranslate.BeginAnimation(TranslateTransform.YProperty, null);
        _pet.PetRotate.BeginAnimation(RotateTransform.AngleProperty, null);
        _pet.PetImage.BeginAnimation(System.Windows.UIElement.OpacityProperty, null);

        // 记录起始值（关键帧未指定的属性使用这些值）
        var startScaleX = _pet.BreathScale.ScaleX;
        var startScaleY = _pet.BreathScale.ScaleY;
        var startTransX = _pet.BobTranslate.X;
        var startTransY = _pet.BobTranslate.Y;
        var startRotate = _pet.PetRotate.Angle;
        var startOpacity = _pet.PetImage.Opacity;

        var totalMs = def.GetDurationMs();
        int completedCount = 0;
        int totalAnims = 0;

        // 收集每个属性的关键帧
        var sxFrames = new List<DoubleKeyFrame>();
        var syFrames = new List<DoubleKeyFrame>();
        var txFrames = new List<DoubleKeyFrame>();
        var tyFrames = new List<DoubleKeyFrame>();
        var rotFrames = new List<DoubleKeyFrame>();
        var opFrames = new List<DoubleKeyFrame>();

        // 起始关键帧（progress=0 隐式）
        foreach (var kf in def.Keyframes.OrderBy(k => k.Progress))
        {
            var keyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(totalMs * kf.Progress / 100.0));
            var easing = GetEasing(def.Easing);
            if (kf.ScaleX.HasValue)
                sxFrames.Add(new EasingDoubleKeyFrame(kf.ScaleX.Value, keyTime, easing));
            if (kf.ScaleY.HasValue)
                syFrames.Add(new EasingDoubleKeyFrame(kf.ScaleY.Value, keyTime, easing));
            if (kf.TranslateX.HasValue)
                txFrames.Add(new EasingDoubleKeyFrame(kf.TranslateX.Value, keyTime, easing));
            if (kf.TranslateY.HasValue)
                tyFrames.Add(new EasingDoubleKeyFrame(kf.TranslateY.Value, keyTime, easing));
            if (kf.Rotate.HasValue)
                rotFrames.Add(new EasingDoubleKeyFrame(kf.Rotate.Value, keyTime, easing));
            if (kf.Opacity.HasValue)
                opFrames.Add(new EasingDoubleKeyFrame(kf.Opacity.Value, keyTime, easing));
        }

        // 为每个属性创建动画
        if (sxFrames.Count > 0)
        {
            var anim = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(totalMs) };
            anim.KeyFrames.Add(new EasingDoubleKeyFrame(startScaleX, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            foreach (var f in sxFrames) anim.KeyFrames.Add(f);
            anim.Completed += (_, _) => { if (++completedCount == totalAnims) onComplete(); };
            totalAnims++;
            _pet.BreathScale.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
        }
        if (syFrames.Count > 0)
        {
            var anim = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(totalMs) };
            anim.KeyFrames.Add(new EasingDoubleKeyFrame(startScaleY, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            foreach (var f in syFrames) anim.KeyFrames.Add(f);
            anim.Completed += (_, _) => { if (++completedCount == totalAnims) onComplete(); };
            totalAnims++;
            _pet.BreathScale.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
        }
        if (txFrames.Count > 0)
        {
            var anim = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(totalMs) };
            anim.KeyFrames.Add(new EasingDoubleKeyFrame(startTransX, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            foreach (var f in txFrames) anim.KeyFrames.Add(f);
            anim.Completed += (_, _) => { if (++completedCount == totalAnims) onComplete(); };
            totalAnims++;
            _pet.BobTranslate.BeginAnimation(TranslateTransform.XProperty, anim);
        }
        if (tyFrames.Count > 0)
        {
            var anim = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(totalMs) };
            anim.KeyFrames.Add(new EasingDoubleKeyFrame(startTransY, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            foreach (var f in tyFrames) anim.KeyFrames.Add(f);
            anim.Completed += (_, _) => { if (++completedCount == totalAnims) onComplete(); };
            totalAnims++;
            _pet.BobTranslate.BeginAnimation(TranslateTransform.YProperty, anim);
        }
        if (rotFrames.Count > 0)
        {
            var anim = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(totalMs) };
            anim.KeyFrames.Add(new EasingDoubleKeyFrame(startRotate, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            foreach (var f in rotFrames) anim.KeyFrames.Add(f);
            anim.Completed += (_, _) => { if (++completedCount == totalAnims) onComplete(); };
            totalAnims++;
            _pet.PetRotate.BeginAnimation(RotateTransform.AngleProperty, anim);
        }
        if (opFrames.Count > 0)
        {
            var anim = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(totalMs) };
            anim.KeyFrames.Add(new EasingDoubleKeyFrame(startOpacity, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            foreach (var f in opFrames) anim.KeyFrames.Add(f);
            anim.Completed += (_, _) => { if (++completedCount == totalAnims) onComplete(); };
            totalAnims++;
            _pet.PetImage.BeginAnimation(System.Windows.UIElement.OpacityProperty, anim);
        }

        _oneShotActive = true;

        // 若没有关键帧属性被设置，立即完成
        if (totalAnims == 0)
        {
            onComplete();
        }
    }

    private void PlayProceduralOneShot(AnimationDef def, string animId)
    {
        PlayProceduralOneShotInternal(def, OnOneShotCompleted);
        var duration = def.GetDurationMs();
        Logger.Info("Anim", $"播放一次性动作: {animId}（procedural, target={def.Target}, {duration}ms）");
    }

    private void PlayProceduralOneShotInternal(AnimationDef def, Action onComplete)
    {
        var duration = def.GetDurationMs();
        var target = def.Target;
        var easing = GetEasing(def.Easing);

        if (target == "translate_y")
        {
            var anim = new DoubleAnimation(def.From, def.To, TimeSpan.FromMilliseconds(duration))
            {
                AutoReverse = def.AutoReverse,
                EasingFunction = easing
            };
            anim.Completed += (_, _) => onComplete();
            _pet.BobTranslate.BeginAnimation(TranslateTransform.YProperty, anim);
            _oneShotActive = true;
        }
        else if (target == "translate_x")
        {
            var anim = new DoubleAnimation(def.From, def.To, TimeSpan.FromMilliseconds(duration))
            {
                AutoReverse = def.AutoReverse,
                EasingFunction = easing
            };
            anim.Completed += (_, _) => onComplete();
            _pet.BobTranslate.BeginAnimation(TranslateTransform.XProperty, anim);
            _oneShotActive = true;
        }
        else if (target == "rotate")
        {
            var anim = new DoubleAnimation(def.From, def.To, TimeSpan.FromMilliseconds(duration))
            {
                AutoReverse = def.AutoReverse,
                EasingFunction = easing
            };
            anim.Completed += (_, _) => onComplete();
            _pet.PetRotate.BeginAnimation(RotateTransform.AngleProperty, anim);
            _oneShotActive = true;
        }
        else if (target == "opacity")
        {
            var anim = new DoubleAnimation(def.From, def.To, TimeSpan.FromMilliseconds(duration))
            {
                AutoReverse = def.AutoReverse,
                EasingFunction = easing
            };
            anim.Completed += (_, _) => onComplete();
            _pet.PetImage.BeginAnimation(System.Windows.UIElement.OpacityProperty, anim);
            _oneShotActive = true;
        }
        else if (target == "scale")
        {
            var sx = new DoubleAnimation(_pet.BreathScale.ScaleX, def.ScaleX, TimeSpan.FromMilliseconds(duration))
            { EasingFunction = easing };
            var sy = new DoubleAnimation(_pet.BreathScale.ScaleY, def.ScaleY, TimeSpan.FromMilliseconds(duration))
            { EasingFunction = easing };
            sy.Completed += (_, _) => onComplete();
            _pet.BreathScale.BeginAnimation(ScaleTransform.ScaleXProperty, sx);
            _pet.BreathScale.BeginAnimation(ScaleTransform.ScaleYProperty, sy);
            _oneShotActive = true;
        }
        else if (target == "scale_y")
        {
            var anim = new DoubleAnimation(def.From, def.To, TimeSpan.FromMilliseconds(duration))
            {
                AutoReverse = def.AutoReverse,
                EasingFunction = easing
            };
            anim.Completed += (_, _) => onComplete();
            _pet.BreathScale.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
            _oneShotActive = true;
        }
        else if (target == "scale_x")
        {
            var anim = new DoubleAnimation(def.From, def.To, TimeSpan.FromMilliseconds(duration))
            {
                AutoReverse = def.AutoReverse,
                EasingFunction = easing
            };
            anim.Completed += (_, _) => onComplete();
            _pet.BreathScale.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
            _oneShotActive = true;
        }
        else if (target == "scale_xy")
        {
            // 独立的 X 和 Y 缩放，from/to 用于 X，scale_x/scale_y 用于直接指定
            var sxFrom = def.From > 0 ? def.From : _pet.BreathScale.ScaleX;
            var sxTo = def.ScaleX > 0 ? def.ScaleX : def.To;
            var syFrom = def.From > 0 ? def.From : _pet.BreathScale.ScaleY;
            var syTo = def.ScaleY > 0 ? def.ScaleY : def.To;

            var sx = new DoubleAnimation(sxFrom, sxTo, TimeSpan.FromMilliseconds(duration))
            { EasingFunction = easing, AutoReverse = def.AutoReverse };
            var sy = new DoubleAnimation(syFrom, syTo, TimeSpan.FromMilliseconds(duration))
            { EasingFunction = easing, AutoReverse = def.AutoReverse };
            sy.Completed += (_, _) => onComplete();
            _pet.BreathScale.BeginAnimation(ScaleTransform.ScaleXProperty, sx);
            _pet.BreathScale.BeginAnimation(ScaleTransform.ScaleYProperty, sy);
            _oneShotActive = true;
        }
        else
        {
            // 未知目标，直接结束
            onComplete();
        }
    }

    private void PlaySpriteSheetOneShot(AnimationDef def, string animId)
    {
        PlaySpriteSheetOneShotInternal(def, OnOneShotCompleted);
        Logger.Info("Anim", $"播放一次性动作: {animId}（sprite_sheet, {def.Frames.Count}帧）");
    }

    private void PlaySpriteSheetOneShotInternal(AnimationDef def, Action onComplete)
    {
        if (def.Frames.Count == 0 || _frames.Count == 0)
        {
            onComplete();
            return;
        }

        _oneShotFrames = def.Frames.Where(i => i < _frames.Count).ToList();
        if (_oneShotFrames.Count == 0)
        {
            onComplete();
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
                onComplete();
                return;
            }
            _pet.PetImage.Source = _frames[_oneShotFrames[_oneShotFrameIndex]];
            _oneShotFrameIndex++;
        };

        // 立即显示第一帧
        _pet.PetImage.Source = _frames[_oneShotFrames[0]];
        _oneShotFrameIndex = 1;
        _oneShotTimer.Start();
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

    // ===== 缓动函数选择 =====

    /// <summary>根据名称返回缓动函数。未知名称回退到 SineEase。</summary>
    private static IEasingFunction? GetEasing(string name)
        => name?.ToLowerInvariant() switch
        {
            "linear" => null,  // 线性 = 无缓动
            "sine" => new SineEase(),
            "cubic" => new CubicEase(),
            "quadratic" => new QuadraticEase(),
            "bounce" => new BounceEase(),
            "elastic" => new ElasticEase(),
            "back" => new BackEase(),
            "" => new SineEase(),
            null => new SineEase(),
            _ => new SineEase()  // 未知回退到 Sine
        };

    // ===== 粒子触发 =====

    /// <summary>触发动作的粒子。immediateOnly=true 时只触发 at_ms=0 的粒子，否则触发延迟粒子。</summary>
    private void TriggerParticles(AnimationDef def, bool immediateOnly)
    {
        if (def.Particles.Count == 0 || OnParticleTrigger == null) return;

        foreach (var p in def.Particles)
        {
            if (immediateOnly && p.AtMs > 0) continue;

            if (p.AtMs <= 0)
            {
                OnParticleTrigger(p.Type, p.Count, p.OffsetX, p.OffsetY);
            }
            else
            {
                // 延迟触发
                var timer = new DispatcherTimer(DispatcherPriority.Render)
                {
                    Interval = TimeSpan.FromMilliseconds(p.AtMs)
                };
                timer.Tick += (_, _) =>
                {
                    timer.Stop();
                    OnParticleTrigger?.Invoke(p.Type, p.Count, p.OffsetX, p.OffsetY);
                };
                timer.Start();
            }
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
