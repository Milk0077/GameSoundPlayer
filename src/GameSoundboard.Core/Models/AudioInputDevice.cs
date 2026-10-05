namespace GameSoundboard.Core.Models;

public enum AudioDeviceAvailability
{
    Active,
    Disabled,
    Unplugged,
    NotPresent,
    Disconnected
}

public sealed record AudioInputDevice(
    string Id,
    string Name,
    AudioDeviceAvailability Availability,
    bool IsDefault)
{
    public string DisplayName => IsDefault ? $"{Name} (默认通信设备)" : Name;
}
