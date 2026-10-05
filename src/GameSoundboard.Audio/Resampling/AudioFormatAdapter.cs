using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace GameSoundboard.Audio.Resampling;

public static class AudioFormatAdapter
{
    public static ISampleProvider To48kStereo(ISampleProvider source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.WaveFormat.Encoding != WaveFormatEncoding.IeeeFloat)
            throw new ArgumentException("Source must be 32-bit float.", nameof(source));

        if (source.WaveFormat.SampleRate != 48_000)
            source = new WdlResamplingSampleProvider(source, 48_000);
        if (source.WaveFormat.Channels != 2)
            source = new StereoChannelSampleProvider(source);
        return source;
    }
}
