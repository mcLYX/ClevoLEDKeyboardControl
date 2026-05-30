namespace ColorfulLedKeyboard.Core;

public sealed class MusicSettings
{
    public string PresetName { get; set; } = "流行";

    public MusicResponseMode ResponseMode { get; set; } = MusicResponseMode.LevelColor;

    public bool LevelColorEnabled { get; set; }

    public string LowColor { get; set; } = "#0040FF";

    public string HighColor { get; set; } = "#FF0040";

    public double Sensitivity { get; set; } = 1.5;

    public int AttackMs { get; set; } = 35;

    public int ReleaseMs { get; set; } = 180;

    public int BaseBrightness { get; set; } = 5;

    public int PeakBrightness { get; set; } = 100;

    public int IntervalMs { get; set; } = 25;

    public double NoiseGate { get; set; } = 0.04;

    public double BeatThreshold { get; set; } = 0.12;

    public int PeakHoldMs { get; set; } = 90;

    public bool EqEnabled { get; set; } = true;

    public int EqLowHz { get; set; } = 60;

    public int EqHighHz { get; set; } = 180;

    public SpotifySettings Spotify { get; set; } = new();

    public List<MusicPreset> CustomPresets { get; set; } = [];

    public MusicSettings Normalize()
    {
        PresetName = string.IsNullOrWhiteSpace(PresetName) ? "流行" : PresetName.Trim();
        if (!Enum.IsDefined(ResponseMode))
        {
            ResponseMode = LevelColorEnabled ? MusicResponseMode.LevelColor : MusicResponseMode.BrightnessPulse;
        }

        LowColor = LightingEffectSettings.NormalizeHex(LowColor, "#0040FF");
        HighColor = LightingEffectSettings.NormalizeHex(HighColor, "#FF0040");
        Sensitivity = Math.Clamp(Sensitivity, 0.5, 2.0);
        AttackMs = Math.Clamp(AttackMs, 10, 1000);
        ReleaseMs = Math.Clamp(ReleaseMs, 20, 3000);
        BaseBrightness = Math.Clamp(BaseBrightness, 0, 100);
        PeakBrightness = Math.Clamp(PeakBrightness, BaseBrightness, 100);
        IntervalMs = Math.Clamp(IntervalMs, 15, 200);
        NoiseGate = Math.Clamp(NoiseGate, 0, 0.5);
        BeatThreshold = Math.Clamp(BeatThreshold, 0.02, 0.8);
        PeakHoldMs = Math.Clamp(PeakHoldMs, 0, 500);
        EqLowHz = Math.Clamp(EqLowHz, 20, 1000);
        EqHighHz = Math.Clamp(EqHighHz, EqLowHz + 10, 8000);
        Spotify ??= new SpotifySettings();
        Spotify.Normalize();
        LevelColorEnabled = ResponseMode == MusicResponseMode.LevelColor;
        CustomPresets = (CustomPresets ?? [])
            .Select(preset => preset.Normalize())
            .Where(preset => !string.IsNullOrWhiteSpace(preset.Name))
            .Where(preset => !IsBuiltInPresetName(preset.Name))
            .GroupBy(preset => preset.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .Take(8)
            .ToList();
        return this;
    }

    public void ApplyPreset(MusicPreset preset)
    {
        PresetName = preset.Name;
        ResponseMode = preset.ResponseMode;
        LevelColorEnabled = ResponseMode == MusicResponseMode.LevelColor;
        LowColor = preset.LowColor;
        HighColor = preset.HighColor;
        Sensitivity = preset.Sensitivity;
        AttackMs = preset.AttackMs;
        ReleaseMs = preset.ReleaseMs;
        BaseBrightness = preset.BaseBrightness;
        PeakBrightness = preset.PeakBrightness;
        IntervalMs = preset.IntervalMs;
        NoiseGate = preset.NoiseGate;
        BeatThreshold = preset.BeatThreshold;
        PeakHoldMs = preset.PeakHoldMs;
        EqEnabled = preset.EqEnabled;
        EqLowHz = preset.EqLowHz;
        EqHighHz = preset.EqHighHz;
        Normalize();
    }

    public MusicPreset ToPreset(string name)
    {
        return new MusicPreset
        {
            Name = name,
            ResponseMode = ResponseMode,
            LowColor = LowColor,
            HighColor = HighColor,
            Sensitivity = Sensitivity,
            AttackMs = AttackMs,
            ReleaseMs = ReleaseMs,
            BaseBrightness = BaseBrightness,
            PeakBrightness = PeakBrightness,
            IntervalMs = IntervalMs,
            NoiseGate = NoiseGate,
            BeatThreshold = BeatThreshold,
            PeakHoldMs = PeakHoldMs,
            EqEnabled = EqEnabled,
            EqLowHz = EqLowHz,
            EqHighHz = EqHighHz
        }.Normalize();
    }

    public static bool IsBuiltInPresetName(string? name)
    {
        return !string.IsNullOrWhiteSpace(name) &&
            BuiltInPresets.Any(preset => string.Equals(preset.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    public static IReadOnlyList<MusicPreset> BuiltInPresets { get; } =
    [
        new MusicPreset
        {
            Name = "自定义",
            ResponseMode = MusicResponseMode.BrightnessPulse,
            LowColor = "#0040FF",
            HighColor = "#FF0040",
            Sensitivity = 1.3,
            AttackMs = 35,
            ReleaseMs = 160,
            BaseBrightness = 5,
            PeakBrightness = 100,
            NoiseGate = 0.07,
            BeatThreshold = 0.16,
            PeakHoldMs = 50,
            EqEnabled = true,
            EqLowHz = 50,
            EqHighHz = 220
        }.Normalize(),
        new MusicPreset
        {
            Name = "流行",
            ResponseMode = MusicResponseMode.LevelColor,
            LowColor = "#0040FF",
            HighColor = "#FF2A5F",
            Sensitivity = 1.15,
            AttackMs = 25,
            ReleaseMs = 150,
            BaseBrightness = 8,
            PeakBrightness = 100,
            NoiseGate = 0.08,
            BeatThreshold = 0.18,
            PeakHoldMs = 50,
            EqEnabled = true,
            EqLowHz = 50,
            EqHighHz = 220
        }.Normalize(),
        new MusicPreset
        {
            Name = "摇滚",
            ResponseMode = MusicResponseMode.BrightnessPulse,
            LowColor = "#FF2020",
            HighColor = "#FFFFFF",
            Sensitivity = 1.35,
            AttackMs = 15,
            ReleaseMs = 110,
            BaseBrightness = 18,
            PeakBrightness = 100,
            NoiseGate = 0.09,
            BeatThreshold = 0.16,
            PeakHoldMs = 45,
            EqEnabled = true,
            EqLowHz = 55,
            EqHighHz = 260
        }.Normalize(),
        new MusicPreset
        {
            Name = "电子",
            ResponseMode = MusicResponseMode.LevelColor,
            LowColor = "#00E5FF",
            HighColor = "#FF00C8",
            Sensitivity = 1.45,
            AttackMs = 15,
            ReleaseMs = 80,
            BaseBrightness = 5,
            PeakBrightness = 100,
            NoiseGate = 0.08,
            BeatThreshold = 0.15,
            PeakHoldMs = 35,
            EqEnabled = true,
            EqLowHz = 40,
            EqHighHz = 220
        }.Normalize()
    ];
}

public enum MusicResponseMode
{
    LevelColor = 0,
    BrightnessPulse = 1
}

public sealed class MusicPreset
{
    public string Name { get; set; } = "自定义";

    public MusicResponseMode ResponseMode { get; set; } = MusicResponseMode.LevelColor;

    public string LowColor { get; set; } = "#0040FF";

    public string HighColor { get; set; } = "#FF0040";

    public double Sensitivity { get; set; } = 1.5;

    public int AttackMs { get; set; } = 35;

    public int ReleaseMs { get; set; } = 180;

    public int BaseBrightness { get; set; } = 5;

    public int PeakBrightness { get; set; } = 100;

    public int IntervalMs { get; set; } = 25;

    public double NoiseGate { get; set; } = 0.04;

    public double BeatThreshold { get; set; } = 0.12;

    public int PeakHoldMs { get; set; } = 90;

    public bool EqEnabled { get; set; } = true;

    public int EqLowHz { get; set; } = 60;

    public int EqHighHz { get; set; } = 180;

    public MusicPreset Normalize()
    {
        Name = string.IsNullOrWhiteSpace(Name) ? "" : Name.Trim();
        if (!Enum.IsDefined(ResponseMode))
        {
            ResponseMode = MusicResponseMode.LevelColor;
        }

        LowColor = LightingEffectSettings.NormalizeHex(LowColor, "#0040FF");
        HighColor = LightingEffectSettings.NormalizeHex(HighColor, "#FF0040");
        Sensitivity = Math.Clamp(Sensitivity, 0.5, 2.0);
        AttackMs = Math.Clamp(AttackMs, 10, 1000);
        ReleaseMs = Math.Clamp(ReleaseMs, 20, 3000);
        BaseBrightness = Math.Clamp(BaseBrightness, 0, 100);
        PeakBrightness = Math.Clamp(PeakBrightness, BaseBrightness, 100);
        IntervalMs = Math.Clamp(IntervalMs, 15, 200);
        NoiseGate = Math.Clamp(NoiseGate, 0, 0.5);
        BeatThreshold = Math.Clamp(BeatThreshold, 0.02, 0.8);
        PeakHoldMs = Math.Clamp(PeakHoldMs, 0, 500);
        EqLowHz = Math.Clamp(EqLowHz, 20, 1000);
        EqHighHz = Math.Clamp(EqHighHz, EqLowHz + 10, 8000);
        return this;
    }
}
