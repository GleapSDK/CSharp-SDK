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

    private static GleapSDK.Models.GleapNetworkLog Log(string? payload, string? response, string url = "https://api.example.com/x") =>
        NetworkLogFactory.Build(
            type: "POST", url: url, date: "2026-07-13T10:00:00.000Z",
            durationMs: 12, statusCode: 200, statusText: "OK",
            requestPayload: payload, requestHeaders: null, responseBody: response);

    // propsToIgnore must cover the request body and the response body too — redacting only headers gives
    // a false sense of safety while a password in a POST body still reaches Gleap.

    [Fact]
    public void Add_StripsIgnoredProps_FromRequestPayload()
    {
        var buffer = new NetworkLogBuffer(100);
        buffer.SetPropsToIgnore(new[] { "password" });

        buffer.Add(Log("{\"user\":\"bob\",\"password\":\"hunter2\"}", null));

        var payload = (string)buffer.Snapshot()[0].Request.Payload!;
        Assert.DoesNotContain("hunter2", payload);
        Assert.Contains("bob", payload);
    }

    [Fact]
    public void Add_StripsIgnoredProps_FromResponseBody()
    {
        var buffer = new NetworkLogBuffer(100);
        buffer.SetPropsToIgnore(new[] { "token" });

        buffer.Add(Log(null, "{\"ok\":true,\"token\":\"abc123\"}"));

        var body = buffer.Snapshot()[0].Response.ResponseText;
        Assert.DoesNotContain("abc123", body);
        Assert.Contains("ok", body);
    }

    [Fact]
    public void Add_StripsIgnoredProps_Nested()
    {
        var buffer = new NetworkLogBuffer(100);
        buffer.SetPropsToIgnore(new[] { "password" });

        buffer.Add(Log("{\"users\":[{\"name\":\"bob\",\"password\":\"hunter2\"}]}", null));

        Assert.DoesNotContain("hunter2", (string)buffer.Snapshot()[0].Request.Payload!);
    }

    [Fact]
    public void Add_LeavesNonJsonBodiesUntouched()
    {
        var buffer = new NetworkLogBuffer(100);
        buffer.SetPropsToIgnore(new[] { "password" });

        buffer.Add(Log("user=bob&password=hunter2", "plain text"));

        // Not JSON: nothing to strip structurally, so the body is passed through as-is.
        Assert.Equal("user=bob&password=hunter2", (string)buffer.Snapshot()[0].Request.Payload!);
        Assert.Equal("plain text", buffer.Snapshot()[0].Response.ResponseText);
    }

    [Fact]
    public void Add_NeverLogsGleapsOwnTraffic()
    {
        var buffer = new NetworkLogBuffer(100);

        buffer.Add(Log(null, null, url: "https://api.gleap.io/sessions/ping"));

        // The SDK's own calls carry the project token + session headers; they must never land in a ticket.
        Assert.Empty(buffer.Snapshot());
    }
}
