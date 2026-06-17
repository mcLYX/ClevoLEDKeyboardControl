using System.Runtime.InteropServices;

namespace ColorfulLedKeyboard.Service;

internal static class WindowsIdleTime
{
    public static TimeSpan GetIdleTime()
    {
        var info = new LastInputInfo
        {
            Size = (uint)Marshal.SizeOf<LastInputInfo>()
        };

        if (!GetLastInputInfo(ref info))
        {
            return TimeSpan.Zero;
        }

        var tickCount = Environment.TickCount64;
        var idleMs = tickCount - info.Time;
        return TimeSpan.FromMilliseconds(Math.Max(0, idleMs));
    }

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LastInputInfo lastInputInfo);

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint Size;
        public uint Time;
    }
}
