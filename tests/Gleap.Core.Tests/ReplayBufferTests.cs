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

        Assert.Equal(new[] { "f1", "f2", "f3" }, b.Snapshot().Select(f => f.Base64).ToArray());
        Assert.Equal(1000, b.IntervalMs);
    }

    [Fact]
    public void AddFrame_KeepsScreenNameAndDate()
    {
        var b = new ReplayBuffer(intervalMs: 1000, capacity: 3);

        b.AddFrame("a", "CheckoutWindow", "2026-07-13T10:00:00.000Z");

        // The report's replay frames are objects, so each frame has to carry its own context.
        var frame = b.Snapshot().Single();
        Assert.Equal("a", frame.Base64);
        Assert.Equal("CheckoutWindow", frame.ScreenName);
        Assert.Equal("2026-07-13T10:00:00.000Z", frame.Date);
    }
}
