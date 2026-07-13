using GleapSDK.Collection;

namespace Gleap.Core.Tests;

public class RingBufferTests
{
    [Fact]
    public void DropsOldest_WhenOverCapacity()
    {
        var buffer = new RingBuffer<int>(3);
        buffer.Add(1);
        buffer.Add(2);
        buffer.Add(3);
        buffer.Add(4);

        Assert.Equal(new[] { 2, 3, 4 }, buffer.Snapshot().ToArray());
        Assert.Equal(3, buffer.Count);
    }

    [Fact]
    public void Clear_EmptiesBuffer()
    {
        var buffer = new RingBuffer<int>(3);
        buffer.Add(1);
        buffer.Clear();
        Assert.Empty(buffer.Snapshot());
    }
}
