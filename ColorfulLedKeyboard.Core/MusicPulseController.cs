namespace ColorfulLedKeyboard.Core;

public sealed class MusicPulseController
{
    private double _envelope;
    private double _lastLevel;
    private DateTimeOffset _lastUpdate = DateTimeOffset.MinValue;
    private DateTimeOffset _lastBeat = DateTimeOffset.MinValue;
    private DateTimeOffset _holdUntil = DateTimeOffset.MinValue;
    private int _colorIndex = -1;

    public MusicPulseFrame Next(MusicSettings settings, double audioLevel, double systemVolumeScalar, int colorCount)
    {
        return Next(settings, audioLevel, systemVolumeScalar, colorCount, DateTimeOffset.UtcNow);
    }

    public MusicPulseFrame Next(MusicSettings settings, double audioLevel, double systemVolumeScalar, int colorCount, DateTimeOffset now)
    {
        settings.Normalize();
        colorCount = Math.Max(1, colorCount);

        var dt = _lastUpdate == DateTimeOffset.MinValue
            ? settings.IntervalMs / 1000d
            : Math.Clamp((now - _lastUpdate).TotalSeconds, 0.001, 0.25);
        _lastUpdate = now;

        var level = NormalizeLevel(settings, audioLevel, systemVolumeScalar);
        var threshold = Math.Clamp(settings.BeatThreshold, 0.02, 0.8);
        var minimumCooldownMs = Math.Clamp(settings.AttackMs + settings.PeakHoldMs + 35, 70, 360);
        var canTrigger = _lastBeat == DateTimeOffset.MinValue ||
            now - _lastBeat >= TimeSpan.FromMilliseconds(minimumCooldownMs);
        var clearRise = level >= _lastLevel + threshold * 0.22;
        var firstBeat = _lastBeat == DateTimeOffset.MinValue && level >= threshold;
        var triggered = level >= threshold && canTrigger && (clearRise || level >= 0.72 || firstBeat);

        if (triggered)
        {
            _lastBeat = now;
            _holdUntil = now.AddMilliseconds(settings.PeakHoldMs);
            _colorIndex = (_colorIndex + 1) % colorCount;
            _envelope = Math.Max(_envelope, Math.Clamp(0.55 + level * 0.45, 0.68, 1));
        }
        else if (now >= _holdUntil)
        {
            var target = level >= threshold ? level * 0.45 : 0;
            var releaseSeconds = Math.Clamp(settings.ReleaseMs / 1000d * 0.38, 0.025, 0.55);
            _envelope = Smooth(_envelope, target, dt, releaseSeconds);
        }

        _lastLevel = Smooth(_lastLevel, level, dt, level > _lastLevel ? 0.018 : 0.12);
        if (_envelope < 0.015)
        {
            _envelope = 0;
        }

        if (_colorIndex < 0)
        {
            _colorIndex = 0;
        }

        return new MusicPulseFrame(Math.Clamp(_envelope, 0, 1), _colorIndex, triggered);
    }

    private static double NormalizeLevel(MusicSettings settings, double audioLevel, double systemVolumeScalar)
    {
        var volume = settings.FollowSystemVolume
            ? Math.Clamp(systemVolumeScalar, 0, 1)
            : 1;
        var level = Math.Clamp(audioLevel * volume * settings.Sensitivity, 0, 1);
        if (level < settings.NoiseGate)
        {
            return 0;
        }

        return Math.Clamp((level - settings.NoiseGate) / Math.Max(0.001, 1 - settings.NoiseGate), 0, 1);
    }

    private static double Smooth(double current, double target, double dtSeconds, double timeConstantSeconds)
    {
        var alpha = 1 - Math.Exp(-dtSeconds / Math.Max(0.001, timeConstantSeconds));
        return current + (target - current) * alpha;
    }
}

public readonly record struct MusicPulseFrame(double Envelope, int ColorIndex, bool BeatTriggered);
