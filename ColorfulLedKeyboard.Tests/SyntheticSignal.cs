namespace ColorfulLedKeyboard.Tests;

internal enum EnvelopeShape
{
    Constant,
    AdsrPercussive,
}

internal static class SyntheticSignal
{
    /// <summary>生成一段正弦 PCM，采样率 sampleRate，频率 frequencyHz，时长 durationMs，幅度 amplitude。</summary>
    internal static float[] SineBurst(int sampleRate, double frequencyHz, double durationMs, double amplitude,
        EnvelopeShape envelope = EnvelopeShape.Constant)
    {
        var sampleCount = (int)(sampleRate * durationMs / 1000.0);
        var result = new float[sampleCount];
        var attackSamples = (int)(sampleRate * 0.001); // 1ms attack
        var decaySamples = (int)(sampleRate * 0.020);  // 20ms decay
        for (var i = 0; i < sampleCount; i++)
        {
            var t = i / (double)sampleRate;
            var sample = Math.Sin(2 * Math.PI * frequencyHz * t);

            if (envelope == EnvelopeShape.AdsrPercussive)
            {
                // 1ms attack → 20ms decay → 0 sustain
                double amp;
                if (i < attackSamples) { amp = (double)i / attackSamples; }
                else if (i < attackSamples + decaySamples) { amp = 1.0 - (double)(i - attackSamples) / decaySamples; }
                else { amp = 0; }
                sample *= amp;
            }

            result[i] = (float)(sample * amplitude);
        }
        return result;
    }

    /// <summary>节拍序列：每 intervalMs 一个 burst，总时 totalMs。burstMs 控制每个节拍的时长。</summary>
    internal static float[] BeatPattern(int sampleRate, double frequencyHz, double burstMs, double intervalMs,
        double totalMs, double amplitude, EnvelopeShape envelope = EnvelopeShape.Constant)
    {
        var totalSamples = (int)(sampleRate * totalMs / 1000.0);
        var result = new float[totalSamples];
        var burstSamples = (int)(sampleRate * burstMs / 1000.0);
        var intervalSamples = (int)(sampleRate * intervalMs / 1000.0);

        for (var beatStart = 0; beatStart < totalSamples; beatStart += intervalSamples)
        {
            var burstEnd = Math.Min(beatStart + burstSamples, totalSamples);
            var burstLen = burstEnd - beatStart;
            if (burstLen <= 0) break;

            var burst = SineBurst(sampleRate, frequencyHz, burstLen * 1000.0 / sampleRate, amplitude, envelope);
            Array.Copy(burst, 0, result, beatStart, burstLen);
        }
        return result;
    }

    /// <summary>静音段。</summary>
    internal static float[] Silence(int sampleRate, double durationMs)
    {
        return new float[(int)(sampleRate * durationMs / 1000.0)];
    }

    /// <summary>拼接多个 PCM 段。</summary>
    internal static float[] Concat(params float[][] segments)
    {
        var totalLen = 0;
        foreach (var seg in segments) { totalLen += seg.Length; }
        var result = new float[totalLen];
        var offset = 0;
        foreach (var seg in segments)
        {
            Array.Copy(seg, 0, result, offset, seg.Length);
            offset += seg.Length;
        }
        return result;
    }
}