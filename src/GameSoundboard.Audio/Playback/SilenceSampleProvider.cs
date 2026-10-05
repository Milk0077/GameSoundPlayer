using NAudio.Wave;

namespace GameSoundboard.Audio.Playback;

public sealed class SilenceSampleProvider : ISampleProvider
{
    public WaveFormat WaveFormat { get; } = NAudio.Wave.WaveFormat.CreateIeeeFloatWaveFormat(48_000, 2);

    public int Read(Span<float> buffer)
    {
        buffer.Clear();
        return buffer.Length;
    }
}
