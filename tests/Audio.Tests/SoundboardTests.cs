using GameSoundboard.Audio.Playback;
using NAudio.Wave;

namespace GameSoundboard.Audio.Tests;

public sealed class SoundboardTests
{
    [Fact]
    public void PlaysMultipleVoicesAndStopsThemAll()
    {
        var clip = new SoundClip(Guid.NewGuid(), "click", "click.wav", [0.25f, 0.25f, 0.25f, 0.25f]);
        var source = new SoundboardSampleProvider();
        Assert.True(source.Play(clip));
        Assert.True(source.Play(clip));
        var mixed = new float[4];

        source.Read(mixed);

        Assert.Equal([0.5f, 0.5f, 0.5f, 0.5f], mixed);
        source.StopAll();
        source.Read(mixed);
        Assert.Equal([0f, 0f, 0f, 0f], mixed);
    }

    [Fact]
    public void PlayAfterStopAllStillStartsOnTheNextMixerPeriod()
    {
        var clip = new SoundClip(Guid.NewGuid(), "click", "click.wav", [0.25f, 0.25f]);
        var source = new SoundboardSampleProvider();
        source.Play(clip);
        source.StopAll();
        source.Play(clip);
        var output = new float[2];
        source.Read(output);
        Assert.Equal([0.25f, 0.25f], output);
    }

    [Fact]
    public void DecodesWavToCached48kStereoFrames()
    {
        var path = Path.Combine(Path.GetTempPath(), $"gsb-test-{Guid.NewGuid():N}.wav");
        try
        {
            using (var writer = new WaveFileWriter(path, WaveFormat.CreateIeeeFloatWaveFormat(44_100, 1)))
            {
                var samples = Enumerable.Repeat(0.25f, 4_410).ToArray();
                writer.WriteSamples(samples, 0, samples.Length);
            }

            var clip = SoundClipDecoder.Decode(path);

            Assert.Equal(Path.GetFileNameWithoutExtension(path), clip.Name);
            Assert.InRange(clip.Samples.Length, 9_000, 10_000);
            Assert.Equal(0, clip.Samples.Length % 2);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
