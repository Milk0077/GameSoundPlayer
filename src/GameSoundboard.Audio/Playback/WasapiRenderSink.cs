using System.Runtime.InteropServices;
using GameSoundboard.Core.Models;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace GameSoundboard.Audio.Playback;

public sealed record AudioRenderDevice(string Id, string Name);

/// <summary>Sends the finished mix to a chosen Windows playback endpoint, such as Voicemeeter Input.</summary>
public sealed class WasapiRenderSink : IAudioOutputSink
{
    private readonly object _gate = new();
    private readonly byte[] _scratch = new byte[1920 * 2 * sizeof(float)];
    private MMDevice? _device;
    private WasapiPlayer? _player;
    private BufferedWaveProvider? _buffer;
    private string? _deviceId;
    private string? _lastError;
    private long _droppedBlocks;
    private bool _disposed;

    public string? DeviceId => Volatile.Read(ref _deviceId);
    public string? LastError => Volatile.Read(ref _lastError);
    public long DroppedBlocks => Interlocked.Read(ref _droppedBlocks);
    public bool IsConnected => Volatile.Read(ref _player)?.PlaybackState == PlaybackState.Playing;

    public static IReadOnlyList<AudioRenderDevice> GetDevices()
    {
        using var enumerator = new MMDeviceEnumerator();
        var result = new List<AudioRenderDevice>();
        foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            using (device) result.Add(new AudioRenderDevice(device.ID, device.FriendlyName));
        }
        return result.OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    public Task SelectDeviceAsync(string? deviceId) => Task.Run(() => SelectDevice(deviceId));

    private void SelectDevice(string? deviceId)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (StringComparer.OrdinalIgnoreCase.Equals(deviceId, _deviceId) && IsConnected) return;
            ClosePlayer();
            Volatile.Write(ref _lastError, null);
            if (deviceId is null) return;

            MMDevice? device = null;
            try
            {
                using var enumerator = new MMDeviceEnumerator();
                device = enumerator.GetDevice(deviceId);
                if (device.State != DeviceState.Active || device.DataFlow != DataFlow.Render)
                    throw new InvalidOperationException("所选播放设备不可用。");
                var buffer = new BufferedWaveProvider(WaveFormat.CreateIeeeFloatWaveFormat(48000, 2),
                    TimeSpan.FromMilliseconds(250))
                {
                    DiscardOnBufferOverflow = false,
                    ReadFully = true
                };
                var player = new WasapiPlayerBuilder().WithDevice(device).WithLatency(40).Build();
                try
                {
                    player.Init(buffer);
                    player.Play();
                }
                catch
                {
                    player.Dispose();
                    throw;
                }
                _device = device;
                _buffer = buffer;
                Volatile.Write(ref _player, player);
                Volatile.Write(ref _deviceId, deviceId);
            }
            catch (Exception exception)
            {
                device?.Dispose();
                Volatile.Write(ref _lastError, exception.Message);
                throw;
            }
        }
    }

    public bool TryWrite(ReadOnlySpan<float> samples)
    {
        if (samples.IsEmpty || (samples.Length & 1) != 0 || samples.Length * sizeof(float) > _scratch.Length)
            return false;
        if (!Monitor.TryEnter(_gate))
        {
            Interlocked.Increment(ref _droppedBlocks);
            return false;
        }
        try
        {
            if (_buffer is null || _player?.PlaybackState != PlaybackState.Playing) return false;
            var bytes = samples.Length * sizeof(float);
            if (_buffer.BufferedBytes > 48000 * 2 * sizeof(float) / 10)
                _buffer.ClearBuffer(); // Shed stale audio if the device clock falls behind.
            if (_buffer.BufferedBytes + bytes > _buffer.BufferLength)
            {
                Interlocked.Increment(ref _droppedBlocks);
                return false;
            }
            MemoryMarshal.AsBytes(samples).CopyTo(_scratch);
            _buffer.AddSamples(_scratch, 0, bytes);
            return true;
        }
        finally { Monitor.Exit(_gate); }
    }

    private void ClosePlayer()
    {
        Volatile.Write(ref _deviceId, null);
        var player = Interlocked.Exchange(ref _player, null);
        _buffer = null;
        player?.Dispose();
        _device?.Dispose();
        _device = null;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            ClosePlayer();
        }
    }
}
