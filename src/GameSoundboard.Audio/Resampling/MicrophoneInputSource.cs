using GameSoundboard.Audio.Capture;
using NAudio.Wave;

namespace GameSoundboard.Audio.Resampling;

public sealed class MicrophoneInputSource : ISampleProvider, IDisposable
{
    private readonly CapturePcmSampleProvider _capturePcm;
    private readonly ISampleProvider _stereo;

    public MicrophoneInputSource(MicrophoneCapture capture, WaveFormat inputFormat)
    {
        _capturePcm = new CapturePcmSampleProvider(capture, inputFormat);
        _stereo = AudioFormatAdapter.To48kStereo(_capturePcm);
    }

    public WaveFormat WaveFormat => _stereo.WaveFormat;
    public long Overruns => _capturePcm.Overruns;
    public long Underruns => _capturePcm.Underruns;
    public int BufferedFrames => _capturePcm.BufferedFrames;

    public int Read(Span<float> buffer) => _stereo.Read(buffer);

    public void Dispose() => _capturePcm.Dispose();
}
