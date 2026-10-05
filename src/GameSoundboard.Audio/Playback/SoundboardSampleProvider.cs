using System.Collections.Concurrent;
using NAudio.Wave;

namespace GameSoundboard.Audio.Playback;

public sealed class SoundboardSampleProvider : ISampleProvider
{
    private const int MaxVoices = 64;
    private const int MaxCommands = 256;
    private readonly ConcurrentQueue<Command> _commands = new();
    private readonly List<Voice> _voices = new(MaxVoices);
    private int _queuedCommands;
    private int _generation;
    private long _droppedTriggers;

    public WaveFormat WaveFormat { get; } = NAudio.Wave.WaveFormat.CreateIeeeFloatWaveFormat(48_000, 2);
    public long DroppedTriggers => Interlocked.Read(ref _droppedTriggers);

    internal bool Play(SoundClip clip)
    {
        if (Interlocked.Increment(ref _queuedCommands) > MaxCommands)
        {
            Interlocked.Decrement(ref _queuedCommands);
            Interlocked.Increment(ref _droppedTriggers);
            return false;
        }
        _commands.Enqueue(new Command(new Voice(clip, Volatile.Read(ref _generation)), Guid.Empty));
        return true;
    }

    public void Stop(Guid soundId)
    {
        if (Interlocked.Increment(ref _queuedCommands) > MaxCommands)
        {
            Interlocked.Decrement(ref _queuedCommands);
            return;
        }
        _commands.Enqueue(new Command(null, soundId));
    }

    public void StopAll() => Interlocked.Increment(ref _generation);

    public int Read(Span<float> buffer)
    {
        buffer.Clear();
        var generation = Volatile.Read(ref _generation);
        _voices.RemoveAll(voice => voice.Generation != generation);

        while (_commands.TryDequeue(out var command))
        {
            Interlocked.Decrement(ref _queuedCommands);
            if (command.Voice is not null)
            {
                if (_voices.Count >= MaxVoices)
                {
                    _voices.RemoveAt(0);
                    Interlocked.Increment(ref _droppedTriggers);
                }
                if (command.Voice.Generation == generation) _voices.Add(command.Voice);
            }
            else
            {
                _voices.RemoveAll(voice => voice.Clip.Id == command.SoundId);
            }
        }

        for (var voiceIndex = _voices.Count - 1; voiceIndex >= 0; voiceIndex--)
        {
            var voice = _voices[voiceIndex];
            var remaining = voice.Clip.Samples.Length - voice.Position;
            var count = Math.Min(buffer.Length, remaining);
            var volume = Volatile.Read(ref voice.Clip.Volume);
            for (var i = 0; i < count; i++)
                buffer[i] += voice.Clip.Samples[voice.Position + i] * volume;
            voice.Position += count;
            if (voice.Position >= voice.Clip.Samples.Length) _voices.RemoveAt(voiceIndex);
        }
        return buffer.Length;
    }

    private sealed class Voice(SoundClip clip, int generation)
    {
        public SoundClip Clip { get; } = clip;
        public int Generation { get; } = generation;
        public int Position;
    }

    private readonly record struct Command(Voice? Voice, Guid SoundId);
}
