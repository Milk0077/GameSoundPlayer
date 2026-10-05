namespace GameSoundboard.Core.Models;

public interface IAudioEngine : IDisposable
{
    bool IsRunning { get; }
    bool IsMicrophoneCapturing { get; }
    string? MicrophoneDeviceId { get; }
    string? MicrophoneError { get; }
    double MicrophonePeakDb { get; }
    int MicrophoneSampleRate { get; }
    int MicrophoneChannels { get; }
    long MixerPeriods { get; }
    long LateMixerPeriods { get; }
    long DroppedOutputBlocks { get; }
    long MicrophoneUnderruns { get; }
    long MicrophoneOverruns { get; }
    long DroppedSoundTriggers { get; }
    int BufferMilliseconds { get; }
    bool MicMonitorEnabled { get; }

    Task StartAsync();
    Task StopAsync();
    Task SetMicrophoneAsync(string? deviceId);
    Task SetBufferMillisecondsAsync(int milliseconds);
    Task SetMicMonitorAsync(bool enabled);
    void SetBusVolume(AudioBus bus, float volume);
    void SetMute(AudioBus bus, bool muted);
    double GetBusPeakDb(AudioBus bus);
    Task<SoundEffectInfo> AddSoundAsync(string filePath, CancellationToken cancellationToken = default);
    Task<SoundEffectInfo> RestoreSoundAsync(string filePath, Guid id, string name, float volume,
        CancellationToken cancellationToken = default);
    IReadOnlyList<SoundEffectInfo> GetSounds();
    bool PlaySound(Guid soundId);
    void StopSound(Guid soundId);
    void StopAllSounds();
    bool SetSoundVolume(Guid soundId, float volume);
    bool RenameSound(Guid soundId, string name);
    bool RemoveSound(Guid soundId);
    IReadOnlyList<MusicTrackInfo> GetMusicTracks();
    void AddMusic(string path);
    void RemoveMusic(int index);
    void PlayMusic();
    void PlayMusicAt(int index);
    void PauseMusic();
    void StopMusic();
    void NextMusic();
    void PreviousMusic();
    void SeekMusic(TimeSpan position);
    void SetMusicLoopMode(MusicLoopMode mode);
    MusicPlaybackInfo GetMusicPlayback();
    bool IsRecording { get; }
    void StartLocalRecording(string path);
    void StopLocalRecording();
}
