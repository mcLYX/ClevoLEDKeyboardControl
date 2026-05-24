using System.Runtime.InteropServices;

namespace ColorfulLedKeyboard.Core;

public sealed class DchuKeyboardDevice
{
    private static readonly int[] Zones = [1, 2, 3];

    [DllImport("InsydeDCHU.dll")]
    private static extern int SetDCHU_Data(int command, byte[] buffer, int length);

    public void SetAllZones(RgbColor color)
    {
        foreach (var zone in Zones)
        {
            SetZone(zone, color);
        }
    }

    public void SetZone(int zone, RgbColor color)
    {
        var commandByte = zone switch
        {
            1 => 240,
            2 => 241,
            3 => 242,
            _ => throw new ArgumentOutOfRangeException(nameof(zone), zone, "Only zones 1, 2 and 3 are supported.")
        };

        var encodedColor = (uint)(color.B << 16 | color.R << 8 | color.G);
        if (color is { R: 0, G: 255, B: 127 })
        {
            encodedColor = (uint)(4587520 | color.R << 8 | color.G);
        }

        var payload = BitConverter.GetBytes((long)commandByte << 24 | encodedColor);
        SetDCHU_Data(103, payload, 4);
    }
}
