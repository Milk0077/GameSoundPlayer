namespace GameSoundboard.Audio.Playback;

internal sealed class SoundClip(Guid id, string name, string filePath, float[] samples)
{
    public Guid Id { get; } = id;
    public string Name = name;
    public string FilePath { get; } = filePath;
    public float[] Samples { get; } = samples;
    public float Volume = 1;
}
