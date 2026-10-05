using GameSoundboard.Audio.DSP;
using GameSoundboard.Audio.Mixer;
using GameSoundboard.Core.Models;
using NAudio.Wave;

namespace GameSoundboard.Audio.Tests;

public sealed class AudioMixerTests
{
    [Fact]
    public void MixesThreeBusesWithIndependentVolumeAndMute()
    {
        var mixer = new AudioMixer(new ConstantSource(0.2f), new ConstantSource(0.1f), new ConstantSource(0.05f));
        mixer.SetVolume(AudioBus.Microphone, 0.5f);
        mixer.SetMute(AudioBus.Music, true);
        var output = new float[960];

        mixer.Mix(output);

        Assert.All(output, sample => Assert.InRange(sample, 0.1999f, 0.2001f));
        Assert.InRange(mixer.GetPeakDb(AudioBus.Microphone), -20.1, -19.9);
        Assert.Equal(-60, mixer.GetPeakDb(AudioBus.Music));
        Assert.InRange(mixer.GetPeakDb(AudioBus.Master), -14.1, -13.9);
    }

    [Fact]
    public void MasterMuteProducesSilence()
    {
        var mixer = new AudioMixer(new ConstantSource(0.4f), new ConstantSource(0.4f), new ConstantSource(0.4f));
        mixer.SetMute(AudioBus.Master, true);
        var output = new float[960];

        mixer.Mix(output);

        Assert.All(output, sample => Assert.Equal(0, sample));
        Assert.Equal(-60, mixer.GetPeakDb(AudioBus.Master));
    }

    [Fact]
    public void SoftLimiterIsContinuousAndNeverExceedsFullScale()
    {
        float[] samples = [0.79f, 0.8f, 0.81f, 1f, 2f, 100f, -100f, float.NaN];
        new SoftLimiter().Process(samples);

        Assert.All(samples, sample => Assert.InRange(sample, -1f, 1f));
        Assert.Equal(0.8f, samples[1]);
        Assert.InRange(samples[2], 0.8f, 0.81f);
        Assert.True(samples[3] > samples[2]);
        Assert.True(samples[4] > samples[3]);
        Assert.Equal(0, samples[^1]);
    }

    [Fact]
    public void EmptyInputIsSilenceAndCountsUnderrun()
    {
        var mixer = new AudioMixer(new EmptySource(), new ConstantSource(0), new ConstantSource(0));
        var output = new float[960];

        mixer.Mix(output);

        Assert.All(output, sample => Assert.Equal(0, sample));
        Assert.Equal(1, mixer.Underruns);
    }

    private sealed class ConstantSource(float value) : ISampleProvider
    {
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(48_000, 2);

        public int Read(Span<float> buffer)
        {
            buffer.Fill(value);
            return buffer.Length;
        }
    }

    private sealed class EmptySource : ISampleProvider
    {
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(48_000, 2);
        public int Read(Span<float> buffer) => 0;
    }
}
