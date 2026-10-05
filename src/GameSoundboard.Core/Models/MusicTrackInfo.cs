namespace GameSoundboard.Core.Models;

public sealed record MusicTrackInfo(string FilePath, string Name);

public enum MusicLoopMode { Off, Single, Playlist }

public sealed record MusicPlaybackInfo(string? Name, TimeSpan Position, TimeSpan Duration, bool IsPlaying, MusicLoopMode LoopMode, string? Error = null);
