using ColorfulLedKeyboard.Core;

namespace ColorfulLedKeyboard.Service;

internal sealed class MusicBrightnessController
{
    private double _current;

    public int NextBrightness(MusicSettings settings, double level)
    {
        settings.Normalize();
        level = Math.Clamp(level * settings.Sensitivity, 0, 1);

        var intervalSeconds = settings.IntervalMs / 1000d;
        var timeConstantMs = level > _current ? settings.AttackMs : settings.ReleaseMs;
        var alpha = 1 - Math.Exp(-intervalSeconds / Math.Max(0.001, timeConstantMs / 1000d));
        _current += (level - _current) * alpha;

        var brightness = settings.BaseBrightness +
            (settings.PeakBrightness - settings.BaseBrightness) * Math.Pow(_current, 0.65);

        return (int)Math.Clamp(Math.Round(brightness), settings.BaseBrightness, settings.PeakBrightness);
    }
}
