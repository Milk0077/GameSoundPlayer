using GameSoundboard.Core.Models;

namespace GameSoundboard.Audio.Playback;

public sealed class NullAudioOutputSink : IAudioOutputSink
{
    public bool TryWrite(ReadOnlySpan<float> samples) => true;
    public void Dispose() { }
}
