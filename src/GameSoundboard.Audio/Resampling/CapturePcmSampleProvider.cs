using System.Buffers.Binary;
using GameSoundboard.Audio.Buffers;
using GameSoundboard.Audio.Capture;
using NAudio.Wave;

namespace GameSoundboard.Audio.Resampling;

/// <summary>Converts WASAPI capture packets to float samples and buffers them for the mixer thread.</summary>
public sealed class CapturePcmSampleProvider : ISampleProvider, IDisposable
{
    private readonly MicrophoneCapture _capture;
    private readonly SpscFloatRingBuffer _ring;
    private readonly WaveFormat _inputFormat;
    private readonly float[] _packetScratch;
    private bool _disposed;

    public CapturePcmSampleProvider(MicrophoneCapture capture, WaveFormat inputFormat)
    {
        _capture = capture;
        _inputFormat = inputFormat;
        WaveFormat = NAudio.Wave.WaveFormat.CreateIeeeFloatWaveFormat(inputFormat.SampleRate, inputFormat.Channels);
        // A larger queue absorbs short scheduler pauses; the consumer trims stale data below.
        _ring = new SpscFloatRingBuffer(inputFormat.SampleRate, inputFormat.Channels);
        _packetScratch = new float[Math.Max(inputFormat.SampleRate / 20, 480) * inputFormat.Channels];
        _capture.PacketCaptured += OnPacketCaptured;
    }

    public WaveFormat WaveFormat { get; }
    public long Overruns => _ring.Overruns;
    public long Underruns => _ring.Underruns;
    public int BufferedFrames => _ring.BufferedFrames;

    private void OnPacketCaptured(ReadOnlySpan<byte> data, WaveFormat format, long devicePosition)
    {
        if (_disposed || format.SampleRate != _inputFormat.SampleRate || format.Channels != _inputFormat.Channels) return;
        var frames = data.Length / format.BlockAlign;
        if (frames == 0) return;
        var bytesPerSample = format.BitsPerSample / 8;
        bool isFloat = format.Encoding == WaveFormatEncoding.IeeeFloat ||
            format is WaveFormatExtensible extensible && extensible.SubFormat == AudioMediaSubtypes.MEDIASUBTYPE_IEEE_FLOAT;
        var capacityFrames = _packetScratch.Length / format.Channels;
        for (var frameOffset = 0; frameOffset < frames;)
        {
            var chunkFrames = Math.Min(capacityFrames, frames - frameOffset);
            var samples = _packetScratch.AsSpan(0, chunkFrames * format.Channels);
            var chunkBytes = data.Slice(frameOffset * format.BlockAlign, chunkFrames * format.BlockAlign);
            for (var sample = 0; sample < samples.Length; sample++)
            {
                var bytes = chunkBytes.Slice(sample * bytesPerSample, bytesPerSample);
                samples[sample] = isFloat ? BitConverter.ToSingle(bytes) : format.BitsPerSample switch
                {
                    16 => BinaryPrimitives.ReadInt16LittleEndian(bytes) / 32768f,
                    24 => ((bytes[0] | bytes[1] << 8 | bytes[2] << 16) << 8 >> 8) / 8_388_608f,
                    32 => BinaryPrimitives.ReadInt32LittleEndian(bytes) / 2_147_483_648f,
                    _ => 0
                };
            }
            _ring.TryWrite(samples);
            frameOffset += chunkFrames;
        }
    }

    public int Read(Span<float> buffer)
    {
        if (_ring.BufferedFrames > _inputFormat.SampleRate / 10)
            _ring.TrimToLatest(_inputFormat.SampleRate / 20);
        _ring.Read(buffer);
        return buffer.Length;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _capture.PacketCaptured -= OnPacketCaptured;
    }
}
