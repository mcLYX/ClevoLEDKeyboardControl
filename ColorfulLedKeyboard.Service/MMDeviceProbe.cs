using NAudio.CoreAudioApi;

namespace ColorfulLedKeyboard.Service;

internal sealed class MMDeviceProbe : IAudioDeviceProbe, IDisposable
{
    public DeviceSnapshot? GetDefaultRenderDevice() => null;
    public DeviceSnapshot? GetDevice(string id) => null;
    public MMDevice? GetCurrentMMDevice() => null;
    public void Dispose() { }
}
