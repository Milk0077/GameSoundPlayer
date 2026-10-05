namespace GameSoundboard.Core.Models;

/// <summary>Consumes 48 kHz, float32, interleaved stereo frames without blocking the mixer.</summary>
public interface IAudioOutputSink : IDisposable
{
    bool TryWrite(ReadOnlySpan<float> samples);
}
