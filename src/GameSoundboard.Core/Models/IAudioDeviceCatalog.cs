namespace GameSoundboard.Core.Models;

public interface IAudioDeviceCatalog : IDisposable
{
    event EventHandler? DevicesChanged;

    IReadOnlyList<AudioInputDevice> GetInputDevices();
}
