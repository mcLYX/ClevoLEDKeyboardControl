namespace ColorfulLedKeyboard.Core;

public sealed class NotificationFlashSettings
{
    public bool Enabled { get; set; }

    public string Color { get; set; } = "#FFFFFF";

    public int Pulses { get; set; } = 2;

    public int PulseMs { get; set; } = 90;

    public int CooldownSeconds { get; set; } = 8;

    public NotificationFlashSettings Normalize()
    {
        Color = LightingEffectSettings.NormalizeHex(Color, "#FFFFFF");
        Pulses = Math.Clamp(Pulses, 1, 5);
        PulseMs = Math.Clamp(PulseMs, 40, 400);
        CooldownSeconds = Math.Clamp(CooldownSeconds, 1, 60);
        return this;
    }
}
