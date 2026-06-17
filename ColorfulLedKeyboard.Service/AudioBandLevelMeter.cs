using ColorfulLedKeyboard.Core;
using NAudio.Wave;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace ColorfulLedKeyboard.Service;

internal sealed class AudioBandLevelMeter : IDisposable
{
    private static readonly BandDefinition[] AdaptiveBands =
    [
        new(35, 70, 1.25),
        new(70, 140, 1.15),
        new(140, 280, 1.00),
        new(280, 700, 0.95),
        new(700, 2500, 0.95),
        new(2500, 8000, 0.85),
        new(8000, 16000, 0.65)
    ];

    private readonly object _sync = new();
    private readonly BandState[] _bandStates = AdaptiveBands.Select(_ => new BandState()).ToArray();
    private readonly AudioSourceProvider _source;
    // D 阶段：PCM buffer 对象池，预分配 4 个 8192 缓冲。写侧（OnDataAvailable）从池取、填、锁内换引用；
    // 读侧（GetAdaptiveBeatLevel）持引用在锁外用 span 处理。池的周转确保稳态零堆分配。
    private readonly ConcurrentQueue<float[]> _bufferPool = new();
    private readonly Queue<float[]> _retiredBuffers = new();
    private readonly float[][] _initialBuffers = [new float[8192], new float[8192], new float[8192], new float[8192]];
    private int _activeSnapshotReaders;
    private string _lastKnownDeviceId = "";
    private DateTimeOffset _lastCaptureResetAt = DateTimeOffset.MinValue;
    private DateTimeOffset _ensureCooldownUntil = DateTimeOffset.MinValue;
    private WasapiLoopbackCapture? _capture;
    private float[] _samples = [];
    private int _sampleCount;
    private int _sampleRate = 48000;
    private DateTimeOffset _lastAdaptiveUpdate = DateTimeOffset.MinValue;
    private double _adaptiveNoise;
    private double _rmsAverage = 0.08;
    private double _outputEnvelope;
    private double _heldOutput;
    private DateTimeOffset _holdUntil = DateTimeOffset.MinValue;
    private DateTimeOffset _lastBeatAt = DateTimeOffset.MinValue;
    private double _beatIntervalMs = 260;
    private bool _gateOpen;

    public AudioBandLevelMeter(AudioSourceProvider source)
    {
        _source = source;
        _source.SourceChanged += OnSourceChanged;
        foreach (var buf in _initialBuffers) { _bufferPool.Enqueue(buf); }
    }

    /// <summary>
    /// 测试专用构造：不订阅 AudioSourceProvider，不开 WasapiLoopbackCapture。
    /// 配合 internal ProcessFrame 直接喂合成 PCM 验证算法行为。
    /// </summary>
    internal AudioBandLevelMeter()
    {
        _source = null!;
        foreach (var buf in _initialBuffers) { _bufferPool.Enqueue(buf); }
    }

    private void OnSourceChanged(object? sender, AudioSourceChangedEventArgs e)
    {
        // 只在两种情况下重建 capture：
        //   1. device id 真变了（切换音频源）
        //   2. 进入 Hfp 状态（避免在 16kHz/Mono 端点上抓 loopback）
        // 同设备的 Active↔Unavailable 状态切换不要停 capture，否则恢复时永远拿不到样本。
        var prevId = _lastKnownDeviceId;
        var newId = e.DeviceId ?? "";
        var deviceChanged = !string.Equals(prevId, newId, StringComparison.Ordinal);
        _lastKnownDeviceId = newId;

        if (!deviceChanged && e.Status != AudioSourceStatus.Hfp)
        {
            return;
        }

        // 设备真变了：清空构造冷却让重建路径立刻可用
        _ensureCooldownUntil = DateTimeOffset.MinValue;

        // ResetCapture 会调用 NAudio 的 StopRecording，必须脱离 COM 回调线程
        System.Threading.ThreadPool.QueueUserWorkItem(_ => ResetCapture());
    }

    public void PauseCapture()
    {
        ResetCapture();
    }

    public float GetLevel(int lowHz, int highHz)
    {
        EnsureCapture();
        var snapshot = AcquireSnapshot(out var sampleRate, out var snapshotAcquired);
        try
        {
            if (snapshot.Length < 256)
            {
                return 0f;
            }

            lowHz = Math.Clamp(lowHz, 20, sampleRate / 2 - 20);
            highHz = Math.Clamp(highHz, lowHz + 10, sampleRate / 2);
            var total = 0d;
            var count = 0;
            for (var hz = lowHz; hz <= highHz; hz += Math.Max(10, (highHz - lowHz) / 8))
            {
                total += Goertzel(snapshot, sampleRate, hz);
                count++;
            }

            if (count == 0)
            {
                return 0f;
            }

            var normalizedEnergy = Math.Sqrt(total / count) / Math.Max(1, snapshot.Length / 2d);
            var level = normalizedEnergy * 1.15;
            return (float)Math.Clamp(level, 0, 1);
        }
        finally
        {
            if (snapshotAcquired)
            {
                ReleaseSnapshot();
            }
        }
    }

    public float GetAdaptiveBeatLevel(MusicSettings settings)
    {
        // 调用契约：settings 必须已 Normalize（Worker.RunMusicAsync 在循环外 normalize 一次）。
        // sanity check 仅校验 Sensitivity，不替代调用方契约。Release 编译下零成本。
        Debug.Assert(settings.Sensitivity is >= 0.5 and <= 4.0,
            "MusicSettings must be normalized before GetAdaptiveBeatLevel()");

        EnsureCapture();
        var snapshot = AcquireSnapshot(out var sampleRate, out var snapshotAcquired);
        try
        {
            if (snapshot.Length < 256)
            {
                return 0f;
            }

            return ProcessFrame(snapshot, sampleRate, settings, DateTimeOffset.UtcNow);
        }
        finally
        {
            if (snapshotAcquired)
            {
                ReleaseSnapshot();
            }
        }
    }

    private ReadOnlySpan<float> AcquireSnapshot(out int sampleRate, out bool snapshotAcquired)
    {
        lock (_sync)
        {
            if (_sampleCount > 0)
            {
                _activeSnapshotReaders++;
                snapshotAcquired = true;
            }
            else
            {
                snapshotAcquired = false;
            }
            sampleRate = _sampleRate;
            return _samples.AsSpan(0, _sampleCount);
        }
    }

    /// <summary>
    /// 自适应鼓点检测的可注入 PCM 的有状态帧处理入口。从 PCM snapshot 出发，更新并应用
    /// _bandStates / _rmsAverage / _adaptiveNoise / _gateOpen 等状态后返回最终 envelope。
    ///
    /// 抽出来是为了让测试可以注入合成 PCM + 注入 now 驱动时间，不依赖 NAudio loopback。
    /// internal 由 InternalsVisibleTo 暴露给测试项目。
    /// </summary>
    internal float ProcessFrame(ReadOnlySpan<float> snapshot, int sampleRate, MusicSettings settings, DateTimeOffset now)
    {
        if (snapshot.Length < 256)
        {
            return 0f;
        }

        var dt = _lastAdaptiveUpdate == DateTimeOffset.MinValue
            ? settings.IntervalMs / 1000d
            : (now - _lastAdaptiveUpdate).TotalSeconds;
        _lastAdaptiveUpdate = now;
        dt = Math.Clamp(dt, 0.005, 0.2);
        var rms = CalculateRms(snapshot);
        _rmsAverage = Smooth(_rmsAverage, rms, dt, rms > _rmsAverage ? 4.0 : 0.9);
        // C1: gain 不再混入 Sensitivity，回归纯 RMS 自归一。
        // 上限 2.2 让 raw 稳态工作点回到 [0.05, 0.6]，给 onset 检测留出动态范围（避免被 Sensitivity 推到饱和）。
        // Sensitivity 改为只影响 openThreshold（C3）和 FollowOutput 输入（C4）。
        var gain = Math.Clamp(0.10 / Math.Max(0.015, _rmsAverage), 0.40, 2.2);

        // C2: sensitivityFactor — Sensitivity=2.0（默认）→ factor=1.0，作为 #2 基线锚点。
        // 落在 openThreshold/output 两处，避免双层 clamp 抹平差异。
        var sensitivityFactor = Math.Clamp(settings.Sensitivity / 2.0, 0.25, 2.0);

        var weightedTotal = 0d;
        var weightTotal = 0d;
        var weightedMax = 0d;

        for (var i = 0; i < AdaptiveBands.Length; i++)
        {
            var band = AdaptiveBands[i];
            var state = _bandStates[i];
            var preference = BandPreference(band, settings.EqLowHz, settings.EqHighHz);
            var raw = GetBandLevel(snapshot, sampleRate, band.LowHz, band.HighHz) * band.Gain * gain;
            raw = Math.Clamp(raw, 0, 1);

            state.Short = Smooth(state.Short, raw, dt, raw > state.Short ? 0.006 : 0.038);
            state.Long = Smooth(state.Long, raw, dt, 0.22);

            var noiseTime = raw < state.Noise ? 0.08 : 1.8;
            state.Noise = Smooth(state.Noise, raw, dt, noiseTime);

            var floor = Math.Max(state.Long, state.Noise * 1.35);
            var onset = Math.Max(0, state.Short - floor);
            var normalizedOnset = Math.Clamp(onset * 7.5, 0, 1);
            var signal = Math.Clamp((state.Short - state.Noise) * 5.0, 0, 1);
            var nonSustained = 1 - Math.Clamp((state.Long - state.Noise) * 2.2, 0, 0.85);
            state.TargetWeight = signal * nonSustained * preference;
            state.Weight = Smooth(state.Weight, state.TargetWeight, dt, 0.55);

            weightedTotal += normalizedOnset * state.Weight;
            weightTotal += state.Weight;
            weightedMax = Math.Max(weightedMax, normalizedOnset * Math.Max(0.25, state.Weight));
        }

        var fused = weightTotal > 0.001
            ? weightedTotal / weightTotal
            : weightedMax;
        fused = Math.Max(fused, weightedMax * 0.85);

        var noiseTimeConstant = fused < _adaptiveNoise ? 0.12 : 1.8;
        _adaptiveNoise = Smooth(_adaptiveNoise, fused, dt, noiseTimeConstant);

        // C3: openThreshold 反比缩放（Sensitivity 高 → 门更低 → 更多拍点过门）。
        // 整段除以 sensitivityFactor，包括 _adaptiveNoise，否则安静段噪声主导时 Sensitivity 调高无感。
        // 下界 0.012 是物理底噪保护，缩放后仍 clamp，不被绕过。
        var beatThreshold = MusicSettings.ToAlgorithmBeatThreshold(settings.BeatThreshold);
        var gateBias = settings.NoiseGate * 0.45 + beatThreshold;
        var openThreshold = Math.Clamp((_adaptiveNoise + gateBias) / sensitivityFactor, 0.012, 0.55);
        var closeThreshold = Math.Max(0.01, openThreshold * 0.68);
        if (!_gateOpen && fused < openThreshold)
        {
            return FollowOutput(0, settings, dt, now);
        }

        if (_gateOpen && fused < closeThreshold)
        {
            _gateOpen = false;
            return FollowOutput(0, settings, dt, now);
        }

        _gateOpen = true;
        var opened = (fused - closeThreshold) / Math.Max(0.001, 1 - closeThreshold);
        var compressed = Math.Pow(Math.Clamp(opened, 0, 1), 0.72);
        // C4: FollowOutput 输入正比缩放（Sensitivity 高 → 输出更亮，触发更密）。
        // 必须在传入 FollowOutput 之前乘 —— beatThreshold 比较针对 target，先缩放再比较。
        // 不在 FollowOutput 内部乘（破坏 attack/release 物理），不乘 _outputEnvelope。
        var scaled = Math.Clamp(compressed * sensitivityFactor, 0, 1);
        return FollowOutput(scaled, settings, dt, now);
    }

    private void ReleaseSnapshot()
    {
        lock (_sync)
        {
            _activeSnapshotReaders--;
            if (_activeSnapshotReaders == 0)
            {
                while (_retiredBuffers.Count > 0)
                {
                    _bufferPool.Enqueue(_retiredBuffers.Dequeue());
                }
            }
        }
    }

    private void RetireBuffer(float[]? buffer)
    {
        if (buffer is not { Length: > 0 })
        {
            return;
        }

        lock (_sync)
        {
            if (_activeSnapshotReaders == 0)
            {
                _bufferPool.Enqueue(buffer);
            }
            else
            {
                _retiredBuffers.Enqueue(buffer);
            }
        }
    }

    public void Dispose()
    {
        if (_source is not null)
        {
            _source.SourceChanged -= OnSourceChanged;
        }
        ResetCapture();
    }

    private void EnsureCapture()
    {
        if (_source is null) return;
        if (_capture is not null) return;

        // HFP 屏蔽：通话端点不开 capture，避免激活 SCO 链路影响通话音质。
        if (_source.Status == AudioSourceStatus.Hfp) return;

        // 仅在"上一次构造抛异常"后短暂节流 1 秒，避免设备真坏时每帧重试。
        if (_ensureCooldownUntil != DateTimeOffset.MinValue && DateTimeOffset.UtcNow < _ensureCooldownUntil)
        {
            return;
        }

        try
        {
            // 关键：无参构造（沿用 v1.3 行为）。NAudio 自己拿当前默认 render 设备 +
            // 自己管理"暂停 → 重新激活"的内部状态，传 MMDevice 那条路径在系统无
            // active session 时重建会进僵尸态（start 成功但 DataAvailable 永不触发）。
            _capture = new WasapiLoopbackCapture();
            _sampleRate = _capture.WaveFormat.SampleRate;
            _capture.DataAvailable += OnDataAvailable;
            _capture.RecordingStopped += (_, _) => ResetCapture();
            _capture.StartRecording();
            _ensureCooldownUntil = DateTimeOffset.MinValue;
        }
        catch
        {
            ResetCapture();
            _ensureCooldownUntil = DateTimeOffset.UtcNow.AddSeconds(1);
        }
    }

    private void ResetCapture()
    {
        var capture = _capture;
        _capture = null;
        _lastCaptureResetAt = DateTimeOffset.UtcNow;

        // 清空残留的 PCM 缓存与 envelope/hold 状态，否则 capture 销毁后这一帧
        // 还会被 GetAdaptiveBeatLevel 读到，灯锁在最后一刻的拍点高峰色不衰减。
        // D 阶段：归还旧 buffer 给池，避免设备切换/暂停场景下漏 buffer。
        float[]? oldBuffer;
        lock (_sync)
        {
            oldBuffer = _samples;
            _samples = [];
            _sampleCount = 0;
        }
        RetireBuffer(oldBuffer);

        _outputEnvelope = 0;
        _heldOutput = 0;
        _holdUntil = DateTimeOffset.MinValue;
        _lastBeatAt = DateTimeOffset.MinValue;
        _lastAdaptiveUpdate = DateTimeOffset.MinValue;
        _gateOpen = false;
        foreach (var state in _bandStates)
        {
            state.Short = 0;
            state.Long = 0;
            state.Noise = 0;
            state.Weight = 0;
            state.TargetWeight = 0;
        }

        if (capture is null)
        {
            return;
        }

        try
        {
            capture.DataAvailable -= OnDataAvailable;
            capture.StopRecording();
        }
        catch
        {
        }
        finally
        {
            try { capture.Dispose(); } catch { }
        }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs args)
    {
        if (sender is not WasapiLoopbackCapture capture)
        {
            return;
        }

        _source.ReportSamples();

        var format = capture.WaveFormat;
        var channels = Math.Max(1, format.Channels);
        var bytesPerSample = format.BitsPerSample / 8;
        if (bytesPerSample <= 0)
        {
            return;
        }

        var frames = args.BytesRecorded / (bytesPerSample * channels);
        var sampleCount = Math.Min(frames, format.SampleRate / 16);
        // D 阶段：从池里取 buffer 复用；池空时 fallback new（启动短暂 warmup 后稳态命中）。
        // 大小不够也 new（极少见，覆盖到 SampleRate/16 即可，初始 8192 够覆盖到 128kHz）。
        if (!_bufferPool.TryDequeue(out var samples) || samples.Length < sampleCount)
        {
            samples = new float[Math.Max(sampleCount, 8192)];
        }
        var sourceFrame = Math.Max(0, frames - sampleCount);
        for (var i = 0; i < sampleCount; i++, sourceFrame++)
        {
            var sum = 0f;
            for (var channel = 0; channel < channels; channel++)
            {
                var offset = ((sourceFrame * channels) + channel) * bytesPerSample;
                sum += ReadSample(args.Buffer, offset, format);
            }

            samples[i] = sum / channels;
        }

        float[]? oldBuffer;
        lock (_sync)
        {
            _sampleRate = format.SampleRate;
            oldBuffer = _samples;
            _samples = samples;
            _sampleCount = sampleCount;
        }
        // 旧 buffer 不能立即归还池：读线程可能仍在锁外用 span 分析它。
        // RetireBuffer 在无 active reader 时入池，否则暂存在 _retiredBuffers，读侧 ReleaseSnapshot 后统一归还。
        RetireBuffer(oldBuffer);
    }

    private static float ReadSample(byte[] buffer, int offset, WaveFormat format)
    {
        if (offset < 0 || offset >= buffer.Length)
        {
            return 0;
        }

        if (format.Encoding == WaveFormatEncoding.IeeeFloat && format.BitsPerSample == 32 && offset + 4 <= buffer.Length)
        {
            return BitConverter.ToSingle(buffer, offset);
        }

        if (format.BitsPerSample == 16 && offset + 2 <= buffer.Length)
        {
            return BitConverter.ToInt16(buffer, offset) / 32768f;
        }

        if (format.BitsPerSample == 32 && offset + 4 <= buffer.Length)
        {
            return BitConverter.ToInt32(buffer, offset) / 2147483648f;
        }

        return 0;
    }

    private static double GetBandLevel(ReadOnlySpan<float> samples, int sampleRate, int lowHz, int highHz)
    {
        lowHz = Math.Clamp(lowHz, 20, sampleRate / 2 - 20);
        highHz = Math.Clamp(highHz, lowHz + 10, sampleRate / 2);
        var total = 0d;
        var count = 0;
        for (var hz = lowHz; hz <= highHz; hz += Math.Max(10, (highHz - lowHz) / 8))
        {
            total += Goertzel(samples, sampleRate, hz);
            count++;
        }

        if (count == 0)
        {
            return 0;
        }

        return Math.Sqrt(total / count) / Math.Max(1, samples.Length / 2d);
    }

    private float FollowOutput(double target, MusicSettings settings, double dt, DateTimeOffset now)
    {
        var beatThreshold = Math.Clamp(0.18 + MusicSettings.ToAlgorithmBeatThreshold(settings.BeatThreshold) * 4.0, 0.08, 0.55);
        var minCooldownMs = Math.Clamp(settings.AttackMs * 0.8 + 10, 18, 70);
        var adaptiveCooldownMs = Math.Clamp(_beatIntervalMs * 0.35, minCooldownMs, 100);
        var canTrigger = now - _lastBeatAt > TimeSpan.FromMilliseconds(adaptiveCooldownMs);

        if (target >= beatThreshold && canTrigger)
        {
            if (_lastBeatAt != DateTimeOffset.MinValue)
            {
                var interval = (now - _lastBeatAt).TotalMilliseconds;
                if (interval is >= 70 and <= 1200)
                {
                    _beatIntervalMs = Smooth(_beatIntervalMs, interval, 0.2, 0.65);
                }
            }

            _lastBeatAt = now;
            _heldOutput = Math.Max(_heldOutput, target);
            _holdUntil = now.AddMilliseconds(Math.Clamp(settings.PeakHoldMs, 8, 80));
        }

        if (now < _holdUntil)
        {
            target = Math.Max(target, _heldOutput);
        }
        else
        {
            _heldOutput = Math.Max(0, _heldOutput - dt * 5.0);
        }

        var attackSeconds = Math.Clamp(settings.AttackMs / 1000d * 0.18, 0.0015, 0.025);
        var releaseSeconds = Math.Clamp(settings.ReleaseMs / 1000d * 0.35, 0.018, 0.16);
        _outputEnvelope = Smooth(_outputEnvelope, target, dt, target > _outputEnvelope ? attackSeconds : releaseSeconds);
        if (_outputEnvelope < 0.015)
        {
            _outputEnvelope = 0;
        }

        return (float)Math.Clamp(_outputEnvelope, 0, 1);
    }

    private static double CalculateRms(ReadOnlySpan<float> samples)
    {
        if (samples.Length == 0)
        {
            return 0;
        }

        var total = 0d;
        foreach (var sample in samples)
        {
            total += sample * sample;
        }

        return Math.Sqrt(total / samples.Length);
    }

    private static double BandPreference(BandDefinition band, int preferredLowHz, int preferredHighHz)
    {
        preferredLowHz = Math.Clamp(preferredLowHz, 20, 1000);
        preferredHighHz = Math.Clamp(preferredHighHz, preferredLowHz + 10, 16000);
        var overlap = Math.Max(0, Math.Min(band.HighHz, preferredHighHz) - Math.Max(band.LowHz, preferredLowHz));
        var bandWidth = Math.Max(1, band.HighHz - band.LowHz);
        var overlapRatio = overlap / (double)bandWidth;
        return 0.75 + overlapRatio * 0.5;
    }

    private static double Smooth(double current, double target, double dtSeconds, double timeConstantSeconds)
    {
        var alpha = 1 - Math.Exp(-dtSeconds / Math.Max(0.001, timeConstantSeconds));
        return current + (target - current) * alpha;
    }

    private static double Goertzel(ReadOnlySpan<float> samples, int sampleRate, int targetHz)
    {
        var omega = 2.0 * Math.PI * targetHz / sampleRate;
        var coeff = 2.0 * Math.Cos(omega);
        var q0 = 0d;
        var q1 = 0d;
        var q2 = 0d;
        for (var i = 0; i < samples.Length; i++)
        {
            var window = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / Math.Max(1, samples.Length - 1));
            q0 = coeff * q1 - q2 + samples[i] * window;
            q2 = q1;
            q1 = q0;
        }

        return q1 * q1 + q2 * q2 - coeff * q1 * q2;
    }

    private sealed class BandState
    {
        public double Short { get; set; }
        public double Long { get; set; }
        public double Noise { get; set; }
        public double TargetWeight { get; set; }
        public double Weight { get; set; }
    }

    private readonly record struct BandDefinition(int LowHz, int HighHz, double Gain);
}
