using GameSoundboard.Core.Models;

namespace GameSoundboard.Audio.Playback;

public sealed class RecordableOutputSink(IAudioOutputSink output) : IAudioOutputSink
{
    private WavRecordingSink? _recording;

    public bool IsRecording => Volatile.Read(ref _recording) is not null;

    public void StartRecording(string path)
    {
        var next = new WavRecordingSink(path);
        var old = Interlocked.Exchange(ref _recording, next);
        old?.Dispose();
    }

    public void StopRecording()
    {
        var old = Interlocked.Exchange(ref _recording, null);
        old?.Dispose();
    }

    public bool TryWrite(ReadOnlySpan<float> samples)
    {
        var outputSuccess = output.TryWrite(samples);
        var recording = Volatile.Read(ref _recording);
        if (recording is not null && !recording.TryWrite(samples)) return false;
        return outputSuccess;
    }

    public void Dispose()
    {
        StopRecording();
        output.Dispose();
    }
}
