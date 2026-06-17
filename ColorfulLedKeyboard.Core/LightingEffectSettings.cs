namespace ColorfulLedKeyboard.Core;

public sealed class LightingEffectSettings
{
    public EffectType Type { get; set; } = EffectType.Rainbow;

    public string Color { get; set; } = "#FF0000";

    public int Step { get; set; } = 3;

    public int IntervalMs { get; set; } = 40;

    public int PeriodMs { get; set; } = EffectPresetSettings.DefaultPeriodMs;

    public int MinimumBrightness { get; set; } = 0;

    public bool HardBlink { get; set; }

    public bool CustomSequenceColorsEnabled { get; set; }

    public List<SequenceColor> Sequence { get; set; } =
    [
        new SequenceColor { Color = "#FF0000" },
        new SequenceColor { Color = "#0000FF" }
    ];

    public int GradientHoldMs { get; set; } = 1500;

    public int GradientTransitionMs { get; set; } = 1500;

    public int GradientMinBrightnessPercent { get; set; } = 100;

    public GradientAlgorithm GradientAlgorithm { get; set; } = GradientAlgorithm.Rgb;

    /// <summary>
    /// 全局亮度 gamma 校正，影响所有灯效的亮度映射。1.0=线性，&gt;1 中间值变暗（对比度增加），&lt;1 中间值变亮。
    /// </summary>
    public double BrightnessGamma { get; set; } = 1.0;

    public MusicSettings Music { get; set; } = new();

    public LightingEffectSettings Normalize()
    {
        if (!Enum.IsDefined(Type))
        {
            Type = EffectType.Rainbow;
        }

        Color = NormalizeHex(Color, "#FF0000");
        Step = Math.Clamp(Step, 1, 20);
        IntervalMs = Math.Clamp(IntervalMs, 20, 500);
        PeriodMs = Math.Clamp(PeriodMs, 300, 30000);
        MinimumBrightness = Math.Clamp(MinimumBrightness, 0, 100);
        GradientHoldMs = Math.Clamp(GradientHoldMs, 0, 30000);
        GradientTransitionMs = Math.Clamp(GradientTransitionMs, 0, 30000);
        GradientMinBrightnessPercent = Math.Clamp(GradientMinBrightnessPercent, 0, 100);
        BrightnessGamma = Math.Clamp(BrightnessGamma, 0.8, 3.0);
        if (!Enum.IsDefined(GradientAlgorithm))
        {
            GradientAlgorithm = GradientAlgorithm.Rgb;
        }
        Sequence = NormalizeSequence(Sequence);
        Music ??= new MusicSettings();
        Music.Normalize();
        return this;
    }

    private static List<SequenceColor> NormalizeSequence(List<SequenceColor>? sequence)
    {
        var normalized = (sequence ?? [])
            .Select(item => item.Normalize())
            .Where(item => !string.IsNullOrWhiteSpace(item.Color))
            .ToList();

        if (normalized.Count > 0)
        {
            return normalized;
        }

        return
        [
            new SequenceColor { Color = "#FF0000" },
            new SequenceColor { Color = "#0000FF" }
        ];
    }

    internal static string NormalizeHex(string value, string fallback)
    {
        try
        {
            return RgbColor.FromHex(value).Hex;
        }
        catch (FormatException)
        {
            return fallback;
        }
    }
}

public sealed class SequenceColor
{
    public string Color { get; set; } = "#FF0000";

    public int HoldMs { get; set; } = EffectPresetSettings.DefaultPeriodMs;

    public int TransitionMs { get; set; } = 1200;

    public bool Breathing { get; set; } = true;

    public SequenceColor Normalize()
    {
        Color = LightingEffectSettings.NormalizeHex(Color, "#FF0000");
        HoldMs = Math.Clamp(HoldMs, 0, 30000);
        TransitionMs = Math.Clamp(TransitionMs, 0, 30000);
        return this;
    }
}
