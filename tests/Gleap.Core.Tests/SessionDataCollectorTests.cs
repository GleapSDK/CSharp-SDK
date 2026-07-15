using Gleap.Core.Tests.Fakes;
using GleapSDK;
using GleapSDK.Collection;
using GleapSDK.Data;
using GleapSDK.Metadata;
using GleapSDK.Serialization;

namespace Gleap.Core.Tests;

public class SessionDataCollectorTests
{
    private static SessionDataCollector NewCollector(out ConsoleLogBuffer logs, out CustomDataStore custom)
    {
        var clock = new FakeClock();
        logs = new ConsoleLogBuffer(clock, 100);
        custom = new CustomDataStore();
        return new SessionDataCollector(
            logs,
            new EventBuffer(clock, 100),
            new NetworkLogBuffer(100),
            custom,
            new TicketAttributeStore(),
            new TagStore(),
            new DefaultMetadataProvider("NET/Test", "0.1.0"));
    }

    private static SessionDataCollector NewCollectorWithScreen(Func<string?> lastScreenName)
    {
        var clock = new FakeClock();
        return new SessionDataCollector(
            new ConsoleLogBuffer(clock, 100),
            new EventBuffer(clock, 100),
            new NetworkLogBuffer(100),
            new CustomDataStore(),
            new TicketAttributeStore(),
            new TagStore(),
            new DefaultMetadataProvider("NET/Test", "0.1.0"),
            lastScreenName);
    }

    [Fact]
    public void BuildTicketData_MetaData_CarriesLastScreenName()
    {
        var collector = NewCollectorWithScreen(() => "CheckoutWindow");

        var json = new SystemTextJsonSerializer().Serialize(collector.BuildTicketData());

        Assert.Contains("\"lastScreenName\":\"CheckoutWindow\"", json);
    }

    [Fact]
    public void BuildTicketData_MetaData_LastScreenNameEmpty_WhenNoScreenTracked()
    {
        var collector = NewCollectorWithScreen(() => null);

        var json = new SystemTextJsonSerializer().Serialize(collector.BuildTicketData());

        // iOS always reports the key, empty until a screen has been seen.
        Assert.Contains("\"lastScreenName\":\"\"", json);
    }

    [Fact]
    public void BuildTicketData_HasAllRequiredKeys()
    {
        var collector = NewCollector(out _, out _);
        var data = collector.BuildTicketData();

        foreach (var key in new[] { "customData", "formData", "metaData", "consoleLog", "networkLogs", "customEventLog", "tags" })
        {
            Assert.True(data.ContainsKey(key), $"missing key {key}");
        }
    }

    [Fact]
    public void BuildTicketData_ReflectsBufferedData()
    {
        var collector = NewCollector(out var logs, out var custom);
        logs.Add("hello", LogLevel.Info);
        custom.Set("plan", "pro");

        var json = new SystemTextJsonSerializer().Serialize(collector.BuildTicketData());

        Assert.Contains("hello", json);
        Assert.Contains("\"plan\":\"pro\"", json);
        Assert.Contains("\"sdkType\":\"NET/Test\"", json);
    }
}
