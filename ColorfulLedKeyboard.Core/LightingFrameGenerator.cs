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

    private static RgbColor BreathingColor(RgbColor color, double elapsedMs, double periodMs)
    {
        var phase = elapsedMs % periodMs / periodMs;
        var wave = (1 - Math.Cos(phase * Math.PI * 2)) / 2d;
        return color.Scale((int)Math.Round(wave * 100));
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
        return color.Scale((int)Math.Round(Math.Clamp(factorAtPhase(phase), 0, 1) * 100));
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

    private static double SmoothStep(double value)
    {
        value = Math.Clamp(value, 0, 1);
        return value * value * (3 - 2 * value);
    }
}
