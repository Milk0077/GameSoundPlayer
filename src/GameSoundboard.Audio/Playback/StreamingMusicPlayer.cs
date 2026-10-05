using GameSoundboard.Audio.Buffers;
using GameSoundboard.Audio.Resampling;
using GameSoundboard.Core.Models;
using NAudio.Wave;
using NAudio.Vorbis;

namespace GameSoundboard.Audio.Playback;

/// <summary>Streams music on a background task. The mixer only reads from a bounded PCM ring.</summary>
public sealed class StreamingMusicPlayer : ISampleProvider, IDisposable, IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly List<MusicTrackInfo> _tracks = [];
    private Session? _session;
    private int _index;
    private MusicLoopMode _loopMode;
    private bool _disposed;

    public WaveFormat WaveFormat { get; } = NAudio.Wave.WaveFormat.CreateIeeeFloatWaveFormat(48_000, 2);

    public IReadOnlyList<MusicTrackInfo> GetTracks()
    {
        lock (_gate) return _tracks.ToArray();
    }

    public void Add(string path)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!File.Exists(path)) throw new FileNotFoundException("音乐文件不存在。", path);
        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension is not (".wav" or ".mp3" or ".flac" or ".ogg" or ".m4a"))
            throw new NotSupportedException("音乐支持 WAV、MP3、FLAC、OGG、M4A。系统须有相应媒体解码器。");
        using var reader = OpenReader(path);
        if (reader.TotalTime <= TimeSpan.Zero) throw new InvalidDataException("音乐文件时长无效。");
        lock (_gate) _tracks.Add(new MusicTrackInfo(Path.GetFullPath(path), Path.GetFileNameWithoutExtension(path)));
    }

    public void Remove(int index)
    {
        lock (_gate)
        {
            if (index < 0 || index >= _tracks.Count) return;
            var wasCurrent = index == _index;
            _tracks.RemoveAt(index);
            if (_tracks.Count == 0) { StopLocked(); _index = 0; return; }
            if (index < _index) _index--;
            else if (_index >= _tracks.Count) _index = _tracks.Count - 1;
            if (wasCurrent && _session is not null) StartLocked(TimeSpan.Zero);
        }
    }

    public void Play()
    {
        lock (_gate)
        {
            if (_tracks.Count == 0) return;
            if (_session is null) StartLocked(TimeSpan.Zero);
            else Volatile.Write(ref _session.Playing, 1);
        }
    }

    public void PlayAt(int index)
    {
        lock (_gate)
        {
            if (index < 0 || index >= _tracks.Count) return;
            _index = index;
            StartLocked(TimeSpan.Zero);
        }
    }

    public void Pause()
    {
        lock (_gate) if (_session is not null) Volatile.Write(ref _session.Playing, 0);
    }

    public void Stop()
    {
        lock (_gate) StopLocked();
    }

    public void Next() => Shift(1);
    public void Previous() => Shift(-1);

    private void Shift(int direction)
    {
        lock (_gate)
        {
            if (_tracks.Count == 0) return;
            _index = (_index + direction + _tracks.Count) % _tracks.Count;
            StartLocked(TimeSpan.Zero);
        }
    }

    public void Seek(TimeSpan position)
    {
        lock (_gate)
        {
            if (_session is null) return;
            var wasPlaying = Volatile.Read(ref _session.Playing) != 0;
            var duration = _session.Duration;
            StartLocked(TimeSpan.FromTicks(Math.Clamp(position.Ticks, 0, duration.Ticks)));
            if (!wasPlaying && _session is not null) Volatile.Write(ref _session.Playing, 0);
        }
    }

    public void SetLoopMode(MusicLoopMode mode)
    {
        lock (_gate) _loopMode = mode;
    }

    public MusicPlaybackInfo GetPlayback()
    {
        lock (_gate)
        {
            if (_tracks.Count == 0) return new(null, TimeSpan.Zero, TimeSpan.Zero, false, _loopMode);
            var session = _session;
            var position = session is null ? TimeSpan.Zero :
                TimeSpan.FromSeconds(session.StartAt.TotalSeconds + Interlocked.Read(ref session.ConsumedSamples) / 2.0 / 48_000);
            return new(_tracks[_index].Name, position, session?.Duration ?? TimeSpan.Zero,
                session is not null && Volatile.Read(ref session.Playing) != 0, _loopMode,
                session is null ? null : Volatile.Read(ref session.Error));
        }
    }

    public int Read(Span<float> buffer)
    {
        var session = Volatile.Read(ref _session);
        if (session is null || Volatile.Read(ref session.Playing) == 0)
        {
            buffer.Clear();
            return buffer.Length;
        }
        var read = session.Ring.Read(buffer);
        Interlocked.Add(ref session.ConsumedSamples, read);
        return buffer.Length;
    }

    private void StartLocked(TimeSpan position)
    {
        CancelSession(_session);
        var track = _tracks[_index];
        var session = new Session(track, position);
        Volatile.Write(ref _session, session);
        session.Worker = Task.Run(() => FillAsync(session));
    }

    private void StopLocked()
    {
        CancelSession(_session);
        Volatile.Write(ref _session, null);
    }

    private static void CancelSession(Session? session)
    {
        if (session is null) return;
        try { session.Cancel.Cancel(); }
        catch (ObjectDisposedException) { }
    }

    private async Task FillAsync(Session session)
    {
        try
        {
            using var reader = OpenReader(session.Track.FilePath);
            reader.CurrentTime = session.StartAt;
            session.Duration = reader.TotalTime;
            var source = AudioFormatAdapter.To48kStereo(reader as ISampleProvider ?? reader.ToSampleProvider());
            var buffer = new float[4096];
            var remainingSamples = (long)Math.Max(0, Math.Ceiling((session.Duration - session.StartAt).TotalSeconds * 48_000) * 2);
            while (!session.Cancel.IsCancellationRequested && remainingSamples > 0)
            {
                if (Volatile.Read(ref session.Playing) == 0 || session.Ring.BufferedFrames > 48_000)
                {
                    await Task.Delay(10, session.Cancel.Token).ConfigureAwait(false);
                    continue;
                }
                var read = source.Read(buffer.AsSpan(0, (int)Math.Min(buffer.Length, remainingSamples)));
                if (read <= 0) break;
                remainingSamples -= read;
                while (!session.Ring.TryWrite(buffer.AsSpan(0, read)))
                    await Task.Delay(5, session.Cancel.Token).ConfigureAwait(false);
            }
            while (!session.Cancel.IsCancellationRequested && session.Ring.BufferedFrames > 0)
                await Task.Delay(10, session.Cancel.Token).ConfigureAwait(false);
            if (!session.Cancel.IsCancellationRequested) AdvanceAfterEnd(session);
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) { Volatile.Write(ref session.Error, exception.Message); }
        finally { session.Cancel.Dispose(); }
    }

    private void AdvanceAfterEnd(Session completed)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(_session, completed)) return;
            if (_loopMode == MusicLoopMode.Single) { StartLocked(TimeSpan.Zero); return; }
            if (_index + 1 < _tracks.Count) { _index++; StartLocked(TimeSpan.Zero); return; }
            if (_loopMode == MusicLoopMode.Playlist && _tracks.Count > 0) { _index = 0; StartLocked(TimeSpan.Zero); return; }
            StopLocked();
        }
    }

    private static WaveStream OpenReader(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".wav" => new WaveFileReader(path),
        ".ogg" => new VorbisWaveReader(path),
        _ => new MediaFoundationReader(path)
    };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }

    public async ValueTask DisposeAsync()
    {
        Session? session;
        lock (_gate)
        {
            session = _session;
            StopLocked();
            _disposed = true;
        }
        if (session?.Worker is not null) await session.Worker.ConfigureAwait(false);
    }

    private sealed class Session(MusicTrackInfo track, TimeSpan startAt)
    {
        public MusicTrackInfo Track { get; } = track;
        public TimeSpan StartAt { get; } = startAt;
        public TimeSpan Duration;
        public readonly SpscFloatRingBuffer Ring = new(96_000, 2);
        public readonly CancellationTokenSource Cancel = new();
        public Task? Worker;
        public int Playing = 1;
        public long ConsumedSamples;
        public string? Error;
    }
}
