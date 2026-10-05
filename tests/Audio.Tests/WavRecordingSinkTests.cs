using GameSoundboard.Audio.Playback;
using NAudio.Wave;

namespace GameSoundboard.Audio.Tests;

public sealed class WavRecordingSinkTests
{
    [Fact]
    public void Writes48kStereoFloatSamplesOffTheMixerThread()
    {
        var path = Path.Combine(Path.GetTempPath(), $"gsb-record-{Guid.NewGuid():N}.wav");
        try
        {
            using (var sink = new WavRecordingSink(path))
                Assert.True(sink.TryWrite(Enumerable.Repeat(0.25f, 960).ToArray()));
            using var reader = new WaveFileReader(path);
            Assert.Equal(48_000, reader.WaveFormat.SampleRate);
            Assert.Equal(2, reader.WaveFormat.Channels);
            Assert.Equal(960 * sizeof(float), reader.Length);
        }
        finally { File.Delete(path); }
    }
}
