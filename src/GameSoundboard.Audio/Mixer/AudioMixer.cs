using GameSoundboard.Audio.DSP;
using GameSoundboard.Core.Models;
using NAudio.Wave;

namespace GameSoundboard.Audio.Mixer;

public sealed class AudioMixer
{
    public const int SampleRate = 48_000;
    public const int Channels = 2;
    private const int MaxChunkSamples = 48_000 * Channels * 40 / 1000;

    private readonly Bus[] _buses;
    private readonly float[] _scratch = new float[MaxChunkSamples];
    private readonly SoftLimiter _limiter = new();
    private float _masterVolume = 1;
    private int _masterMuted;
    private int _masterPeakMilliDb = -60_000;
    private long _underruns;

    public AudioMixer(ISampleProvider microphone, ISampleProvider soundboard, ISampleProvider music)
    {
        _buses =
        [
            new Bus(Validate(microphone)),
            new Bus(Validate(soundboard)),
            new Bus(Validate(music))
        ];
    }

    public long Underruns => Interlocked.Read(ref _underruns);

    public void SetSource(AudioBus bus, ISampleProvider source) =>
        Volatile.Write(ref GetBus(bus).Source, Validate(source));

    public void SetVolume(AudioBus bus, float volume)
    {
        if (!float.IsFinite(volume) || volume is < 0 or > 2)
            throw new ArgumentOutOfRangeException(nameof(volume), "音量必须在 0 到 2 之间。");
        if (bus == AudioBus.Master) Volatile.Write(ref _masterVolume, volume);
        else Volatile.Write(ref GetBus(bus).Volume, volume);
    }

    public void SetMute(AudioBus bus, bool mute)
    {
        if (bus == AudioBus.Master) Volatile.Write(ref _masterMuted, mute ? 1 : 0);
        else Volatile.Write(ref GetBus(bus).Muted, mute ? 1 : 0);
    }

    public double GetPeakDb(AudioBus bus) =>
        (bus == AudioBus.Master ? Volatile.Read(ref _masterPeakMilliDb) : Volatile.Read(ref GetBus(bus).PeakMilliDb)) / 1000.0;

    /// <summary>Fills the requested stereo frames. Called only by the audio mixer/output thread.</summary>
    public void Mix(Span<float> output)
    {
        if ((output.Length & 1) != 0) throw new ArgumentException("Stereo output requires an even sample count.", nameof(output));

        for (var offset = 0; offset < output.Length;)
        {
            var count = Math.Min(MaxChunkSamples, output.Length - offset);
            var block = output.Slice(offset, count);
            block.Clear();

            foreach (var bus in _buses)
            {
                var input = _scratch.AsSpan(0, count);
                input.Clear();
                int read;
                try
                {
                    read = Volatile.Read(ref bus.Source).Read(input);
                }
                catch
                {
                    read = 0;
                }

                if (read < count) Interlocked.Increment(ref _underruns);
                read = Math.Clamp(read, 0, count);
                var volume = Volatile.Read(ref bus.Muted) == 0 ? Volatile.Read(ref bus.Volume) : 0;
                float peak = 0;
                for (var i = 0; i < read; i++)
                {
                    var sample = input[i] * volume;
                    if (!float.IsFinite(sample)) sample = 0;
                    block[i] += sample;
                    peak = Math.Max(peak, Math.Abs(sample));
                }
                Volatile.Write(ref bus.PeakMilliDb, ToMilliDb(peak));
            }

            var masterVolume = Volatile.Read(ref _masterMuted) == 0 ? Volatile.Read(ref _masterVolume) : 0;
            for (var i = 0; i < block.Length; i++) block[i] *= masterVolume;
            _limiter.Process(block);

            float masterPeak = 0;
            foreach (var sample in block) masterPeak = Math.Max(masterPeak, Math.Abs(sample));
            Volatile.Write(ref _masterPeakMilliDb, ToMilliDb(masterPeak));
            offset += count;
        }
    }

    private static ISampleProvider Validate(ISampleProvider source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.WaveFormat.SampleRate != SampleRate || source.WaveFormat.Channels != Channels ||
            source.WaveFormat.Encoding != WaveFormatEncoding.IeeeFloat)
            throw new ArgumentException("Mixer inputs must be 48 kHz, stereo, 32-bit float.", nameof(source));
        return source;
    }

    private Bus GetBus(AudioBus bus) => bus switch
    {
        AudioBus.Microphone => _buses[0],
        AudioBus.Soundboard => _buses[1],
        AudioBus.Music => _buses[2],
        _ => throw new ArgumentOutOfRangeException(nameof(bus))
    };

    private static int ToMilliDb(float peak) => peak <= 0
        ? -60_000
        : (int)Math.Round(Math.Clamp(20 * Math.Log10(peak), -60, 0) * 1000);

    private sealed class Bus(ISampleProvider source)
    {
        public ISampleProvider Source = source;
        public float Volume = 1;
        public int Muted;
        public int PeakMilliDb = -60_000;
    }
}
