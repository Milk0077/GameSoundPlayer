namespace GameSoundboard.Audio.DSP;

/// <summary>A continuous soft knee that approaches full scale without hard clipping.</summary>
public sealed class SoftLimiter
{
    private const float Knee = 0.8f;
    private const float Headroom = 1f - Knee;

    public void Process(Span<float> samples)
    {
        for (var i = 0; i < samples.Length; i++)
        {
            var sample = samples[i];
            if (!float.IsFinite(sample))
            {
                samples[i] = 0;
                continue;
            }

            var magnitude = MathF.Abs(sample);
            if (magnitude <= Knee) continue;

            var limited = Knee + Headroom * (1f - MathF.Exp(-(magnitude - Knee) / Headroom));
            samples[i] = MathF.CopySign(limited, sample);
        }
    }
}
