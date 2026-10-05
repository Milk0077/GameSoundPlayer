namespace GameSoundboard.Core.Models;

public sealed record SoundEffectInfo(Guid Id, string Name, string FilePath, TimeSpan Duration, float Volume, string Hotkey = "");
