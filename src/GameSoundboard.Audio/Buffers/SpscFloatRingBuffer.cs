namespace GameSoundboard.Audio.Buffers;

/// <summary>Single producer/single consumer bounded PCM ring. A full ring drops the incoming packet.</summary>
public sealed class SpscFloatRingBuffer
{
    private readonly float[] _samples;
    private readonly int _channels;
    private readonly int _capacityFrames;
    private long _readFrames;
    private long _writtenFrames;
    private long _overruns;
    private long _underruns;

    public SpscFloatRingBuffer(int capacityFrames, int channels)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacityFrames, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(channels, 1);
        _capacityFrames = capacityFrames;
        _channels = channels;
        _samples = new float[checked(capacityFrames * channels)];
    }

    public long Overruns => Interlocked.Read(ref _overruns);
    public long Underruns => Interlocked.Read(ref _underruns);
    public int BufferedFrames => (int)(Volatile.Read(ref _writtenFrames) - Volatile.Read(ref _readFrames));

    /// <summary>Called only by the consumer to drop stale audio after a scheduling pause.</summary>
    public int TrimToLatest(int keepFrames)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(keepFrames);
        var read = _readFrames;
        var available = Volatile.Read(ref _writtenFrames) - read;
        var discarded = (int)Math.Max(0, available - keepFrames);
        if (discarded == 0) return 0;
        Volatile.Write(ref _readFrames, read + discarded);
        Interlocked.Increment(ref _overruns);
        return discarded;
    }

    public bool TryWrite(ReadOnlySpan<float> interleavedSamples)
    {
        if (interleavedSamples.Length % _channels != 0)
            throw new ArgumentException("Input must contain complete frames.", nameof(interleavedSamples));
        var frames = interleavedSamples.Length / _channels;
        var write = _writtenFrames;
        var read = Volatile.Read(ref _readFrames);
        if (frames > _capacityFrames - (write - read))
        {
            Interlocked.Increment(ref _overruns);
            return false;
        }

        for (var frame = 0; frame < frames; frame++)
        {
            var destination = (int)((write + frame) % _capacityFrames) * _channels;
            interleavedSamples.Slice(frame * _channels, _channels).CopyTo(_samples.AsSpan(destination, _channels));
        }
        Volatile.Write(ref _writtenFrames, write + frames);
        return true;
    }

    public int Read(Span<float> destination)
    {
        if (destination.Length % _channels != 0)
            throw new ArgumentException("Output must contain complete frames.", nameof(destination));
        var framesRequested = destination.Length / _channels;
        var read = _readFrames;
        var available = Volatile.Read(ref _writtenFrames) - read;
        var framesRead = (int)Math.Min(framesRequested, available);
        for (var frame = 0; frame < framesRead; frame++)
        {
            var source = (int)((read + frame) % _capacityFrames) * _channels;
            _samples.AsSpan(source, _channels).CopyTo(destination.Slice(frame * _channels, _channels));
        }
        Volatile.Write(ref _readFrames, read + framesRead);
        if (framesRead < framesRequested)
        {
            destination.Slice(framesRead * _channels).Clear();
            Interlocked.Increment(ref _underruns);
        }
        return framesRead * _channels;
    }
}
