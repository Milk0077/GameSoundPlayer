using GameSoundboard.Audio.Resampling;
using NAudio.Wave;

namespace GameSoundboard.Audio.Tests;

public sealed class AudioFormatAdapterTests
{
    [Theory]
    [InlineData(44_100)]
    [InlineData(96_000)]
    public void ResamplesMonoInputTo48kStereo(int sourceRate)
    {
        var adapted = AudioFormatAdapter.To48kStereo(new ConstantMonoSource(sourceRate));
        var output = new float[960];

        var read = adapted.Read(output);

        Assert.Equal(48_000, adapted.WaveFormat.SampleRate);
        Assert.Equal(2, adapted.WaveFormat.Channels);
        Assert.Equal(output.Length, read);
        for (var i = 0; i < output.Length; i += 2)
            Assert.InRange(Math.Abs(output[i] - output[i + 1]), 0, 0.0001f);
    }

    private sealed class ConstantMonoSource(int sampleRate) : ISampleProvider
    {
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 1);

        public int Read(Span<float> buffer)
        {
            buffer.Fill(0.25f);
            return buffer.Length;
        }
    }
}
