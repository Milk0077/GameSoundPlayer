using System.Runtime.InteropServices;
using GameSoundboard.Core.Models;
using NAudio.CoreAudioApi;

namespace GameSoundboard.Audio.Capture;

public sealed class WasapiDeviceCatalog : IAudioDeviceCatalog
{
    private readonly MMDeviceEnumerator _notificationEnumerator = new();
    private readonly MMDeviceNotificationClient _notifications;
    private bool _disposed;

    public WasapiDeviceCatalog()
    {
        // Callbacks only signal a change; the UI marshals and debounces the refresh.
        _notifications = _notificationEnumerator.CreateNotificationClient(useSynchronizationContext: false);
        _notifications.DeviceAdded += OnDevicesChanged;
        _notifications.DeviceRemoved += OnDevicesChanged;
        _notifications.DeviceStateChanged += OnDevicesChanged;
        _notifications.DefaultDeviceChanged += OnDevicesChanged;
    }

    public event EventHandler? DevicesChanged;

    public IReadOnlyList<AudioInputDevice> GetInputDevices()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        using var enumerator = new MMDeviceEnumerator();
        string? defaultId = null;
        try
        {
            using var defaultDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
            defaultId = defaultDevice.ID;
        }
        catch (COMException)
        {
            // A machine with no active microphone has no default capture endpoint.
        }

        var devices = new List<AudioInputDevice>();
        foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.All))
        {
            using (device)
            {
                var id = device.ID;
                string name;
                try
                {
                    name = device.FriendlyName;
                }
                catch (COMException)
                {
                    name = "未命名输入设备";
                }

                devices.Add(new AudioInputDevice(
                    id,
                    name,
                    MapState(device.State),
                    StringComparer.OrdinalIgnoreCase.Equals(id, defaultId)));
            }
        }

        return devices
            .OrderByDescending(device => device.IsDefault)
            .ThenBy(device => device.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    private static AudioDeviceAvailability MapState(DeviceState state) => state switch
    {
        DeviceState.Active => AudioDeviceAvailability.Active,
        DeviceState.Disabled => AudioDeviceAvailability.Disabled,
        DeviceState.Unplugged => AudioDeviceAvailability.Unplugged,
        _ => AudioDeviceAvailability.NotPresent
    };

    private void OnDevicesChanged(object? sender, EventArgs args) => DevicesChanged?.Invoke(this, EventArgs.Empty);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _notifications.DeviceAdded -= OnDevicesChanged;
        _notifications.DeviceRemoved -= OnDevicesChanged;
        _notifications.DeviceStateChanged -= OnDevicesChanged;
        _notifications.DefaultDeviceChanged -= OnDevicesChanged;
        _notifications.Dispose();
        _notificationEnumerator.Dispose();
    }
}
