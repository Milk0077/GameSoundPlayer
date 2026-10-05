using GameSoundboard.Audio.Buffers;

namespace GameSoundboard.Audio.Tests;

public sealed class SpscFloatRingBufferTests
{
    [Fact]
    public void WrapsCompleteStereoFramesAndSuppliesInitializedSilence()
    {
        var ring = new SpscFloatRingBuffer(3, 2);
        Assert.True(ring.TryWrite([1, 2, 3, 4]));
        var first = new float[2];
        Assert.Equal(2, ring.Read(first));
        Assert.Equal([1f, 2f], first);

        Assert.True(ring.TryWrite([5, 6, 7, 8]));
        var rest = new float[8];
        Assert.Equal(6, ring.Read(rest));
        Assert.Equal([3f, 4f, 5f, 6f, 7f, 8f, 0f, 0f], rest);
        Assert.Equal(1, ring.Underruns);
    }

    [Fact]
    public void FullRingDropsIncomingPacketAndKeepsExistingFrames()
    {
        var ring = new SpscFloatRingBuffer(2, 1);
        Assert.True(ring.TryWrite([0.25f, 0.5f]));
        Assert.False(ring.TryWrite([0.75f]));

        var output = new float[2];
        Assert.Equal(2, ring.Read(output));
        Assert.Equal([0.25f, 0.5f], output);
        Assert.Equal(1, ring.Overruns);
    }

    [Fact]
    public void ConsumerCanSkipStaleFramesAfterSchedulingPause()
    {
        var ring = new SpscFloatRingBuffer(10, 1);
        Assert.True(ring.TryWrite([1, 2, 3, 4, 5, 6, 7, 8]));
        Assert.Equal(5, ring.TrimToLatest(3));

        float[] output = new float[3];
        Assert.Equal(3, ring.Read(output));
        Assert.Equal([6f, 7f, 8f], output);
        Assert.Equal(1, ring.Overruns);
    }
}
