namespace ColorfulLedKeyboard.Core;

public static class LightingPresets
{
    public static void ApplyWarmWhite(KeyboardSettings settings)
    {
        settings.Enabled = true;
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
        settings.Effect = new LightingEffectSettings
        {
            Type = EffectType.Sequence,
            PeriodMs = EffectPresetSettings.DefaultPeriodMs,
            Sequence =
            [
                new SequenceColor { Color = "#FF0000", HoldMs = EffectPresetSettings.DefaultPeriodMs, TransitionMs = 0, Breathing = true },
                new SequenceColor { Color = "#0000FF", HoldMs = EffectPresetSettings.DefaultPeriodMs, TransitionMs = 0, Breathing = true }
            ]
        };
        settings.Normalize();
    }

    public static void ApplySoftRainbow(KeyboardSettings settings)
    {
        settings.Enabled = true;
        settings.Effect = new LightingEffectSettings
        {
            Type = EffectType.Rainbow,
            PeriodMs = EffectPresetSettings.DefaultPeriodMs,
            CustomSequenceColorsEnabled = true,
            Sequence =
            [
                new SequenceColor { Color = "#FF0000", HoldMs = EffectPresetSettings.DefaultPeriodMs, TransitionMs = 0, Breathing = false },
                new SequenceColor { Color = "#FFFF00", HoldMs = EffectPresetSettings.DefaultPeriodMs, TransitionMs = 0, Breathing = false },
                new SequenceColor { Color = "#00FF00", HoldMs = EffectPresetSettings.DefaultPeriodMs, TransitionMs = 0, Breathing = false },
                new SequenceColor { Color = "#00FFFF", HoldMs = EffectPresetSettings.DefaultPeriodMs, TransitionMs = 0, Breathing = false },
                new SequenceColor { Color = "#0000FF", HoldMs = EffectPresetSettings.DefaultPeriodMs, TransitionMs = 0, Breathing = false },
                new SequenceColor { Color = "#FF00FF", HoldMs = EffectPresetSettings.DefaultPeriodMs, TransitionMs = 0, Breathing = false }
            ]
        };
        settings.Normalize();
    }
}
