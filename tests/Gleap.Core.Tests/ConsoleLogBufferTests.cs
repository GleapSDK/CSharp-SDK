using Gleap.Core.Tests.Fakes;
using GleapSDK;
using GleapSDK.Collection;

namespace Gleap.Core.Tests;

public class ConsoleLogBufferTests
{
    [Fact]
    public void Add_StampsDateAndMapsLevel()
    {
        var clock = new FakeClock();
        var buffer = new ConsoleLogBuffer(clock, capacity: 100);

        buffer.Add("boom", LogLevel.Error);

        var entry = buffer.Snapshot().Single();
        Assert.Equal("boom", entry.Log);
        Assert.Equal("ERROR", entry.Priority);
        Assert.Equal("2026-07-13T10:00:00.000Z", entry.Date);
    }

    [Fact]
    public void Add_DefaultsToInfoPriority()
    {
        var buffer = new ConsoleLogBuffer(new FakeClock(), 100);
        buffer.Add("hello", LogLevel.Info);
        Assert.Equal("INFO", buffer.Snapshot().Single().Priority);
    }
}
