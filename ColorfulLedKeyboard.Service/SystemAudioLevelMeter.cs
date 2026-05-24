using NAudio.CoreAudioApi;

namespace ColorfulLedKeyboard.Service;

internal sealed class SystemAudioLevelMeter : IDisposable
{
    private readonly MMDeviceEnumerator _enumerator = new();
    private MMDevice? _device;
    private DateTimeOffset _nextRetry = DateTimeOffset.MinValue;

    public float GetPeakLevel()
    {
        try
        {
            EnsureDevice();
            return _device?.AudioMeterInformation.MasterPeakValue ?? 0f;
        }
        catch
        {
            ResetDevice();
            return 0f;
        }
    }

    public void Dispose()
    {
        ResetDevice();
        _enumerator.Dispose();
    }

    private void EnsureDevice()
    {
        if (_device is not null)
        {
            return;
        }

        if (DateTimeOffset.UtcNow < _nextRetry)
        {
            return;
        }

        try
        {
            _device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        }
        catch
        {
            _nextRetry = DateTimeOffset.UtcNow.AddSeconds(5);
        }
    }

    private void ResetDevice()
    {
        _device?.Dispose();
        _device = null;
        _nextRetry = DateTimeOffset.UtcNow.AddSeconds(5);
    }
}
