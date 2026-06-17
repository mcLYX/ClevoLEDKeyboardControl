namespace ColorfulLedKeyboard.Service;

using ColorfulLedKeyboard.Core;
using System.Runtime.InteropServices;

public class Worker : BackgroundService
{
    private readonly SettingsStore _settingsStore = new();
    private readonly DchuKeyboardDevice _device = new();
    private readonly AudioSourceProvider _audioSource;
    private readonly SystemAudioLevelMeter _audioLevelMeter;
    private readonly AudioBandLevelMeter _audioBandLevelMeter;
    private readonly ILogger<Worker> _logger;
    private FileSystemWatcher? _watcher;
    private volatile bool _settingsChanged = true;

    public Worker(ILogger<Worker> logger, ILoggerFactory loggerFactory)
    {
        _logger = logger;
        _audioSource = new AudioSourceProvider(loggerFactory.CreateLogger<AudioSourceProvider>());
        _audioLevelMeter = new SystemAudioLevelMeter(_audioSource);
        _audioBandLevelMeter = new AudioBandLevelMeter(_audioSource);
        _audioSource.SourceChanged += OnAudioSourceChanged;

        // 订阅完事件后立刻刷一次状态：让初始（启动那一刻）的设备状态也走 OnAudioSourceChanged
        // 写到文件里，否则 Tray 在用户首次切设备前都看不到任何状态，UI 显示"检测中…"。
        _audioSource.RefreshNow();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        EnsureConfigWatcher();
        await FlashStartupAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            var settings = BuildRuntimeSettings(_settingsStore.Load());
            _settingsChanged = false;

            if (!settings.Enabled)
            {
                TryTurnOffKeyboard();
                await WaitForSettingsChangeAsync(1000, stoppingToken);
                continue;
            }

            try
            {
                await RunEffectAsync(settings, stoppingToken);
            }
            catch (DllNotFoundException ex)
            {
                _logger.LogError(ex, "InsydeDCHU.dll was not found. Copy it next to the service executable.");
                await Task.Delay(5000, stoppingToken);
            }
            catch (EntryPointNotFoundException ex)
            {
                _logger.LogError(ex, "InsydeDCHU.dll does not expose SetDCHU_Data.");
                await Task.Delay(5000, stoppingToken);
            }
            catch (SEHException ex)
            {
                _logger.LogError(ex, "The keyboard LED driver rejected the operation.");
                await Task.Delay(5000, stoppingToken);
            }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _watcher?.Dispose();
        _audioSource.SourceChanged -= OnAudioSourceChanged;

        // NAudio 在某些路径下 StopRecording / Dispose 可能阻塞（COM 回调链路死锁）
        // 加 5 秒超时保护，超时则放弃 dispose 让进程自然终结
        var disposeTask = Task.Run(() =>
        {
            try { _audioBandLevelMeter.Dispose(); } catch (Exception ex) { _logger.LogWarning(ex, "AudioBandLevelMeter.Dispose threw"); }
            try { _audioLevelMeter.Dispose(); } catch (Exception ex) { _logger.LogWarning(ex, "SystemAudioLevelMeter.Dispose threw"); }
            try { _audioSource.Dispose(); } catch (Exception ex) { _logger.LogWarning(ex, "AudioSourceProvider.Dispose threw"); }
        });

        var completed = await Task.WhenAny(disposeTask, Task.Delay(TimeSpan.FromSeconds(5), cancellationToken));
        if (completed != disposeTask)
        {
            _logger.LogWarning("Audio dispose timed out after 5s; proceeding with shutdown anyway");
        }

        await base.StopAsync(cancellationToken);
    }

    private void TryTurnOffKeyboard()
    {
        try
        {
            _device.SetColor(RgbColor.Black);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or SEHException)
        {
            _logger.LogWarning(ex, "Keyboard LEDs could not be turned off.");
        }
    }

    private async Task RunEffectAsync(KeyboardSettings settings, CancellationToken stoppingToken)
    {
        if (settings.OperatingMode == OperatingMode.Music)
        {
            await RunMusicAsync(settings, stoppingToken);
            return;
        }

        var generator = new LightingFrameGenerator(settings);
        var nextRuntimeRefresh = DateTimeOffset.UtcNow.AddSeconds(1);
        RgbColor? lastColor = null;

        while (!stoppingToken.IsCancellationRequested && !_settingsChanged)
        {
            if (DateTimeOffset.UtcNow >= nextRuntimeRefresh)
            {
                nextRuntimeRefresh = DateTimeOffset.UtcNow.AddSeconds(1);
                if (ShouldRebuildRuntimeSettings(settings))
                {
                    _settingsChanged = true;
                    return;
                }
            }

            var brightness = ApplyTypingPulseBrightness(settings.Brightness, settings);
            var color = ApplyNotificationFlash(generator.Next(brightness), settings);
            if (color != lastColor)
            {
                _device.SetColor(color);
                lastColor = color;
            }

            if (settings.Effect.Type is EffectType.Static or EffectType.Off)
            {
                if ((settings.Effect.Type == EffectType.Static && settings.TypingPulse.Enabled) ||
                    settings.NotificationFlash.Enabled)
                {
                    await Task.Delay(40, stoppingToken);
                    continue;
                }

                if (NeedsRuntimePolling(settings))
                {
                    await Task.Delay(1000, stoppingToken);
                    _settingsChanged = true;
                    return;
                }

                await WaitForSettingsChangeAsync(1000, stoppingToken);
                _settingsChanged = true;
                return;
            }

            await Task.Delay(generator.IntervalMs, stoppingToken);
        }
    }

    private async Task RunMusicAsync(KeyboardSettings settings, CancellationToken stoppingToken)
    {
        var music = settings.Effect.Music.Normalize();
        var controller = new MusicPulseController();
        var musicColors = music.Colors.Select(RgbColor.FromHex).ToList();
        var nextRuntimeRefresh = DateTimeOffset.UtcNow.AddSeconds(1);
        RgbColor? lastColor = null;

        // 频率分布模式的平滑状态
        // 颜色组：根据 EqualLoudness 开关选择原始或加权，用于颜色比例
        var freqSmoothLow = 0d;
        var freqSmoothMid = 0d;
        var freqSmoothHigh = 0d;
        // 亮度组：始终用原始总能量，保证 A-weighting 不影响亮度
        var freqSmoothRawTotal = 0d;
        // AGC 动态峰值/下限：跟踪近期峰值与底噪，使亮度相对归一化，适应不同响度/动态范围的音源
        var dynamicPeak = 0.0005;
        var dynamicFloor = 0d;
        const double peakHalfLifeSeconds = 8.0;
        const double floorHalfLifeSeconds = 20.0;
        var freqLastUpdate = DateTimeOffset.MinValue;

        // 进入音乐模式立刻刷一次状态文件，避免 Tray 看到陈旧值
        _audioSource.RefreshNow();

        try
        {
            while (!stoppingToken.IsCancellationRequested && !_settingsChanged)
            {
                if (DateTimeOffset.UtcNow >= nextRuntimeRefresh)
                {
                    nextRuntimeRefresh = DateTimeOffset.UtcNow.AddSeconds(1);
                    if (ShouldRebuildRuntimeSettings(settings))
                    {
                        _settingsChanged = true;
                        return;
                    }
                }

                if (music.ResponseMode == MusicResponseMode.FrequencyDistribution)
                {
                    // 频率分布模式：不做节拍检测，直接映射三频能量到颜色
                    // 一次计算同时返回原始与加权能量，Goertzel 不重复
                    var analysis = _audioBandLevelMeter.GetTriBandAnalysis();
                    var now = DateTimeOffset.UtcNow;
                    var dt = freqLastUpdate == DateTimeOffset.MinValue
                        ? music.IntervalMs / 1000d
                        : Math.Clamp((now - freqLastUpdate).TotalSeconds, 0.005, 0.2);
                    freqLastUpdate = now;

                    // 颜色比例：EqualLoudness 开启时用加权能量，否则用原始能量
                    var colorSource = music.EqualLoudness ? analysis.Weighted : analysis.Raw;
                    freqSmoothLow = FreqSmooth(freqSmoothLow, colorSource.Low, dt, 0.04);
                    freqSmoothMid = FreqSmooth(freqSmoothMid, colorSource.Mid, dt, 0.04);
                    freqSmoothHigh = FreqSmooth(freqSmoothHigh, colorSource.High, dt, 0.04);

                    // 亮度：始终用原始总能量，A-weighting 不影响亮度
                    var rawTotal = analysis.Raw.Low + analysis.Raw.Mid + analysis.Raw.High;
                    freqSmoothRawTotal = FreqSmooth(freqSmoothRawTotal, rawTotal, dt, 0.04);

                    // AGC 动态范围监测：根据 DynamicRange 选项决定归一化方式
                    // PeakOnly：只跟踪峰值，亮度=energy/peak（适应整体响度差异）
                    // PeakAndFloor：跟踪峰值与下限，亮度=(energy-floor)/(peak-floor)（扩展压缩动态范围）
                    // None：不启用动态监测，用固定阈值线性映射
                    var sensitivityFactor = Math.Clamp(music.Sensitivity / 2.0, 0.25, 2.0);
                    double normalizedEnergy;
                    switch (music.DynamicRange)
                    {
                        case DynamicRangeMode.PeakOnly:
                            // 峰值快速攻击、慢速衰减
                            if (freqSmoothRawTotal > dynamicPeak)
                            {
                                dynamicPeak = freqSmoothRawTotal;
                            }
                            else
                            {
                                dynamicPeak *= Math.Exp(-dt * Math.Log(2) / peakHalfLifeSeconds);
                                dynamicPeak = Math.Max(dynamicPeak, 0.0005);
                            }
                            normalizedEnergy = Math.Clamp(freqSmoothRawTotal / dynamicPeak, 0, 1);
                            break;
                        case DynamicRangeMode.PeakAndFloor:
                            // 峰值同上
                            if (freqSmoothRawTotal > dynamicPeak)
                            {
                                dynamicPeak = freqSmoothRawTotal;
                            }
                            else
                            {
                                dynamicPeak *= Math.Exp(-dt * Math.Log(2) / peakHalfLifeSeconds);
                                dynamicPeak = Math.Max(dynamicPeak, 0.0005);
                            }
                            // 下限：慢速跟踪最低能量（20秒半衰期，向上缓慢爬升）
                            if (freqSmoothRawTotal < dynamicFloor)
                            {
                                dynamicFloor = freqSmoothRawTotal;
                            }
                            else
                            {
                                dynamicFloor += (freqSmoothRawTotal - dynamicFloor) *
                                    (1 - Math.Exp(-dt * Math.Log(2) / floorHalfLifeSeconds));
                            }
                            dynamicFloor = Math.Min(dynamicFloor, dynamicPeak * 0.8);
                            normalizedEnergy = Math.Clamp(
                                (freqSmoothRawTotal - dynamicFloor) / Math.Max(1e-6, dynamicPeak - dynamicFloor),
                                0, 1);
                            break;
                        default:
                            // None：固定阈值线性映射
                            normalizedEnergy = Math.Clamp(freqSmoothRawTotal * 3.0, 0, 1);
                            break;
                    }

                    var energy = Math.Clamp(normalizedEnergy * sensitivityFactor, 0, 1);
                    // 亮度映射，应用全局 gamma 校正（gamma>1 中间值变暗，对比度增加）
                    var gamma = Math.Clamp(settings.Effect.BrightnessGamma, 0.8, 3.0);
                    var brightness = music.BaseBrightness +
                        (music.PeakBrightness - music.BaseBrightness) * Math.Pow(energy, gamma);
                    var brightnessPct = (int)Math.Clamp(Math.Round(brightness), music.BaseBrightness, music.PeakBrightness);

                    // 颜色计算：用 colorTotal 作为权重在"频段比例色"与"基色"之间平滑混合，
                    // 避免低音量时颜色在两种逻辑间突然跳变（绿→红）。
                    var colorTotal = freqSmoothLow + freqSmoothMid + freqSmoothHigh;
                    // blend=1 完全用频段比例色，blend=0 完全用基色（低能量时）
                    var blend = Math.Clamp(colorTotal / 0.005, 0, 1);

                    RgbColor bandColor;
                    if (colorTotal < 1e-7)
                    {
                        bandColor = new RgbColor(255, 0, 0);
                    }
                    else
                    {
                        var r = freqSmoothLow / colorTotal;
                        var g = freqSmoothMid / colorTotal;
                        var b = freqSmoothHigh / colorTotal;
                        bandColor = new RgbColor(
                            (byte)Math.Round(r * 255),
                            (byte)Math.Round(g * 255),
                            (byte)Math.Round(b * 255));
                    }

                    // 基色：低频红（hue=0）经色调偏移
                    var baseHue = (0 + music.FreqHueOffset + 360) % 360;
                    var baseColor = RgbColor.FromHsv(baseHue, 1, 1);

                    // 平滑混合
                    var mixedRgb = blend < 0.001
                        ? baseColor
                        : blend > 0.999
                            ? bandColor
                            : RgbColor.Lerp(baseColor, bandColor, blend);

                    // 色调偏移（对混合后的颜色再统一应用一次，确保偏移生效）
                    if (music.FreqHueOffset != 0)
                    {
                        var (hue, sat, _) = mixedRgb.ToHsv();
                        hue = (hue + music.FreqHueOffset + 360) % 360;
                        mixedRgb = RgbColor.FromHsv(hue, sat > 0.01 ? 1 : 0, 1);
                    }

                    var color = mixedRgb.Scale(brightnessPct);
                    color = ApplyNotificationFlash(color, settings);
                    if (color != lastColor)
                    {
                        _device.SetColor(color);
                        lastColor = color;
                    }
                }
                else
                {
                    // 节拍模式（LevelColor / BrightnessPulse）
                    // 永远调 meter（同 v1.3）：静音 → envelope=0 → 灯自然降到 BaseBrightness 颜色保持。
                    // HFP 屏蔽在 meter 内部处理（Status==Hfp 时 EnsureCapture 跳过、不激活 SCO）。
                    var level = music.EqEnabled
                        ? Math.Max(_audioBandLevelMeter.GetAdaptiveBeatLevel(music), _audioLevelMeter.GetPeakLevel() * 0.12f)
                        : _audioLevelMeter.GetPeakLevel();
                    var systemVolume = _audioLevelMeter.GetMasterVolumeScalar();
                    var frame = controller.Next(music, level, systemVolume, musicColors.Count);
                    var envelope = frame.Envelope;
                    // 应用全局 gamma 校正到亮度映射
                    var beatGamma = Math.Clamp(settings.Effect.BrightnessGamma, 0.8, 3.0);
                    var musicBrightness = music.BaseBrightness +
                        (music.PeakBrightness - music.BaseBrightness) * Math.Pow(envelope, beatGamma);
                    var brightness = (int)Math.Clamp(Math.Round(musicBrightness), music.BaseBrightness, music.PeakBrightness);

                    // BrightnessPulse：颜色固定不变，只有亮度脉冲
                    // LevelColor：每次节拍切换颜色
                    var sourceColor = music.ResponseMode == MusicResponseMode.BrightnessPulse
                        ? musicColors[0]
                        : musicColors[frame.ColorIndex % musicColors.Count];
                    var color = ApplyNotificationFlash(sourceColor.Scale(brightness), settings);

                    if (color != lastColor)
                    {
                        _device.SetColor(color);
                        lastColor = color;
                    }
                }

                await Task.Delay(music.IntervalMs, stoppingToken);
            }
        }
        finally
        {
            _audioBandLevelMeter.PauseCapture();
            _audioLevelMeter.PauseDevice();
        }
    }

    private static double FreqSmooth(double current, double target, double dtSeconds, double timeConstantSeconds)
    {
        var alpha = 1 - Math.Exp(-dtSeconds / Math.Max(0.001, timeConstantSeconds));
        return current + (target - current) * alpha;
    }

    private static RgbColor ApplyNotificationFlash(RgbColor color, KeyboardSettings settings)
    {
        var flash = settings.NotificationFlash.Normalize();
        if (!flash.Enabled)
        {
            return color;
        }

        var state = NotificationFlashState.Load();
        if (state is null)
        {
            return color;
        }

        var elapsedMs = (DateTimeOffset.UtcNow - state.TriggeredUtc).TotalMilliseconds;
        var cycleMs = flash.PulseMs * 2;
        var totalMs = cycleMs * flash.Pulses;
        if (elapsedMs < 0 || elapsedMs > totalMs)
        {
            return color;
        }

        var phase = elapsedMs % cycleMs;
        return phase < flash.PulseMs ? RgbColor.FromHex(flash.Color) : RgbColor.Black;
    }

    private static int ApplyTypingPulseBrightness(int currentBrightness, KeyboardSettings settings)
    {
        var pulse = settings.TypingPulse.Normalize();
        if (!pulse.Enabled)
        {
            return currentBrightness;
        }

        var state = TypingPulseState.Load();
        if (state is null)
        {
            return currentBrightness;
        }

        var elapsedMs = (DateTimeOffset.UtcNow - state.LastKeyUtc).TotalMilliseconds;
        if (elapsedMs < 0 || elapsedMs > pulse.HoldMs + pulse.FadeMs)
        {
            return currentBrightness;
        }

        var pulseBrightness = pulse.PeakBrightness;
        if (elapsedMs > pulse.HoldMs)
        {
            var progress = Math.Clamp((elapsedMs - pulse.HoldMs) / Math.Max(1, pulse.FadeMs), 0, 1);
            pulseBrightness = (int)Math.Round(pulse.PeakBrightness - (pulse.PeakBrightness - currentBrightness) * progress);
        }

        return Math.Max(currentBrightness, pulseBrightness);
    }

    private static KeyboardSettings BuildRuntimeSettings(KeyboardSettings settings)
    {
        var runtime = settings.CloneForRuntime();
        ApplySchedule(runtime);
        ApplyAppProfiles(runtime);
        ApplyIdleDim(runtime);
        return runtime.Normalize();
    }

    private static bool ShouldRebuildRuntimeSettings(KeyboardSettings current)
    {
        var next = BuildRuntimeSettings(new SettingsStore().Load());
        return next.Enabled != current.Enabled ||
            next.OperatingMode != current.OperatingMode ||
            next.Brightness != current.Brightness ||
            !NotificationFlashEquals(next.NotificationFlash, current.NotificationFlash) ||
            next.Effect.Type != current.Effect.Type ||
            next.Effect.Color != current.Effect.Color ||
            next.Effect.Step != current.Effect.Step ||
            next.Effect.IntervalMs != current.Effect.IntervalMs ||
            next.Effect.PeriodMs != current.Effect.PeriodMs ||
            next.Effect.MinimumBrightness != current.Effect.MinimumBrightness ||
            next.Effect.HardBlink != current.Effect.HardBlink ||
            next.Effect.CustomSequenceColorsEnabled != current.Effect.CustomSequenceColorsEnabled ||
            next.Effect.GradientHoldMs != current.Effect.GradientHoldMs ||
            next.Effect.GradientTransitionMs != current.Effect.GradientTransitionMs ||
            next.Effect.GradientMinBrightnessPercent != current.Effect.GradientMinBrightnessPercent ||
            next.Effect.GradientAlgorithm != current.Effect.GradientAlgorithm ||
            !MusicEquals(next.Effect.Music, current.Effect.Music) ||
            next.Effect.Sequence.Count != current.Effect.Sequence.Count ||
            next.Effect.Sequence.Zip(current.Effect.Sequence).Any(pair =>
                pair.First.Color != pair.Second.Color ||
                pair.First.HoldMs != pair.Second.HoldMs ||
                pair.First.TransitionMs != pair.Second.TransitionMs ||
                pair.First.Breathing != pair.Second.Breathing);
    }

    private static bool NeedsRuntimePolling(KeyboardSettings settings)
    {
        return settings.Schedule.Enabled ||
            settings.IdleDim.Enabled ||
            settings.AppProfiles.Enabled;
    }

    private static bool NotificationFlashEquals(NotificationFlashSettings left, NotificationFlashSettings right)
    {
        return left.Enabled == right.Enabled &&
            left.Color == right.Color &&
            left.Pulses == right.Pulses &&
            left.PulseMs == right.PulseMs &&
            left.CooldownSeconds == right.CooldownSeconds;
    }

    private static bool MusicEquals(MusicSettings left, MusicSettings right)
    {
        return left.LevelColorEnabled == right.LevelColorEnabled &&
            left.PresetName == right.PresetName &&
            left.ResponseMode == right.ResponseMode &&
            left.LowColor == right.LowColor &&
            left.HighColor == right.HighColor &&
            left.Colors.Count == right.Colors.Count &&
            left.Colors.SequenceEqual(right.Colors, StringComparer.OrdinalIgnoreCase) &&
            Math.Abs(left.Sensitivity - right.Sensitivity) < 0.001 &&
            left.AttackMs == right.AttackMs &&
            left.ReleaseMs == right.ReleaseMs &&
            left.BaseBrightness == right.BaseBrightness &&
            left.PeakBrightness == right.PeakBrightness &&
            left.IntervalMs == right.IntervalMs &&
            Math.Abs(left.NoiseGate - right.NoiseGate) < 0.001 &&
            Math.Abs(left.BeatThreshold - right.BeatThreshold) < 0.001 &&
            left.PeakHoldMs == right.PeakHoldMs &&
            left.FollowSystemVolume == right.FollowSystemVolume &&
            left.EqEnabled == right.EqEnabled &&
            left.EqLowHz == right.EqLowHz &&
            left.EqHighHz == right.EqHighHz &&
            left.FreqHueOffset == right.FreqHueOffset &&
            left.EqualLoudness == right.EqualLoudness &&
            left.DynamicRange == right.DynamicRange &&
            left.CustomPresets.Count == right.CustomPresets.Count &&
            left.CustomPresets.Zip(right.CustomPresets).All(pair => MusicPresetEquals(pair.First, pair.Second));
    }

    private static bool MusicPresetEquals(MusicPreset left, MusicPreset right)
    {
        return left.Name == right.Name &&
            left.ResponseMode == right.ResponseMode &&
            left.LowColor == right.LowColor &&
            left.HighColor == right.HighColor &&
            left.Colors.Count == right.Colors.Count &&
            left.Colors.SequenceEqual(right.Colors, StringComparer.OrdinalIgnoreCase) &&
            Math.Abs(left.Sensitivity - right.Sensitivity) < 0.001 &&
            left.AttackMs == right.AttackMs &&
            left.ReleaseMs == right.ReleaseMs &&
            left.BaseBrightness == right.BaseBrightness &&
            left.PeakBrightness == right.PeakBrightness &&
            left.IntervalMs == right.IntervalMs &&
            Math.Abs(left.NoiseGate - right.NoiseGate) < 0.001 &&
            Math.Abs(left.BeatThreshold - right.BeatThreshold) < 0.001 &&
            left.PeakHoldMs == right.PeakHoldMs &&
            left.FollowSystemVolume == right.FollowSystemVolume &&
            left.EqEnabled == right.EqEnabled &&
            left.EqLowHz == right.EqLowHz &&
            left.EqHighHz == right.EqHighHz &&
            left.FreqHueOffset == right.FreqHueOffset &&
            left.EqualLoudness == right.EqualLoudness &&
            left.DynamicRange == right.DynamicRange;
    }

    private async Task FlashStartupAsync(CancellationToken stoppingToken)
    {
        try
        {
            for (var i = 0; i < 2; i++)
            {
                _device.SetColor(new RgbColor(255, 255, 255));
                await Task.Delay(120, stoppingToken);
                _device.SetColor(RgbColor.Black);
                await Task.Delay(120, stoppingToken);
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or SEHException)
        {
            _logger.LogWarning(ex, "Startup flash could not be sent to the keyboard.");
        }
    }

    private static void ApplySchedule(KeyboardSettings settings)
    {
        if (!settings.Schedule.Enabled)
        {
            return;
        }

        var now = TimeOnly.FromDateTime(DateTime.Now);
        var rule = settings.Schedule.Rules.FirstOrDefault(item => item.Enabled && item.IsActive(now));
        if (rule is null)
        {
            return;
        }

        settings.Enabled = true;
        settings.Effect = rule.Effect;
    }

    private static void ApplyAppProfiles(KeyboardSettings settings)
    {
        if (!settings.AppProfiles.Enabled || settings.AppProfiles.Rules.Count == 0)
        {
            return;
        }

        var foreground = ForegroundAppState.Load();
        if (foreground is null || DateTimeOffset.UtcNow - foreground.UpdatedUtc > TimeSpan.FromSeconds(10))
        {
            return;
        }

        var processName = foreground.ProcessName;
        if (string.IsNullOrWhiteSpace(processName))
        {
            return;
        }

        var rule = settings.AppProfiles.Rules.FirstOrDefault(item => item.Matches(processName));
        if (rule is null)
        {
            return;
        }

        settings.Enabled = true;
        // AppProfile 不再支持 TargetEffect=Music（已从 EffectType 中移除，规则只能切到灯效模式下的 Static/Breathing）。
        // 用户希望"前台某进程时切到音乐"需要在未来的 AppProfile 改进中重新设计。
        settings.Effect = rule.BuildEffect();
    }

    private static void ApplyIdleDim(KeyboardSettings settings)
    {
        if (!settings.IdleDim.Enabled)
        {
            return;
        }

        if (WindowsIdleTime.GetIdleTime().TotalSeconds < settings.IdleDim.AfterSeconds)
        {
            return;
        }

        if (settings.IdleDim.TurnOff)
        {
            settings.Effect.Type = EffectType.Off;
            return;
        }

        settings.Brightness = Math.Min(settings.Brightness, settings.IdleDim.Brightness);
    }

    private void EnsureConfigWatcher()
    {
        Directory.CreateDirectory(AppPaths.ProgramDataDirectory);
        _watcher = new FileSystemWatcher(AppPaths.ProgramDataDirectory)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.CreationTime | NotifyFilters.Size | NotifyFilters.FileName,
            EnableRaisingEvents = true
        };

        _watcher.Changed += (_, args) => MarkSettingsChanged(args.Name);
        _watcher.Created += (_, args) => MarkSettingsChanged(args.Name);
        _watcher.Deleted += (_, args) => MarkSettingsChanged(args.Name);
        _watcher.Renamed += (_, args) => MarkSettingsChanged(args.Name);
    }

    private void MarkSettingsChanged(string? fileName)
    {
        if (string.Equals(fileName, AppPaths.SettingsFileName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(fileName, AppPaths.NotificationFlashStateFileName, StringComparison.OrdinalIgnoreCase))
        {
            _settingsChanged = true;
        }
    }

    private async Task WaitForSettingsChangeAsync(int pollIntervalMs, CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested && !_settingsChanged)
        {
            await Task.Delay(pollIntervalMs, stoppingToken);
        }
    }

    private void OnAudioSourceChanged(object? sender, AudioSourceChangedEventArgs e)
    {
        // 这个回调可能在 NAudio COM 回调线程里触发；文件 IO 必须脱离它
        var snapshot = new AudioSourceStatusInfo
        {
            Status = e.Status,
            DeviceFriendlyName = e.DeviceFriendlyName,
            DeviceId = e.DeviceId,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        System.Threading.ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                AudioSourceStatusFile.Write(snapshot);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to write audio source status file");
            }
        });
    }
}
