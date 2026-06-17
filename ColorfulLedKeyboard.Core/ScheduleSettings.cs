namespace ColorfulLedKeyboard.Core;

public sealed class ScheduleSettings
{
    public bool Enabled { get; set; }

    public List<ScheduleRule> Rules { get; set; } =
    [
        new ScheduleRule
        {
            Name = "Evening",
            Start = "19:00",
            End = "23:30",
            Brightness = 35,
            Effect = new LightingEffectSettings
            {
                Type = EffectType.Static,
                Color = "#FFD2A1"
            }
        },
        new ScheduleRule
        {
            Name = "Night",
            Start = "23:30",
            End = "07:00",
            Enabled = false,
            Brightness = 0,
            Effect = new LightingEffectSettings
            {
                Type = EffectType.Off
            }
        }
    ];

    public ScheduleSettings Normalize()
    {
        Rules = (Rules ?? [])
            .Select(rule => rule.Normalize())
            .ToList();
        return this;
    }
}

public sealed class ScheduleRule
{
    public string Name { get; set; } = "Rule";

    public string Start { get; set; } = "19:00";

    public string End { get; set; } = "23:30";

    public bool Enabled { get; set; } = true;

    public int Brightness { get; set; } = 50;

    public LightingEffectSettings Effect { get; set; } = new();

    public ScheduleRule Normalize()
    {
        Name = string.IsNullOrWhiteSpace(Name) ? "Rule" : Name.Trim();
        Start = NormalizeTime(Start, "19:00");
        End = NormalizeTime(End, "23:30");
        Brightness = Math.Clamp(Brightness, 0, 100);
        Effect ??= new LightingEffectSettings();
        Effect.Normalize();
        return this;
    }

    public bool IsActive(TimeOnly now)
    {
        var start = TimeOnly.Parse(Start);
        var end = TimeOnly.Parse(End);

        if (start == end)
        {
            return true;
        }

        return start < end
            ? now >= start && now < end
            : now >= start || now < end;
    }

    private static string NormalizeTime(string value, string fallback)
    {
        return TimeOnly.TryParse(value, out var time) ? time.ToString("HH:mm") : fallback;
    }
}
