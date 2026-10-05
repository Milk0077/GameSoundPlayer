using System.Diagnostics;
using System.Runtime.InteropServices;
using GameSoundboard.Core.Models;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace GameSoundboard.Audio.Capture;

public delegate void MicrophonePacketHandler(ReadOnlySpan<byte> data, WaveFormat format, long devicePosition);

public sealed class MicrophoneCapture : IMicrophoneCapture
{
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private WasapiRecorder? _recorder;
    private MMDevice? _device;
    private WaveFormat? _format;
    private string? _deviceId;
    private string? _lastError;
    private int _isCapturing;
    private int _peakMilliDb = -60_000;
    private int _sampleRate;
    private int _channels;
    private long _lastPacketTick;
    private long _capturedFrames;
    private bool _disposed;
    private int _requestedBufferMilliseconds = 10;

    public int RequestedBufferMilliseconds
    {
        get => Volatile.Read(ref _requestedBufferMilliseconds);
        set
        {
            if (value is not (5 or 10 or 20 or 40)) throw new ArgumentOutOfRangeException(nameof(value));
            Volatile.Write(ref _requestedBufferMilliseconds, value);
        }
    }

    // Raised on NAudio's capture thread. A subscriber must copy any data it needs to keep.
    public event MicrophonePacketHandler? PacketCaptured;

    public WaveFormat? CurrentFormat => Volatile.Read(ref _format);

    public string? DeviceId => Volatile.Read(ref _deviceId);
    public bool IsCapturing => Volatile.Read(ref _isCapturing) != 0;
    public int SampleRate => Volatile.Read(ref _sampleRate);
    public int Channels => Volatile.Read(ref _channels);
    public long CapturedFrames => Interlocked.Read(ref _capturedFrames);
    public string? LastError => Volatile.Read(ref _lastError);
    public double PeakDb
    {
        get
        {
            var tick = Volatile.Read(ref _lastPacketTick);
            return !IsCapturing || tick == 0 || Stopwatch.GetElapsedTime(tick) > TimeSpan.FromMilliseconds(250)
                ? -60
                : Volatile.Read(ref _peakMilliDb) / 1000.0;
        }
    }

    public async Task StartAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (IsCapturing && StringComparer.OrdinalIgnoreCase.Equals(DeviceId, deviceId)) return;
            await Task.Run(() => StartCore(deviceId), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_disposed) return;
            await Task.Run(StopCore, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    private void StartCore(string deviceId)
    {
        StopCore();
        using var enumerator = new MMDeviceEnumerator();
        MMDevice? device = null;
        WasapiRecorder? recorder = null;
        try
        {
            device = enumerator.GetDevice(deviceId);
            if (device.DataFlow != DataFlow.Capture || device.State != DeviceState.Active)
                throw new InvalidOperationException("所选设备不是可用的麦克风输入端点。");

            recorder = new WasapiRecorderBuilder()
                .WithDevice(device)
                .WithEventSync()
                .WithBufferLength(RequestedBufferMilliseconds)
                .Build();

            var format = recorder.WaveFormat;
            ValidateFormat(format);
            recorder.DataAvailable += OnDataAvailable;
            recorder.RecordingStopped += OnRecordingStopped;
            recorder.StartRecording();

            _device = device;
            _recorder = recorder;
            _format = format;
            Volatile.Write(ref _deviceId, deviceId);
            Volatile.Write(ref _sampleRate, format.SampleRate);
            Volatile.Write(ref _channels, format.Channels);
            Volatile.Write(ref _lastError, null);
            Volatile.Write(ref _peakMilliDb, -60_000);
            Volatile.Write(ref _lastPacketTick, 0);
            Volatile.Write(ref _isCapturing, 1);
        }
        catch
        {
            recorder?.Dispose();
            device?.Dispose();
            throw;
        }
    }

    private void StopCore()
    {
        Volatile.Write(ref _isCapturing, 0);
        Volatile.Write(ref _peakMilliDb, -60_000);
        Volatile.Write(ref _lastPacketTick, 0);
        if (_recorder is not null)
        {
            _recorder.DataAvailable -= OnDataAvailable;
            _recorder.RecordingStopped -= OnRecordingStopped;
            _recorder.Dispose();
            _recorder = null;
        }
        _device?.Dispose();
        _device = null;
        _format = null;
        Volatile.Write(ref _deviceId, null);
        Volatile.Write(ref _sampleRate, 0);
        Volatile.Write(ref _channels, 0);
    }

    private void OnDataAvailable(ReadOnlySpan<byte> data, AudioClientBufferFlags flags, long devicePosition, long qpcPosition)
    {
        var format = _format;
        if (format is null) return;

        float peak = (flags & AudioClientBufferFlags.Silent) != 0 ? 0 : CalculatePeak(data, format);
        var db = peak <= 0 ? -60.0 : Math.Clamp(20 * Math.Log10(peak), -60, 0);
        Volatile.Write(ref _peakMilliDb, (int)Math.Round(db * 1000));
        Volatile.Write(ref _lastPacketTick, Stopwatch.GetTimestamp());
        Interlocked.Add(ref _capturedFrames, data.Length / format.BlockAlign);
        PacketCaptured?.Invoke(data, format, devicePosition);
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs args)
    {
        Volatile.Write(ref _isCapturing, 0);
        Volatile.Write(ref _peakMilliDb, -60_000);
        if (args.Exception is not null)
            Volatile.Write(ref _lastError, args.Exception.Message);
    }

    private static void ValidateFormat(WaveFormat format)
    {
        bool isFloat = format.Encoding == WaveFormatEncoding.IeeeFloat ||
            format is WaveFormatExtensible extensible && extensible.SubFormat == AudioMediaSubtypes.MEDIASUBTYPE_IEEE_FLOAT;
        bool isPcm = format.Encoding == WaveFormatEncoding.Pcm ||
            format is WaveFormatExtensible pcmExtensible && pcmExtensible.SubFormat == AudioMediaSubtypes.MEDIASUBTYPE_PCM;
        if (!((isFloat && format.BitsPerSample == 32) ||
              (isPcm && format.BitsPerSample is 16 or 24 or 32)))
            throw new NotSupportedException($"不支持的麦克风采样格式：{format.Encoding}, {format.BitsPerSample} bit。");
    }

    private static float CalculatePeak(ReadOnlySpan<byte> data, WaveFormat format)
    {
        float peak = 0;
        bool isFloat = format.Encoding == WaveFormatEncoding.IeeeFloat ||
            format is WaveFormatExtensible extensible && extensible.SubFormat == AudioMediaSubtypes.MEDIASUBTYPE_IEEE_FLOAT;

        if (isFloat)
        {
            foreach (var sample in MemoryMarshal.Cast<byte, float>(data))
                if (float.IsFinite(sample)) peak = Math.Max(peak, Math.Abs(sample));
        }
        else if (format.BitsPerSample == 16)
        {
            foreach (var sample in MemoryMarshal.Cast<byte, short>(data))
                peak = Math.Max(peak, Math.Abs(sample / 32768f));
        }
        else if (format.BitsPerSample == 24)
        {
            for (int i = 0; i + 2 < data.Length; i += 3)
            {
                int value = (data[i] | data[i + 1] << 8 | data[i + 2] << 16) << 8 >> 8;
                peak = Math.Max(peak, Math.Abs(value / 8_388_608f));
            }
        }
        else
        {
            foreach (var sample in MemoryMarshal.Cast<byte, int>(data))
                peak = Math.Max(peak, Math.Abs(sample / 2_147_483_648f));
        }
        return peak;
    }

    public void Dispose()
    {
        _lifecycle.Wait();
        try
        {
            if (_disposed) return;
            _disposed = true;
            StopCore();
        }
        finally
        {
            _lifecycle.Release();
        }
    }
}
