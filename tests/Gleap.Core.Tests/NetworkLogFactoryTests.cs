using GleapSDK.Http;

namespace Gleap.Core.Tests;

public class NetworkLogFactoryTests
{
    [Fact]
    public void Build_CopiesCoreFields()
    {
        var log = NetworkLogFactory.Build(
            type: "GET", url: "https://api.example.com/x", date: "2026-07-13T10:00:00.000Z",
            durationMs: 12, statusCode: 200, statusText: "OK",
            requestPayload: "req", requestHeaders: new Dictionary<string, string> { ["A"] = "1" },
            responseBody: "resp");

        Assert.Equal("GET", log.Type);
        Assert.True(log.Success);
        Assert.Equal(200, log.Response.Status);
        Assert.Equal("resp", log.Response.ResponseText);
        Assert.Equal("req", log.Request.Payload);
    }

    [Fact]
    public void Build_MarksNon2xxAsFailure()
    {
        var log = NetworkLogFactory.Build("GET", "u", "d", 1, 500, "err", null, null, "boom");
        Assert.False(log.Success);
    }

    [Fact]
    public void Build_TruncatesOversizePayloadAndResponse()
    {
        var big = new string('x', 1_000_001);
        var log = NetworkLogFactory.Build("POST", "u", "d", 1, 200, "OK", big, null, big);
        Assert.Equal("<payload_too_large>", log.Request.Payload);
        Assert.Equal("<response_too_large>", log.Response.ResponseText);
    }
}
