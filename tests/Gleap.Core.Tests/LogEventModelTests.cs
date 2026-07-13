using GleapSDK.Models;
using GleapSDK.Serialization;
using Xunit;

namespace Gleap.Core.Tests;

public class LogEventModelTests
{
    private readonly IJsonSerializer _json = new SystemTextJsonSerializer();

    [Fact]
    public void GleapLog_SerializesToDateLogPriority()
    {
        var log = new GleapLog { Date = "2026-07-13T10:00:00.000Z", Log = "hi", Priority = "INFO" };
        var s = _json.Serialize(log);
        Assert.Contains("\"log\":\"hi\"", s);
        Assert.Contains("\"priority\":\"INFO\"", s);
        Assert.Contains("\"date\":\"2026-07-13T10:00:00.000Z\"", s);
    }

    [Fact]
    public void GleapEvent_HoldsNameDataDate()
    {
        var e = new GleapEvent { Name = "purchase", Data = null, Date = "2026-07-13T10:00:00.000Z" };
        Assert.Equal("purchase", e.Name);
        Assert.Equal("2026-07-13T10:00:00.000Z", e.Date);
    }
}
