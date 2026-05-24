using System.Text.Json.Serialization;

namespace ColorfulLedKeyboard.Core;

public readonly record struct RgbColor(byte R, byte G, byte B)
{
    [JsonIgnore]
    public string Hex => $"#{R:X2}{G:X2}{B:X2}";

    public static RgbColor Black => new(0, 0, 0);

    public static RgbColor FromHex(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new FormatException("Color value is empty.");
        }

        var hex = value.Trim().TrimStart('#');
        if (hex.Length != 6)
        {
            throw new FormatException("Color value must use #RRGGBB format.");
        }

        return new RgbColor(
            Convert.ToByte(hex[..2], 16),
            Convert.ToByte(hex.Substring(2, 2), 16),
            Convert.ToByte(hex.Substring(4, 2), 16));
    }

    public RgbColor Scale(int brightness)
    {
        brightness = Math.Clamp(brightness, 0, 100);
        return new RgbColor(
            (byte)Math.Clamp(R * brightness / 100, 0, 255),
            (byte)Math.Clamp(G * brightness / 100, 0, 255),
            (byte)Math.Clamp(B * brightness / 100, 0, 255));
    }

    public static RgbColor Lerp(RgbColor from, RgbColor to, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        return new RgbColor(
            (byte)Math.Round(from.R + (to.R - from.R) * amount),
            (byte)Math.Round(from.G + (to.G - from.G) * amount),
            (byte)Math.Round(from.B + (to.B - from.B) * amount));
    }

    public static RgbColor FromHsv(double hue, double saturation, double value)
    {
        hue = ((hue % 360) + 360) % 360;
        saturation = Math.Clamp(saturation, 0, 1);
        value = Math.Clamp(value, 0, 1);

        var chroma = value * saturation;
        var x = chroma * (1 - Math.Abs(hue / 60 % 2 - 1));
        var m = value - chroma;

        var (r, g, b) = hue switch
        {
            < 60 => (chroma, x, 0d),
            < 120 => (x, chroma, 0d),
            < 180 => (0d, chroma, x),
            < 240 => (0d, x, chroma),
            < 300 => (x, 0d, chroma),
            _ => (chroma, 0d, x)
        };

        return new RgbColor(
            (byte)Math.Round((r + m) * 255),
            (byte)Math.Round((g + m) * 255),
            (byte)Math.Round((b + m) * 255));
    }
}
