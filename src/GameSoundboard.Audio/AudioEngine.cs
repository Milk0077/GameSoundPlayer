using System.Collections.Concurrent;
using GameSoundboard.Audio.Capture;
using GameSoundboard.Audio.Mixer;
using GameSoundboard.Audio.Playback;
using GameSoundboard.Audio.Resampling;
using GameSoundboard.Core.Models;

namespace GameSoundboard.Audio;

public sealed class AudioEngine : IAudioEngine
{
    private readonly SemaphoreSlim _microphoneGate = new(1, 1);
    private readonly MicrophoneCapture _capture = new();
    private readonly SilenceSampleProvider _silence = new();
    private readonly SoundboardSampleProvider _soundboard = new();
    private readonly StreamingMusicPlayer _music = new();
    private readonly ConcurrentDictionary<Guid, SoundClip> _sounds = new();
    private readonly object _soundsGate = new();
    private long _cachedSoundBytes;
    private readonly AudioMixer _mixer;
    private readonly AudioMixerRunner _runner;
    private readonly RecordableOutputSink _output;
    private MicrophoneInputSource? _microphoneSource;
    private MicrophoneMonitor? _monitor;
    private bool _monitorEnabled;
    private bool _disposed;

    public AudioEngine(IAudioOutputSink? outputSink = null)
    {
        _mixer = new AudioMixer(_silence, _soundboard, _music);
        _output = new RecordableOutputSink(outputSink ?? new NullAudioOutputSink());
        _runner = new AudioMixerRunner(_mixer, _output);
    }

    public bool IsRunning => _runner.IsRunning;
    public bool IsMicrophoneCapturing => _capture.IsCapturing;
    public string? MicrophoneDeviceId => _capture.DeviceId;
    public string? MicrophoneError => _capture.LastError;
    public double MicrophonePeakDb => _capture.PeakDb;
    public int MicrophoneSampleRate => _capture.SampleRate;
    public int MicrophoneChannels => _capture.Channels;
    public long MixerPeriods => _runner.Periods;
    public long LateMixerPeriods => _runner.LatePeriods;
    public long DroppedOutputBlocks => _runner.DroppedOutputBlocks;
    public long MicrophoneUnderruns => _microphoneSource?.Underruns ?? 0;
    public long MicrophoneOverruns => _microphoneSource?.Overruns ?? 0;
    public long DroppedSoundTriggers => _soundboard.DroppedTriggers;
    public int BufferMilliseconds => _runner.PeriodMilliseconds;
    public bool MicMonitorEnabled => _monitorEnabled;
    public bool IsRecording => _output.IsRecording;

    public Task StartAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _runner.Start();
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        await SetMicrophoneAsync(null).ConfigureAwait(false);
        await _runner.StopAsync().ConfigureAwait(false);
    }

    public async Task SetBufferMillisecondsAsync(int milliseconds)
    {
        if (milliseconds is not (5 or 10 or 20 or 40)) throw new ArgumentOutOfRangeException(nameof(milliseconds));
        if (milliseconds == BufferMilliseconds) return;
        var microphoneId = MicrophoneDeviceId;
        if (microphoneId is not null) await SetMicrophoneAsync(null).ConfigureAwait(false);
        var restart = _runner.IsRunning;
        if (restart) await _runner.StopAsync().ConfigureAwait(false);
        _runner.SetPeriodMilliseconds(milliseconds);
        _capture.RequestedBufferMilliseconds = milliseconds;
        if (restart) _runner.Start();
        if (microphoneId is not null) await SetMicrophoneAsync(microphoneId).ConfigureAwait(false);
    }

    public async Task SetMicrophoneAsync(string? deviceId)
    {
        await _microphoneGate.WaitAsync().ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (deviceId is not null && _capture.IsCapturing &&
                StringComparer.OrdinalIgnoreCase.Equals(deviceId, _capture.DeviceId)) return;

            _mixer.SetSource(AudioBus.Microphone, _silence);
            _monitor?.Dispose();
            _monitor = null;
            _microphoneSource?.Dispose();
            _microphoneSource = null;
            await _capture.StopAsync().ConfigureAwait(false);
            if (deviceId is null) return;

            await _capture.StartAsync(deviceId).ConfigureAwait(false);
            var format = _capture.CurrentFormat ?? throw new InvalidOperationException("麦克风没有返回有效音频格式。");
            var source = new MicrophoneInputSource(_capture, format);
            _microphoneSource = source;
            _mixer.SetSource(AudioBus.Microphone, source);
            if (_monitorEnabled)
                _monitor = await Task.Run(() => new MicrophoneMonitor(_capture, format)).ConfigureAwait(false);
        }
        finally
        {
            _microphoneGate.Release();
        }
    }

    public async Task SetMicMonitorAsync(bool enabled)
    {
        await _microphoneGate.WaitAsync().ConfigureAwait(false);
        try
        {
            _monitorEnabled = enabled;
            if (!enabled)
            {
                _monitor?.Dispose();
                _monitor = null;
            }
            else if (_monitor is null && _capture.CurrentFormat is { } format && _capture.IsCapturing)
            {
                _monitor = await Task.Run(() => new MicrophoneMonitor(_capture, format)).ConfigureAwait(false);
            }
        }
        finally { _microphoneGate.Release(); }
    }

    public void SetBusVolume(AudioBus bus, float volume) => _mixer.SetVolume(bus, volume);
    public void SetMute(AudioBus bus, bool muted) => _mixer.SetMute(bus, muted);
    public double GetBusPeakDb(AudioBus bus) => _mixer.GetPeakDb(bus);

    public async Task<SoundEffectInfo> AddSoundAsync(string filePath, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var clip = await Task.Run(() => SoundClipDecoder.Decode(filePath), cancellationToken).ConfigureAwait(false);
        AddClip(clip);
        return ToInfo(clip);
    }

    public async Task<SoundEffectInfo> RestoreSoundAsync(string filePath, Guid id, string name, float volume,
        CancellationToken cancellationToken = default)
    {
        if (id == Guid.Empty || !float.IsFinite(volume) || volume is < 0 or > 2)
            throw new ArgumentException("保存的音效参数无效。");
        var decoded = await Task.Run(() => SoundClipDecoder.Decode(filePath), cancellationToken).ConfigureAwait(false);
        var clip = new SoundClip(id, name, decoded.FilePath, decoded.Samples) { Volume = volume };
        AddClip(clip);
        return ToInfo(clip);
    }

    private void AddClip(SoundClip clip)
    {
        lock (_soundsGate)
        {
            if (_sounds.ContainsKey(clip.Id)) throw new InvalidOperationException("音效 ID 已存在。");
            var bytes = (long)clip.Samples.Length * sizeof(float);
            if (_sounds.Count >= 256 || _cachedSoundBytes + bytes > 256L * 1024 * 1024)
                throw new InvalidOperationException("音效缓存已满（最多 256 个、256 MiB）。");
            _sounds[clip.Id] = clip;
            _cachedSoundBytes += bytes;
        }
    }

    public IReadOnlyList<SoundEffectInfo> GetSounds() =>
        _sounds.Values.Select(ToInfo).OrderBy(sound => sound.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();

    public bool PlaySound(Guid soundId) => _sounds.TryGetValue(soundId, out var clip) && _soundboard.Play(clip);
    public void StopSound(Guid soundId) => _soundboard.Stop(soundId);
    public void StopAllSounds() => _soundboard.StopAll();

    public bool SetSoundVolume(Guid soundId, float volume)
    {
        if (!float.IsFinite(volume) || volume is < 0 or > 2) throw new ArgumentOutOfRangeException(nameof(volume));
        if (!_sounds.TryGetValue(soundId, out var clip)) return false;
        Volatile.Write(ref clip.Volume, volume);
        return true;
    }

    public bool RenameSound(Guid soundId, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name.Trim().Length > 80) throw new ArgumentOutOfRangeException(nameof(name));
        if (!_sounds.TryGetValue(soundId, out var clip)) return false;
        Volatile.Write(ref clip.Name, name.Trim());
        return true;
    }

    public bool RemoveSound(Guid soundId)
    {
        lock (_soundsGate)
        {
            if (!_sounds.TryRemove(soundId, out var clip)) return false;
            _cachedSoundBytes -= (long)clip.Samples.Length * sizeof(float);
        }
        StopSound(soundId);
        return true;
    }

    public IReadOnlyList<MusicTrackInfo> GetMusicTracks() => _music.GetTracks();
    public void AddMusic(string path) => _music.Add(path);
    public void RemoveMusic(int index) => _music.Remove(index);
    public void PlayMusic() => _music.Play();
    public void PlayMusicAt(int index) => _music.PlayAt(index);
    public void PauseMusic() => _music.Pause();
    public void StopMusic() => _music.Stop();
    public void NextMusic() => _music.Next();
    public void PreviousMusic() => _music.Previous();
    public void SeekMusic(TimeSpan position) => _music.Seek(position);
    public void SetMusicLoopMode(MusicLoopMode mode) => _music.SetLoopMode(mode);
    public MusicPlaybackInfo GetMusicPlayback() => _music.GetPlayback();
    public void StartLocalRecording(string path) => _output.StartRecording(path);
    public void StopLocalRecording() => _output.StopRecording();

    private static SoundEffectInfo ToInfo(SoundClip clip) => new(
        clip.Id,
        Volatile.Read(ref clip.Name),
        clip.FilePath,
        TimeSpan.FromSeconds(clip.Samples.Length / 2.0 / AudioMixer.SampleRate),
        Volatile.Read(ref clip.Volume));

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _runner.Dispose();
        _monitor?.Dispose();
        _music.Dispose();
        _microphoneSource?.Dispose();
        _capture.Dispose();
        _microphoneGate.Dispose();
    }
}
