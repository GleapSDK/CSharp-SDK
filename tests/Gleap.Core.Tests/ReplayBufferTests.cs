using GleapSDK.Capture;

namespace Gleap.Core.Tests;

public class ReplayBufferTests
{
    [Fact]
    public void AddFrame_KeepsRingBounded()
    {
        var b = new ReplayBuffer(intervalMs: 1000, capacity: 3);
        for (var i = 0; i < 4; i++)
        {
            b.AddFrame("f" + i);
        }

        Assert.Equal(new[] { "f1", "f2", "f3" }, b.Snapshot().ToArray());
        Assert.Equal(1000, b.IntervalMs);
    }

    [Fact]
    public void BuildReplay_ReturnsIntervalAndFrames()
    {
        var b = new ReplayBuffer(intervalMs: 1000, capacity: 3);
        b.AddFrame("a");
        b.AddFrame("b");

        var replay = b.BuildReplay();

        Assert.Equal(1000, replay["interval"]);
        var frames = Assert.IsAssignableFrom<IReadOnlyList<string>>(replay["frames"]);
        Assert.Equal(2, frames.Count);
    }
}
