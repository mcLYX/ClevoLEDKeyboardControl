using ColorfulLedKeyboard.Core;
using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;

namespace ColorfulLedKeyboard.Service;

/// <summary>音频源状态机 + 设备切换通知 + 1.5 秒无样本 fallback。
/// 两个 meter 类共享一个 Provider，从这里拿 MMDevice。</summary>
public sealed class AudioSourceProvider : IDisposable
{
    private static readonly TimeSpan FallbackThreshold = TimeSpan.FromMilliseconds(1500);

    private readonly object _stateLock = new();
    private readonly IAudioDeviceProbe _probe;
    private readonly ILogger<AudioSourceProvider>? _logger;
    private readonly System.Threading.Timer? _fallbackTimer;
    private readonly bool _ownsProbe;

    private AudioSourceStatus _status;
    private string _deviceFriendlyName = "";
    private string _deviceId = "";
    private long _lastSampleTicks;
    private int _hasSample; // 0 = 从未 ReportSamples；1 = 已经 ReportSamples 至少一次
    // 测试模式：以虚拟时钟驱动 fallback；ReportSamples 把 _lastSampleTicks 设为 _virtualNowTicks
    private long _virtualNowTicks;
    private bool _disposed;

    /// <summary>生产构造：内部创建 MMDeviceProbe。</summary>
    public AudioSourceProvider(ILogger<AudioSourceProvider>? logger = null)
        : this(new MMDeviceProbe(), logger, ownsProbe: true)
    {
    }

    /// <summary>测试构造：注入 probe，不启用真实 fallback timer。</summary>
    internal AudioSourceProvider(IAudioDeviceProbe probe, ILogger<AudioSourceProvider>? logger = null)
        : this(probe, logger, ownsProbe: false)
    {
    }

    private AudioSourceProvider(IAudioDeviceProbe probe, ILogger<AudioSourceProvider>? logger, bool ownsProbe)
    {
        _probe = probe;
        _logger = logger;
        _ownsProbe = ownsProbe;
        _lastSampleTicks = 0;
        _virtualNowTicks = 0;

        // 真实部署时启用周期 timer；测试构造（ownsProbe=false）依赖 TestOnly_AdvanceFallbackClock 推动
        if (ownsProbe)
        {
            _fallbackTimer = new System.Threading.Timer(_ => CheckFallback(), null,
                TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(500));
        }

        InitialResolve();
    }

    public AudioSourceStatus Status
    {
        get { lock (_stateLock) return _status; }
    }

    public string DeviceFriendlyName
    {
        get { lock (_stateLock) return _deviceFriendlyName; }
    }

    public string DeviceId
    {
        get { lock (_stateLock) return _deviceId; }
    }

    public event EventHandler<AudioSourceChangedEventArgs>? SourceChanged;

    /// <summary>meter 收到一帧 PCM 后调用。无锁高频路径。</summary>
    public void ReportSamples()
    {
        if (_disposed) return;
        long now;
        if (_ownsProbe)
        {
            now = DateTime.UtcNow.Ticks;
        }
        else
        {
            now = System.Threading.Interlocked.Read(ref _virtualNowTicks);
        }
        System.Threading.Interlocked.Exchange(ref _lastSampleTicks, now);
        System.Threading.Interlocked.Exchange(ref _hasSample, 1);
    }

    /// <summary>Worker 进入 Music 模式那一刻调一次，强刷状态文件。</summary>
    public void RefreshNow()
    {
        if (_disposed) return;
        ResolveAndPublish(transitional: false);
    }

    /// <summary>测试入口：模拟 IMMNotificationClient.OnDefaultDeviceChanged。</summary>
    internal void TestOnly_SimulateDefaultDeviceChanged(string newDeviceId)
    {
        OnDefaultDeviceChangedInternal(newDeviceId);
    }

    /// <summary>测试入口：推进 fallback 时钟。</summary>
    internal void TestOnly_AdvanceFallbackClock(TimeSpan delta)
    {
        System.Threading.Interlocked.Add(ref _virtualNowTicks, delta.Ticks);
        CheckFallback();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _fallbackTimer?.Dispose();

        if (_ownsProbe && _probe is IDisposable disposable)
        {
            try { disposable.Dispose(); }
            catch { /* swallow */ }
        }

        SourceChanged = null;
    }

    private void InitialResolve()
    {
        try
        {
            var snapshot = _probe.GetDefaultRenderDevice();
            ApplySnapshot(snapshot, fireEvent: false);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "AudioSourceProvider initial resolve failed");
            lock (_stateLock)
            {
                _status = AudioSourceStatus.Unavailable;
                _deviceFriendlyName = "";
                _deviceId = "";
            }
        }
    }

    /// <summary>NotificationClient 真实回调进来时也走这条路径。</summary>
    internal void OnDefaultDeviceChangedInternal(string newDeviceId)
    {
        if (_disposed) return;

        // 第一次：进入 Switching
        string nameNow, idNow;
        lock (_stateLock)
        {
            nameNow = _deviceFriendlyName;
            idNow = _deviceId;
        }
        PublishStatus(AudioSourceStatus.Switching, nameNow, idNow);

        // 第二次：根据 InspectDevice 结果决定 Active / Hfp / Switching（保守）
        ResolveAndPublish(transitional: true);
    }

    private void ResolveAndPublish(bool transitional)
    {
        DeviceSnapshot? snapshot;
        try
        {
            snapshot = _probe.GetDefaultRenderDevice();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "AudioSourceProvider resolve threw, keeping Switching");
            // 保守：当前回调过程中保持 Switching，让 fallback 兜底
            string nameNow, idNow;
            lock (_stateLock)
            {
                nameNow = _deviceFriendlyName;
                idNow = _deviceId;
            }
            PublishStatus(AudioSourceStatus.Switching, nameNow, idNow);
            return;
        }

        ApplySnapshot(snapshot, fireEvent: true);
    }

    private void ApplySnapshot(DeviceSnapshot? snapshot, bool fireEvent)
    {
        AudioSourceStatus newStatus;
        string newName;
        string newId;

        if (snapshot is null)
        {
            newStatus = AudioSourceStatus.Unavailable;
            newName = "";
            newId = "";
        }
        else if (IsHfp(snapshot))
        {
            newStatus = AudioSourceStatus.Hfp;
            newName = snapshot.FriendlyName;
            newId = snapshot.Id;
        }
        else
        {
            newStatus = AudioSourceStatus.Active;
            newName = snapshot.FriendlyName;
            newId = snapshot.Id;
        }

        bool changed;
        lock (_stateLock)
        {
            changed = _status != newStatus || _deviceFriendlyName != newName || _deviceId != newId;
            _status = newStatus;
            _deviceFriendlyName = newName;
            _deviceId = newId;
        }

        if (changed && fireEvent)
        {
            RaiseSourceChanged(newStatus, newName, newId);
        }
    }

    private void PublishStatus(AudioSourceStatus status, string name, string id)
    {
        bool changed;
        lock (_stateLock)
        {
            changed = _status != status || _deviceFriendlyName != name || _deviceId != id;
            _status = status;
            _deviceFriendlyName = name;
            _deviceId = id;
        }
        if (changed)
        {
            RaiseSourceChanged(status, name, id);
        }
    }

    private void RaiseSourceChanged(AudioSourceStatus status, string name, string id)
    {
        var handlers = SourceChanged;
        if (handlers is null) return;

        var args = new AudioSourceChangedEventArgs { Status = status, DeviceFriendlyName = name, DeviceId = id };
        foreach (var handler in handlers.GetInvocationList())
        {
            try
            {
                ((EventHandler<AudioSourceChangedEventArgs>)handler).Invoke(this, args);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "AudioSourceProvider subscriber threw");
            }
        }
    }

    private static bool IsHfp(DeviceSnapshot snapshot) =>
        snapshot.SampleRate > 0 && snapshot.SampleRate <= 16000 && snapshot.Channels == 1;

    private void CheckFallback()
    {
        if (_disposed) return;
        try
        {
            if (System.Threading.Interlocked.CompareExchange(ref _hasSample, 0, 0) == 0) return;
            var lastTicks = System.Threading.Interlocked.Read(ref _lastSampleTicks);

            long nowTicks = _ownsProbe
                ? DateTime.UtcNow.Ticks
                : System.Threading.Interlocked.Read(ref _virtualNowTicks);

            var elapsed = TimeSpan.FromTicks(nowTicks - lastTicks);

            AudioSourceStatus current;
            string name, id;
            lock (_stateLock)
            {
                current = _status;
                name = _deviceFriendlyName;
                id = _deviceId;
            }

            // Active 状态下，超过 1.5s 没样本 → Unavailable
            if (current == AudioSourceStatus.Active && elapsed > FallbackThreshold)
            {
                PublishStatus(AudioSourceStatus.Unavailable, name, id);
                return;
            }

            // Unavailable 状态下，最近 1.5s 内有样本 → 切回 Active（设备名保持）
            if (current == AudioSourceStatus.Unavailable && elapsed <= FallbackThreshold)
            {
                PublishStatus(AudioSourceStatus.Active, name, id);
                return;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "AudioSourceProvider fallback check threw");
        }
    }

    /// <summary>Provider 暴露给 meter 的辅助：当前 NAudio MMDevice。
    /// 测试构造（FakeAudioDeviceProbe）下永远返回 null。
    /// 生产构造（MMDeviceProbe）下从 probe 内部缓存拿。</summary>
    public MMDevice? CurrentDevice
    {
        get
        {
            if (_probe is MMDeviceProbe production) return production.GetCurrentMMDevice();
            return null;
        }
    }
}

public sealed class AudioSourceChangedEventArgs : EventArgs
{
    public AudioSourceStatus Status { get; init; }
    public string DeviceFriendlyName { get; init; } = "";
    public string DeviceId { get; init; } = "";
}
