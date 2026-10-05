using System.Diagnostics;
using GameSoundboard.Core.Models;

namespace GameSoundboard.Audio.Mixer;

/// <summary>Drives the first-stage mixer at 10 ms blocks. A future driver sink owns the final output clock.</summary>
public sealed class AudioMixerRunner : IDisposable
{
    private int _periodMilliseconds = 10;
    private readonly AudioMixer _mixer;
    private readonly IAudioOutputSink _sink;
    private readonly ManualResetEvent _stop = new(false);
    private Thread? _thread;
    private long _periods;
    private long _latePeriods;
    private long _droppedOutputBlocks;
    private string? _lastError;
    private bool _disposed;

    public AudioMixerRunner(AudioMixer mixer, IAudioOutputSink sink)
    {
        _mixer = mixer;
        _sink = sink;
    }

    public bool IsRunning => _thread?.IsAlive == true;
    public int PeriodMilliseconds => Volatile.Read(ref _periodMilliseconds);

    public void SetPeriodMilliseconds(int milliseconds)
    {
        if (milliseconds is not (5 or 10 or 20 or 40)) throw new ArgumentOutOfRangeException(nameof(milliseconds));
        if (IsRunning) throw new InvalidOperationException("请先停止混音线程再调整缓冲周期。");
        Volatile.Write(ref _periodMilliseconds, milliseconds);
    }
    public long Periods => Interlocked.Read(ref _periods);
    public long LatePeriods => Interlocked.Read(ref _latePeriods);
    public long DroppedOutputBlocks => Interlocked.Read(ref _droppedOutputBlocks);
    public string? LastError => Volatile.Read(ref _lastError);

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsRunning) return;
        _stop.Reset();
        Volatile.Write(ref _lastError, null);
        _thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "GameSoundboard Audio Mixer",
            Priority = ThreadPriority.AboveNormal
        };
        _thread.Start();
    }

    private void Run()
    {
        var periodMilliseconds = PeriodMilliseconds;
        var block = new float[AudioMixer.SampleRate * periodMilliseconds / 1000 * AudioMixer.Channels];
        var periodTicks = Stopwatch.Frequency * periodMilliseconds / 1000;
        var nextTick = Stopwatch.GetTimestamp();
        try
        {
            while (!_stop.WaitOne(0))
            {
                var now = Stopwatch.GetTimestamp();
                var remainingTicks = nextTick - now;
                if (remainingTicks > Stopwatch.Frequency / 500)
                {
                    var waitMs = Math.Max(1, (int)(remainingTicks * 1000 / Stopwatch.Frequency) - 1);
                    _stop.WaitOne(waitMs);
                    continue;
                }
                if (remainingTicks > 0)
                {
                    Thread.Yield();
                    continue;
                }

                _mixer.Mix(block);
                if (!_sink.TryWrite(block)) Interlocked.Increment(ref _droppedOutputBlocks);
                Interlocked.Increment(ref _periods);
                nextTick += periodTicks;
                if (Stopwatch.GetTimestamp() - nextTick >= periodTicks)
                {
                    Interlocked.Increment(ref _latePeriods);
                    nextTick = Stopwatch.GetTimestamp() + periodTicks;
                }
            }
        }
        catch (Exception exception)
        {
            Volatile.Write(ref _lastError, exception.Message);
        }
    }

    public async Task StopAsync()
    {
        _stop.Set();
        var thread = _thread;
        if (thread is not null && thread.IsAlive)
            await Task.Run(thread.Join).ConfigureAwait(false);
        _thread = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _stop.Set();
        _thread?.Join();
        _sink.Dispose();
        _stop.Dispose();
    }
}
