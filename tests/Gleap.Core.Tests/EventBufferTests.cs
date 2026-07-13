using Gleap.Core.Tests.Fakes;
using GleapSDK.Collection;

namespace Gleap.Core.Tests;

public class EventBufferTests
{
    [Fact]
    public void Add_StampsDateAndKeepsNameData()
    {
        var buffer = new EventBuffer(new FakeClock(), 100);
        buffer.Add("purchase", new Dictionary<string, object> { ["amount"] = 5 });

        var e = buffer.Snapshot().Single();
        Assert.Equal("purchase", e.Name);
        Assert.Equal("2026-07-13T10:00:00.000Z", e.Date);
        Assert.NotNull(e.Data);
    }

    [Fact]
    public void Add_AllowsNullData()
    {
        var buffer = new EventBuffer(new FakeClock(), 100);
        buffer.Add("opened", null);
        Assert.Null(buffer.Snapshot().Single().Data);
    }
}
