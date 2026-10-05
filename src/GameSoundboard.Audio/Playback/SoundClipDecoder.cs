using GameSoundboard.Audio.Resampling;
using NAudio.Wave;
using NAudio.Vorbis;

namespace GameSoundboard.Audio.Playback;

internal static class SoundClipDecoder
{
    private const int MaxSeconds = 30;
    private const int MaxSamples = 48_000 * 2 * MaxSeconds;

    public static SoundClip Decode(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("音效文件不存在。", path);
        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension is not (".wav" or ".mp3" or ".flac" or ".ogg" or ".m4a"))
            throw new NotSupportedException("音效支持 WAV、MP3、FLAC、OGG、M4A。");

        using WaveStream reader = extension switch
        {
            ".wav" => new WaveFileReader(path),
            ".ogg" => new VorbisWaveReader(path),
            _ => new MediaFoundationReader(path)
        };
        if (reader.TotalTime > TimeSpan.FromSeconds(MaxSeconds))
            throw new InvalidDataException("音效超过 30 秒，请将长音频加入音乐播放器。");

        var decodedSource = reader as ISampleProvider ?? reader.ToSampleProvider();
        var inputChannels = decodedSource.WaveFormat.Channels;
        var inputRate = decodedSource.WaveFormat.SampleRate;
        var maxInputSamples = Math.Min(8_000_000, checked(MaxSeconds * inputRate * inputChannels));
        var decoded = new List<float>(Math.Min(maxInputSamples, 96_000));
        var block = new float[4096 - 4096 % inputChannels];
        while (true)
        {
            var read = decodedSource.Read(block);
            if (read == 0) break;
            if (decoded.Count + read > maxInputSamples)
                throw new InvalidDataException("解码后的音效超过 30 秒限制。");
            decoded.AddRange(block.AsSpan(0, read));
        }
        if (decoded.Count == 0) throw new InvalidDataException("音效文件没有可播放的音频帧。");

        var source = AudioFormatAdapter.To48kStereo(new FiniteSampleProvider(decoded.ToArray(), inputRate, inputChannels));
        var targetFrames = (int)Math.Ceiling((decoded.Count / inputChannels) * 48_000.0 / inputRate);
        var targetSamples = Math.Min(MaxSamples, targetFrames * 2);
        var samples = new List<float>(targetSamples);
        while (samples.Count < targetSamples)
        {
            var read = source.Read(block.AsSpan(0, Math.Min(block.Length & ~1, targetSamples - samples.Count)));
            if (read == 0) break;
            samples.AddRange(block.AsSpan(0, read));
        }
        if ((samples.Count & 1) != 0) samples.RemoveAt(samples.Count - 1);
        return new SoundClip(Guid.NewGuid(), Path.GetFileNameWithoutExtension(path), Path.GetFullPath(path), samples.ToArray());
    }

    private sealed class FiniteSampleProvider(float[] samples, int sampleRate, int channels) : ISampleProvider
    {
        private int _position;
        public WaveFormat WaveFormat { get; } = NAudio.Wave.WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels);

        public int Read(Span<float> buffer)
        {
            var count = Math.Min(buffer.Length, samples.Length - _position);
            samples.AsSpan(_position, count).CopyTo(buffer);
            _position += count;
            return count;
        }
    }
}
