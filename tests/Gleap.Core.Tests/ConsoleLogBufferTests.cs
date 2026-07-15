using Gleap.Core.Tests.Fakes;
using GleapSDK;
using GleapSDK.Collection;

namespace Gleap.Core.Tests;

public class ConsoleLogBufferTests
{
    [Fact]
    public void Add_TruncatesOverlongEntries()
    {
        var buffer = new ConsoleLogBuffer(new FakeClock(), 100);

        buffer.Add(new string('x', 20_000), LogLevel.Error);

        // One dumped payload/stack trace must not dominate the report body (iOS caps at 10k + a marker).
        var log = buffer.Snapshot().Single().Log;
        Assert.Equal(10_000 + " [truncated]".Length, log.Length);
        Assert.EndsWith(" [truncated]", log);
    }

    [Fact]
    public void Add_LeavesNormalEntriesIntact()
    {
        var buffer = new ConsoleLogBuffer(new FakeClock(), 100);

        buffer.Add("just a log line", LogLevel.Info);

        Assert.Equal("just a log line", buffer.Snapshot().Single().Log);
    }

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
