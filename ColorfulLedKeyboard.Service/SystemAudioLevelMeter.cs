using ColorfulLedKeyboard.Core;
using NAudio.CoreAudioApi;

namespace ColorfulLedKeyboard.Service;

internal sealed class SystemAudioLevelMeter : IDisposable
{
    private readonly AudioSourceProvider _source;

    public SystemAudioLevelMeter(AudioSourceProvider source)
    {
        _source = source;
    }

    public float GetPeakLevel()
    {
        // HFP（通话端点）必须屏蔽，否则会激活 SCO 链路影响通话音质。
        // Active / Switching / Unavailable 都尝试读 —— 读到非零会通过 ReportSamples
        // 让 Provider 把 Unavailable 自动切回 Active（v1.3 行为）。
        if (_source.Status == AudioSourceStatus.Hfp) return 0f;
        var device = _source.CurrentDevice;
        if (device is null) return 0f;

        try
        {
            var peak = device.AudioMeterInformation.MasterPeakValue;
            if (peak > 0f) _source.ReportSamples();
            return peak;
        }
        catch
        {
            _source.RefreshNow();
            return 0f;
        }
    }

    public float GetMasterVolumeScalar()
    {
        if (_source.Status == AudioSourceStatus.Hfp) return 1f;
        var device = _source.CurrentDevice;
        if (device is null) return 1f;

        try
        {
            if (device.AudioEndpointVolume.Mute) return 0f;
            return device.AudioEndpointVolume.MasterVolumeLevelScalar;
        }
        catch
        {
            _source.RefreshNow();
            return 1f;
        }
    }

    /// <summary>退出 Music 模式时调用。当前 device 由 Provider 持有，此方法占位以匹配 spec 接口。</summary>
    public void PauseDevice() { }

    public void Dispose()
    {
        // device 生命周期由 Provider 管理
    }
}
