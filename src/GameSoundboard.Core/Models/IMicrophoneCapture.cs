namespace GameSoundboard.Core.Models;

public interface IMicrophoneCapture : IDisposable
{
    string? DeviceId { get; }
    bool IsCapturing { get; }
    double PeakDb { get; }
    int SampleRate { get; }
    int Channels { get; }
    long CapturedFrames { get; }
    string? LastError { get; }

    Task StartAsync(string deviceId, CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
}
