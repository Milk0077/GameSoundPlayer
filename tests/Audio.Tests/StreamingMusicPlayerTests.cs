using GameSoundboard.Audio.Playback;
using NAudio.Wave;

namespace GameSoundboard.Audio.Tests;

public sealed class StreamingMusicPlayerTests
{
    [Fact]
    public async Task StreamsWavFromBackgroundAndTracksConsumedPosition()
    {
        var path = Path.Combine(Path.GetTempPath(), $"gsb-music-{Guid.NewGuid():N}.wav");
        try
        {
            using (var writer = new WaveFileWriter(path, WaveFormat.CreateIeeeFloatWaveFormat(48_000, 2)))
            {
                var samples = Enumerable.Repeat(0.2f, 48_000).ToArray();
                writer.WriteSamples(samples, 0, samples.Length);
            }
            await using var player = new StreamingMusicPlayer();
            player.Add(path);
            player.Play();
            var block = new float[960];
            var heard = false;
            for (var i = 0; i < 30 && !heard; i++)
            {
                await Task.Delay(10);
                player.Read(block);
                heard = block.Any(sample => Math.Abs(sample) > 0.01f);
            }
            Assert.True(heard);
            Assert.True(player.GetPlayback().Position > TimeSpan.Zero);
            player.Pause();
            player.Read(block);
            Assert.All(block, sample => Assert.Equal(0, sample));
        }
        finally { File.Delete(path); }
    }
}
