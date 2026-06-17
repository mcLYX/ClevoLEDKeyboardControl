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

    public (double Hue, double Saturation, double Value) ToHsv()
    {
        var r = R / 255.0;
        var g = G / 255.0;
        var b = B / 255.0;

        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var delta = max - min;

        double hue;
        if (delta < 0.00001)
        {
            hue = 0;
        }
        else if (Math.Abs(max - r) < 0.00001)
        {
            hue = 60 * (((g - b) / delta) % 6);
        }
        else if (Math.Abs(max - g) < 0.00001)
        {
            hue = 60 * (((b - r) / delta) + 2);
        }
        else
        {
            hue = 60 * (((r - g) / delta) + 4);
        }

        hue = (hue + 360) % 360;

        var saturation = max < 0.00001 ? 0 : delta / max;
        return (hue, saturation, max);
    }

    public static RgbColor LerpHsv(RgbColor from, RgbColor to, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        var (h1, s1, v1) = from.ToHsv();
        var (h2, s2, v2) = to.ToHsv();

        var hueDiff = h2 - h1;
        if (hueDiff > 180)
        {
            hueDiff -= 360;
        }
        else if (hueDiff < -180)
        {
            hueDiff += 360;
        }

        var h = h1 + hueDiff * amount;
        h = (h + 360) % 360;
        var s = s1 + (s2 - s1) * amount;
        var v = v1 + (v2 - v1) * amount;

        return FromHsv(h, s, v);
    }
}
