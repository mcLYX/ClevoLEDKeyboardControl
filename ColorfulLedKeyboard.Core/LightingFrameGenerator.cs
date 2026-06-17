namespace ColorfulLedKeyboard.Core;

public sealed class LightingFrameGenerator
{
    private readonly KeyboardSettings _settings;
    private readonly DateTimeOffset _startedAt = DateTimeOffset.UtcNow;

    public LightingFrameGenerator(KeyboardSettings settings)
    {
        _settings = settings.Normalize();
    }

    public int IntervalMs => Math.Clamp(_settings.Effect.IntervalMs, 20, 500);

    public RgbColor Next()
    {
        return Next(_settings.Brightness);
    }

    public RgbColor Next(int brightness)
    {
        var elapsedMs = (DateTimeOffset.UtcNow - _startedAt).TotalMilliseconds;
        return NextAtElapsed(brightness, elapsedMs);
    }

    public RgbColor NextAtElapsed(int brightness, double elapsedMs)
    {
        var color = _settings.Effect.Type switch
        {
            EffectType.Off => RgbColor.Black,
            EffectType.Static => RgbColor.FromHex(_settings.Effect.Color),
            EffectType.Breathing => Breathing(RgbColor.FromHex(_settings.Effect.Color), elapsedMs),
            EffectType.Sequence => Sequence(elapsedMs),
            EffectType.Rainbow => Rainbow(elapsedMs),
            EffectType.Pulse => PatternColor(elapsedMs, PulseFactor),
            EffectType.Heartbeat => PatternColor(elapsedMs, HeartbeatFactor),
            EffectType.GradientCycle => GradientCycle(elapsedMs),
            _ => RgbColor.Black
        };

        return color.Scale(Math.Clamp(brightness, 0, 100));
    }

    private RgbColor Rainbow(double elapsedMs)
    {
        if (_settings.Effect.CustomSequenceColorsEnabled && _settings.Effect.Sequence.Count > 0)
        {
            return Sequence(elapsedMs, allowBreathing: false);
        }

        var degreesPerSecond = Math.Clamp(_settings.Effect.Step, 1, 20) * 9;
        var absoluteSeconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000d;
        var hue = absoluteSeconds * degreesPerSecond;
        return RgbColor.FromHsv(hue, 1, 1);
    }

    private RgbColor Breathing(RgbColor color, double elapsedMs)
    {
        if (_settings.Effect.HardBlink)
        {
            var on = elapsedMs % _settings.Effect.PeriodMs < _settings.Effect.PeriodMs / 2d;
            return on ? color : RgbColor.Black;
        }

        var phase = elapsedMs % _settings.Effect.PeriodMs / _settings.Effect.PeriodMs;
        var wave = (1 - Math.Cos(phase * Math.PI * 2)) / 2d;
        var min = _settings.Effect.MinimumBrightness / 100d;
        var factor = min + (1 - min) * wave;
        // 应用全局 gamma 校正到亮度调制因子
        factor = ApplyGamma(factor);
        return color.Scale((int)Math.Round(factor * 100));
    }

    private RgbColor Sequence(double elapsedMs) => Sequence(elapsedMs, allowBreathing: true);

    private RgbColor Sequence(double elapsedMs, bool allowBreathing)
    {
        var sequence = _settings.Effect.Sequence;
        if (sequence.Count == 0)
        {
            return RgbColor.Black;
        }

        var totalMs = sequence.Sum(SegmentDurationMs);
        var cursor = elapsedMs % totalMs;

        for (var i = 0; i < sequence.Count; i++)
        {
            var current = sequence[i];
            var next = sequence[(i + 1) % sequence.Count];
            var holdMs = Math.Max(0, current.HoldMs);
            var transitionMs = Math.Max(0, current.TransitionMs);
            var segmentMs = Math.Max(1, holdMs + transitionMs);

            if (cursor >= segmentMs)
            {
                cursor -= segmentMs;
                continue;
            }

            var currentColor = RgbColor.FromHex(current.Color);
            if (allowBreathing && current.Breathing)
            {
                var local = cursor % segmentMs;
                return BreathingColor(currentColor, local, segmentMs);
            }

            if (cursor <= holdMs || transitionMs == 0)
            {
                return currentColor;
            }

            var amount = (cursor - holdMs) / transitionMs;
            return RgbColor.Lerp(currentColor, RgbColor.FromHex(next.Color), amount);
        }

        return RgbColor.FromHex(sequence[0].Color);
    }

    private static double SegmentDurationMs(SequenceColor item) =>
        Math.Max(1, Math.Max(0, item.HoldMs) + Math.Max(0, item.TransitionMs));

    private RgbColor BreathingColor(RgbColor color, double elapsedMs, double periodMs)
    {
        var phase = elapsedMs % periodMs / periodMs;
        var wave = (1 - Math.Cos(phase * Math.PI * 2)) / 2d;
        return color.Scale((int)Math.Round(ApplyGamma(wave) * 100));
    }

    private RgbColor PatternColor(double elapsedMs, Func<double, double> factorAtPhase)
    {
        var sequence = _settings.Effect.Sequence;
        if (sequence.Count == 0)
        {
            return RgbColor.Black;
        }

        var periodMs = Math.Max(1, _settings.Effect.PeriodMs);
        var patternIndex = (long)Math.Floor(Math.Max(0, elapsedMs) / periodMs);
        var color = RgbColor.FromHex(sequence[(int)(patternIndex % sequence.Count)].Color);
        var phase = Math.Max(0, elapsedMs) % periodMs / periodMs;
        return color.Scale((int)Math.Round(ApplyGamma(Math.Clamp(factorAtPhase(phase), 0, 1)) * 100));
    }

    /// <summary>
    /// 全局亮度 gamma 校正。gamma>1 中间值变暗（对比度增加），gamma&lt;1 中间值变亮。
    /// </summary>
    private double ApplyGamma(double factor)
    {
        var gamma = Math.Clamp(_settings.Effect.BrightnessGamma, 0.8, 3.0);
        return Math.Pow(Math.Clamp(factor, 0, 1), gamma);
    }

    private static double PulseFactor(double phase)
    {
        if (phase < 0.20)
        {
            return SmoothStep(phase / 0.20);
        }

        if (phase < 0.85)
        {
            return 1 - SmoothStep((phase - 0.20) / 0.65);
        }

        return 0;
    }

    private static double HeartbeatFactor(double phase)
    {
        if (phase < 0.25)
        {
            return Math.Sin(Math.PI * phase / 0.25);
        }

        if (phase >= 0.35 && phase < 0.60)
        {
            return Math.Sin(Math.PI * (phase - 0.35) / 0.25) * 0.65;
        }

        return 0;
    }

    private RgbColor GradientCycle(double elapsedMs)
    {
        var sequence = _settings.Effect.Sequence;
        if (sequence.Count == 0)
        {
            return RgbColor.Black;
        }

        var holdMs = Math.Max(0, _settings.Effect.GradientHoldMs);
        var transitionMs = Math.Max(0, _settings.Effect.GradientTransitionMs);
        var segmentMs = holdMs + transitionMs;
        if (segmentMs <= 0)
        {
            return RgbColor.FromHex(sequence[0].Color).Scale(_settings.Brightness);
        }

        var totalMs = segmentMs * sequence.Count;
        var cursor = elapsedMs % totalMs;
        var index = (int)(cursor / segmentMs);
        var localCursor = cursor % segmentMs;

        var currentColor = RgbColor.FromHex(sequence[index].Color);

        if (localCursor <= holdMs)
        {
            return currentColor.Scale(_settings.Brightness);
        }

        var nextIndex = (index + 1) % sequence.Count;
        var nextColor = RgbColor.FromHex(sequence[nextIndex].Color);
        var phase = (localCursor - holdMs) / transitionMs;

        RgbColor interpolated;
        if (_settings.Effect.GradientAlgorithm == GradientAlgorithm.Hsv)
        {
            interpolated = RgbColor.LerpHsv(currentColor, nextColor, phase);
        }
        else
        {
            interpolated = RgbColor.Lerp(currentColor, nextColor, phase);
        }

        var minBrightnessFactor = _settings.Effect.GradientMinBrightnessPercent / 100.0;
        var brightnessFactor = minBrightnessFactor + (1 - minBrightnessFactor) * (1 + Math.Cos(phase * Math.PI * 2)) / 2;
        // 应用全局 gamma 校正到亮度调制因子
        brightnessFactor = ApplyGamma(brightnessFactor);
        var finalBrightness = (int)Math.Round(_settings.Brightness * brightnessFactor);

        return interpolated.Scale(finalBrightness);
    }

    private static double SmoothStep(double value)
    {
        value = Math.Clamp(value, 0, 1);
        return value * value * (3 - 2 * value);
    }
}
