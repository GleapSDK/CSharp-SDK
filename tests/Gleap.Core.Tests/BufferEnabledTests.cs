using Gleap.Core.Tests.Fakes;
using GleapSDK;
using GleapSDK.Collection;
using GleapSDK.Models;

namespace Gleap.Core.Tests;

public class BufferEnabledTests
{
    [Fact]
    public void ConsoleLog_WhenDisabled_DoesNotRecord()
    {
        var b = new ConsoleLogBuffer(new FakeClock(), 100);
        b.Enabled = false;

        b.Add("x", LogLevel.Info);
        Assert.Empty(b.Snapshot());

        b.Enabled = true;
        b.Add("x", LogLevel.Info);
        Assert.Single(b.Snapshot());
    }

    [Fact]
    public void NetworkLog_WhenDisabled_DoesNotRecord()
    {
        var b = new NetworkLogBuffer(100);
        b.Enabled = false;

        b.Add(new GleapNetworkLog { Url = "u" });
        Assert.Empty(b.Snapshot());

        b.Enabled = true;
        b.Add(new GleapNetworkLog { Url = "u" });
        Assert.Single(b.Snapshot());
    }
}
