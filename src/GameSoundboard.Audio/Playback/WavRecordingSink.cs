using GameSoundboard.Audio.Buffers;
using GameSoundboard.Core.Models;
using NAudio.Wave;

namespace GameSoundboard.Audio.Playback;

/// <summary>Nonblocking mixer sink with a dedicated WAV writer task.</summary>
public sealed class WavRecordingSink : IAudioOutputSink
{
    private readonly SpscFloatRingBuffer _ring = new(48_000 * 10, 2);
    private readonly AutoResetEvent _hasData = new(false);
    private readonly Task _writer;
    private int _stopping;
    private int _activeWriters;
    private string? _error;

    public WavRecordingSink(string path)
    {
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var fileWriter = new WaveFileWriter(fullPath, WaveFormat.CreateIeeeFloatWaveFormat(48_000, 2));
        _writer = Task.Run(() => WriteLoop(fileWriter));
    }

    public long Overruns => _ring.Overruns;
    public string? Error => Volatile.Read(ref _error);

    public bool TryWrite(ReadOnlySpan<float> samples)
    {
        if (Volatile.Read(ref _stopping) != 0) return false;
        Interlocked.Increment(ref _activeWriters);
        try
        {
            if (Volatile.Read(ref _stopping) != 0) return false;
            var written = _ring.TryWrite(samples);
            if (written) _hasData.Set();
            return written;
        }
        finally { Interlocked.Decrement(ref _activeWriters); }
    }

    private void WriteLoop(WaveFileWriter writer)
    {
        try
        {
            var block = new float[4096];
            while (Volatile.Read(ref _stopping) == 0 || _ring.BufferedFrames > 0)
            {
                var samples = Math.Min(block.Length, _ring.BufferedFrames * 2) & ~1;
                if (samples == 0) { _hasData.WaitOne(50); continue; }
                var read = _ring.Read(block.AsSpan(0, samples));
                if (read > 0) writer.WriteSamples(block, 0, read);
            }
        }
        catch (Exception exception) { Volatile.Write(ref _error, exception.Message); }
        finally { writer.Dispose(); }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _stopping, 1) != 0) return;
        var spinner = new SpinWait();
        while (Volatile.Read(ref _activeWriters) != 0) spinner.SpinOnce();
        _hasData.Set();
        _writer.Wait();
        _hasData.Dispose();
    }
}
