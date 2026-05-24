namespace ColorfulLedKeyboard.Core;

public static class LightingPresets
{
    public static void ApplyWarmWhite(KeyboardSettings settings)
    {
        settings.Enabled = true;
        settings.Brightness = 45;
        settings.Effect = new LightingEffectSettings
        {
            Type = EffectType.Static,
            Color = "#FFD2A1"
        };
        settings.Normalize();
    }

    public static void ApplyNeutralWhite(KeyboardSettings settings)
    {
        settings.Enabled = true;
        settings.Brightness = 55;
        settings.Effect = new LightingEffectSettings
        {
            Type = EffectType.Static,
            Color = "#FFFFFF"
        };
        settings.Normalize();
    }

    public static void ApplyCoolWhite(KeyboardSettings settings)
    {
        settings.Enabled = true;
        settings.Brightness = 60;
        settings.Effect = new LightingEffectSettings
        {
            Type = EffectType.Static,
            Color = "#CFE8FF"
        };
        settings.Normalize();
    }

    public static void ApplyRedBluePulse(KeyboardSettings settings)
    {
        settings.Enabled = true;
        settings.Brightness = 70;
        settings.Effect = new LightingEffectSettings
        {
            Type = EffectType.Sequence,
            IntervalMs = 35,
            Sequence =
            [
                new SequenceColor { Color = "#FF0000", HoldMs = 200, TransitionMs = 1200, Breathing = true },
                new SequenceColor { Color = "#0000FF", HoldMs = 200, TransitionMs = 1200, Breathing = true }
            ]
        };
        settings.Normalize();
    }

    public static void ApplySoftRainbow(KeyboardSettings settings)
    {
        settings.Enabled = true;
        settings.Brightness = 45;
        settings.Effect = new LightingEffectSettings
        {
            Type = EffectType.Rainbow,
            Step = 2,
            IntervalMs = 55
        };
        settings.Normalize();
    }
}
