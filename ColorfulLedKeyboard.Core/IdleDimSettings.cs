namespace ColorfulLedKeyboard.Core;

public sealed class IdleDimSettings
{
    public bool Enabled { get; set; }

    public int AfterSeconds { get; set; } = 300;

    public int Brightness { get; set; } = 15;

    public bool TurnOff { get; set; }

    public IdleDimSettings Normalize()
    {
        AfterSeconds = Math.Clamp(AfterSeconds, 30, 86400);
        Brightness = Math.Clamp(Brightness, 0, 100);
        return this;
    }
}
