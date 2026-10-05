namespace GameSoundboard.Core.Settings;

public sealed class AppSettings
{
    public int Version { get; set; } = 1;
    public string? MicrophoneDeviceId { get; set; }
    public string? OutputDeviceId { get; set; }
    public List<SoundSettings> Sounds { get; set; } = [];
    public List<string> MusicPaths { get; set; } = [];
    public List<HotkeySettings> Hotkeys { get; set; } = [];
    public List<BusSettings> Buses { get; set; } = [];
    public int MusicLoopMode { get; set; }
    public int AudioBufferMilliseconds { get; set; } = 10;
    public bool MicMonitorEnabled { get; set; }
    public double WindowWidth { get; set; } = 1120;
    public double WindowHeight { get; set; } = 650;
}

public sealed record SoundSettings(Guid Id, string FilePath, string Name, float Volume);
public sealed record HotkeySettings(Guid SoundId, uint Modifiers, uint VirtualKey, string DisplayName);
public sealed record BusSettings(int Bus, float Volume, bool Muted);
