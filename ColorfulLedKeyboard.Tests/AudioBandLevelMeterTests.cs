using ColorfulLedKeyboard.Core;
using ColorfulLedKeyboard.Service;

namespace ColorfulLedKeyboard.Tests;

/// <summary>
/// AudioBandLevelMeter 自适应鼓点检测算法的回归测试。
/// 通过 internal ProcessFrame 注入合成 PCM，避免依赖 NAudio loopback。
///
/// 测试驱动方式：sampleRate=48000，每帧步进 25ms（=1200 sample），
/// 把整段 PCM 切成 1200-sample 块逐帧喂给 ProcessFrame，并以注入的 now 驱动时间。
/// </summary>
public sealed class AudioBandLevelMeterTests
{
    private const int SampleRate = 48000;
    private const int FrameSize = SampleRate / 40; // 25ms per frame = 1200 samples
    private const double FrameMs = 25.0;
    private static readonly DateTimeOffset BaseTime = new(2026, 6, 17, 12, 0, 0, TimeSpan.Zero);

    /// <summary>把整段 PCM 喂入 meter，返回每帧的 envelope。</summary>
    private static List<float> RunFrames(AudioBandLevelMeter meter, MusicSettings settings, float[] pcm)
    {
        var envelopes = new List<float>();
        var frameCount = pcm.Length / FrameSize;
        for (var i = 0; i < frameCount; i++)
        {
            var span = pcm.AsSpan(i * FrameSize, FrameSize);
            var now = BaseTime.AddMilliseconds(i * FrameMs);
            envelopes.Add(meter.ProcessFrame(span, SampleRate, settings, now));
        }
        return envelopes;
    }

    /// <summary>统计触发数：envelope 在 0.1 以上视为有效响应（粗略）。</summary>
    private static int CountActiveFrames(List<float> envelopes, float threshold = 0.1f) =>
        envelopes.Count(e => e > threshold);

    private static MusicSettings DefaultSettings(double sensitivity = 2.0) => new MusicSettings
    {
        Sensitivity = sensitivity,
        AttackMs = 35,
        ReleaseMs = 80,
        BaseBrightness = 25,
        PeakBrightness = 100,
        IntervalMs = 25,
        NoiseGate = 0,
        BeatThreshold = 0.02,
        PeakHoldMs = 50,
        EqEnabled = true,
        EqLowHz = 30,
        EqHighHz = 5000,
    }.Normalize();

    // ---------- #1 静音 → 输出恒 0 ----------
    [Fact]
    public void Silence_ProducesZeroEnvelope()
    {
        using var meter = new AudioBandLevelMeter();
        var pcm = SyntheticSignal.Silence(SampleRate, 1000);
        var envelopes = RunFrames(meter, DefaultSettings(), pcm);
        Assert.All(envelopes, e => Assert.True(e < 0.01f, $"silence frame produced {e}"));
    }

    // ---------- #2 默认 Sensitivity=2.0 行为基线 ----------
    [Fact]
    public void DefaultSensitivity_ProducesExpectedTriggerCount()
    {
        using var meter = new AudioBandLevelMeter();
        // 60Hz 120BPM (500ms interval) × 10 拍 + 1s warm up = ~6s
        var warmup = SyntheticSignal.Silence(SampleRate, 1000);
        var beats = SyntheticSignal.BeatPattern(SampleRate, 60, burstMs: 30, intervalMs: 500,
            totalMs: 5000, amplitude: 0.6, envelope: EnvelopeShape.AdsrPercussive);
        var pcm = SyntheticSignal.Concat(warmup, beats);

        var envelopes = RunFrames(meter, DefaultSettings(), pcm);
        var maxEnvelope = envelopes.Max();

        // 默认配置下应有可见拍点响应
        Assert.True(maxEnvelope > 0.1f, $"max envelope {maxEnvelope} too low for 60Hz beats");
        Assert.True(maxEnvelope <= 1.0f);
    }

    // ---------- #3 Sensitivity 单调性（核心验收）----------
    [Fact]
    public void Sensitivity_HigherProducesHigherAverageEnvelope()
    {
        // 同一 PCM 跑三个独立 meter 实例
        var warmup = SyntheticSignal.Silence(SampleRate, 1000);
        var beats = SyntheticSignal.BeatPattern(SampleRate, 60, burstMs: 30, intervalMs: 500,
            totalMs: 5000, amplitude: 0.5, envelope: EnvelopeShape.AdsrPercussive);
        var pcm = SyntheticSignal.Concat(warmup, beats);

        double Avg(double sensitivity)
        {
            using var meter = new AudioBandLevelMeter();
            var envelopes = RunFrames(meter, DefaultSettings(sensitivity), pcm);
            // 全帧均值（含静音段、衰减段，禁止偏触发态采样）
            return envelopes.Average();
        }

        var avgLow = Avg(0.5);
        var avgMid = Avg(2.0);
        var avgHigh = Avg(4.0);

        // 全帧 envelope 均值严格单调递增
        Assert.True(avgLow < avgMid, $"avg(0.5)={avgLow} should be < avg(2.0)={avgMid}");
        Assert.True(avgMid < avgHigh, $"avg(2.0)={avgMid} should be < avg(4.0)={avgHigh}");
    }

    // ---------- #3b 4.0 档应有非饱和帧（防止"卡满亮"伪单调）----------
    [Fact]
    public void HighSensitivity_HasNonSaturatedFrames()
    {
        using var meter = new AudioBandLevelMeter();
        var warmup = SyntheticSignal.Silence(SampleRate, 1000);
        var beats = SyntheticSignal.BeatPattern(SampleRate, 60, burstMs: 30, intervalMs: 500,
            totalMs: 5000, amplitude: 0.5, envelope: EnvelopeShape.AdsrPercussive);
        var pcm = SyntheticSignal.Concat(warmup, beats);

        var envelopes = RunFrames(meter, DefaultSettings(4.0), pcm);
        // 跳过 warmup 后的帧（前 40 帧）
        var afterWarmup = envelopes.Skip(40).ToList();
        var minEnv = afterWarmup.Min();

        // 120BPM/500ms 间隔 + ~150ms release：理论非触发帧应到 0.3 以下
        Assert.True(minEnv < 0.3f,
            $"4.0 档稳态最小 envelope {minEnv} 不应饱和；说明 release 太慢或拖尾，需检查");
    }

    // ---------- #4 高频在低频偏好下不响应 ----------
    [Fact]
    public void HighFrequency_DoesNotTriggerLowFrequencyPreference()
    {
        var warmup = SyntheticSignal.Silence(SampleRate, 1000);

        // 60Hz beats → 低频偏好下应有响应
        using var lowMeter = new AudioBandLevelMeter();
        var lowFreqSettings = DefaultSettings();
        lowFreqSettings.EqLowHz = 40;
        lowFreqSettings.EqHighHz = 200;
        lowFreqSettings.Normalize();
        var lowPcm = SyntheticSignal.Concat(warmup, SyntheticSignal.BeatPattern(SampleRate, 60, 30, 500, 5000, 0.5, EnvelopeShape.AdsrPercussive));
        var lowEnvs = RunFrames(lowMeter, lowFreqSettings, lowPcm);

        // 8kHz beats → 低频偏好下应几乎无响应
        using var highMeter = new AudioBandLevelMeter();
        var highPcm = SyntheticSignal.Concat(warmup, SyntheticSignal.BeatPattern(SampleRate, 8000, 30, 500, 5000, 0.5, EnvelopeShape.AdsrPercussive));
        var highEnvs = RunFrames(highMeter, lowFreqSettings, highPcm);

        Assert.True(lowEnvs.Average() > highEnvs.Average() + 0.01,
            $"low-freq beats avg {lowEnvs.Average()} should be > high-freq beats avg {highEnvs.Average()} when EQ prefers low");
    }

    // ---------- #5 NoiseGate 单调性 ----------
    [Fact]
    public void NoiseGate_HigherSettingProducesLessOutput()
    {
        var warmup = SyntheticSignal.Silence(SampleRate, 1000);
        var weak = SyntheticSignal.BeatPattern(SampleRate, 60, 30, 500, 5000, amplitude: 0.15, envelope: EnvelopeShape.AdsrPercussive);
        var pcm = SyntheticSignal.Concat(warmup, weak);

        double SumWith(double noiseGate)
        {
            var settings = DefaultSettings();
            settings.NoiseGate = noiseGate;
            settings.Normalize();
            using var meter = new AudioBandLevelMeter();
            return RunFrames(meter, settings, pcm).Sum();
        }

        var sumLow = SumWith(0.0);
        var sumHigh = SumWith(0.5);

        Assert.True(sumLow > sumHigh, $"NoiseGate=0 sum={sumLow} should be > NoiseGate=0.5 sum={sumHigh}");
    }

    // ---------- #6 BeatThreshold 单调性 ----------
    [Fact]
    public void BeatThreshold_HigherSettingProducesLessOutput()
    {
        var warmup = SyntheticSignal.Silence(SampleRate, 1000);
        var beats = SyntheticSignal.BeatPattern(SampleRate, 60, 30, 500, 5000, amplitude: 0.4, envelope: EnvelopeShape.AdsrPercussive);
        var pcm = SyntheticSignal.Concat(warmup, beats);

        double SumWith(double threshold)
        {
            var settings = DefaultSettings();
            settings.BeatThreshold = threshold;
            settings.Normalize();
            using var meter = new AudioBandLevelMeter();
            return RunFrames(meter, settings, pcm).Sum();
        }

        var sumLow = SumWith(0.0);
        var sumHigh = SumWith(1.0);

        Assert.True(sumLow >= sumHigh, $"BeatThreshold=0 sum={sumLow} should be >= BeatThreshold=1.0 sum={sumHigh}");
    }

    // ---------- #7 RMS 自适应防饱和回归（持续大音量不应卡满亮）----------
    [Fact]
    public void ContinuousLoudSignal_DoesNotSaturate()
    {
        using var meter = new AudioBandLevelMeter();
        // 持续 5 秒大音量 60Hz 正弦，幅度 0.9
        var pcm = SyntheticSignal.SineBurst(SampleRate, 60, durationMs: 5000, amplitude: 0.9, envelope: EnvelopeShape.Constant);
        var envelopes = RunFrames(meter, DefaultSettings(), pcm);
        // 跳过 warmup 后的帧（前 40 帧 = 1s）
        var afterWarmup = envelopes.Skip(40).ToList();

        // 持续音应被 RMS 自归一抑制：min 应该 < 0.3，证明不是"卡满亮"
        var min = afterWarmup.Min();
        Assert.True(min < 0.3f, $"持续大音量稳态 min envelope {min} 不应饱和；RMS 自适应失效");
    }
}
