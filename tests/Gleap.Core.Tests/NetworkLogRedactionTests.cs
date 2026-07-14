using GleapSDK.Collection;
using GleapSDK.Http;

namespace Gleap.Core.Tests;

public class NetworkLogRedactionTests
{
    [Fact]
    public void Add_StripsIgnoredHeaderProps_OnRealHandlerShapedLog()
    {
        var buffer = new NetworkLogBuffer(100);
        buffer.SetPropsToIgnore(new[] { "Authorization" });

        var log = NetworkLogFactory.Build(
            type: "GET", url: "https://api.example.com/x", date: "2026-07-13T10:00:00.000Z",
            durationMs: 12, statusCode: 200, statusText: "OK",
            requestPayload: null,
            requestHeaders: new Dictionary<string, string> { ["Authorization"] = "secret", ["Accept"] = "json" },
            responseBody: null);

        buffer.Add(log);

        var headers = (IReadOnlyDictionary<string, string>)buffer.Snapshot()[0].Request.Headers!;
        Assert.False(headers.ContainsKey("Authorization"));
        Assert.True(headers.ContainsKey("Accept"));
    }
}
