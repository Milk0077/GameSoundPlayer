using NAudio.Wave;

namespace GameSoundboard.Audio.Resampling;

public sealed class StereoChannelSampleProvider : ISampleProvider
{
    private const int ChunkFrames = 1920;
    private readonly ISampleProvider _source;
    private readonly float[] _scratch;
    private readonly int _sourceChannels;

    public StereoChannelSampleProvider(ISampleProvider source)
    {
        _source = source;
        _sourceChannels = source.WaveFormat.Channels;
        if (_sourceChannels < 1) throw new ArgumentException("Source must have at least one channel.", nameof(source));
        _scratch = new float[ChunkFrames * _sourceChannels];
        WaveFormat = NAudio.Wave.WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, 2);
    }

    public WaveFormat WaveFormat { get; }

    public int Read(Span<float> buffer)
    {
        if ((buffer.Length & 1) != 0) throw new ArgumentException("Stereo buffer must contain full frames.", nameof(buffer));
        for (var frameOffset = 0; frameOffset < buffer.Length / 2;)
        {
            var frames = Math.Min(ChunkFrames, buffer.Length / 2 - frameOffset);
            var input = _scratch.AsSpan(0, frames * _sourceChannels);
            input.Clear();
            var samplesRead = Math.Clamp(_source.Read(input), 0, input.Length);
            var framesRead = samplesRead / _sourceChannels;
            for (var frame = 0; frame < frames; frame++)
            {
                float left = 0;
                float right = 0;
                if (frame < framesRead)
                {
                    var start = frame * _sourceChannels;
                    if (_sourceChannels == 1) left = right = input[start];
                    else if (_sourceChannels == 2)
                    {
                        left = input[start];
                        right = input[start + 1];
                    }
                    else
                    {
                        float sum = 0;
                        for (var channel = 0; channel < _sourceChannels; channel++) sum += input[start + channel];
                        left = right = sum / _sourceChannels;
                    }
                }
                var output = (frameOffset + frame) * 2;
                buffer[output] = left;
                buffer[output + 1] = right;
            }
            frameOffset += frames;
        }
        return buffer.Length;
    }
}
