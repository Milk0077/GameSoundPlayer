using GameSoundboard.Audio.Capture;
using GameSoundboard.Audio.Resampling;
using NAudio.Wave;

namespace GameSoundboard.Audio.Playback;

/// <summary>Opt-in physical microphone monitor on the default speaker endpoint.</summary>
public sealed class MicrophoneMonitor : IDisposable
{
    private readonly MicrophoneInputSource _source;
    private readonly WasapiPlayer _player;

    public MicrophoneMonitor(MicrophoneCapture capture, WaveFormat captureFormat)
    {
        _source = new MicrophoneInputSource(capture, captureFormat);
        WasapiPlayer? player = null;
        try
        {
            player = new WasapiPlayerBuilder().WithLatency(20).Build();
            player.Init(_source.ToWaveProvider());
            player.Play();
            _player = player;
        }
        catch
        {
            player?.Dispose();
            _source.Dispose();
            throw;
        }
    }

    public int ActualLatencyMilliseconds => _player.LatencyMilliseconds;

    public void Dispose()
    {
        _player.Stop();
        _player.Dispose();
        _source.Dispose();
    }
}
