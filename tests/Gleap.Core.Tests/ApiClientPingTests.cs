using Gleap.Core.Tests.Fakes;
using GleapSDK.Http;
using GleapSDK.Serialization;

namespace Gleap.Core.Tests;

public class ApiClientPingTests
{
    private static ApiClient NewClient(FakeHttpTransport t) =>
        new ApiClient(t, new SystemTextJsonSerializer(), GleapEndpoints.Default, "sdk-key-123");

    [Fact]
    public async Task Ping_PostsToPing_WithHeaders()
    {
        var t = new FakeHttpTransport();
        t.Responses.Enqueue(new HttpResult(200, "{\"a\":[{\"actionType\":\"survey\",\"outbound\":\"ob1\"}],\"u\":2}"));
        var client = NewClient(t);

        var r = await client.PingAsync(123, Array.Empty<object?>(), true, "g1", "h1", CancellationToken.None);

        var call = t.Calls[0];
        Assert.Equal("POST", call.Method);
        Assert.Equal("https://api.gleap.io/sessions/ping", call.Url);
        Assert.Equal("g1", call.Headers["Gleap-Id"]);
        Assert.Contains("\"opened\":true", call.Body);
        Assert.Equal(2, r.UnreadCount);
        Assert.Equal("survey", r.Actions[0].ActionType);
        Assert.Equal("ob1", r.Actions[0].OutboundId);
    }

    [Fact]
    public async Task Ping_EmptyResponse_YieldsNoActions()
    {
        var t = new FakeHttpTransport();
        t.Responses.Enqueue(new HttpResult(200, "{}"));
        var client = NewClient(t);

        var r = await client.PingAsync(123, Array.Empty<object?>(), true, "g1", "h1", CancellationToken.None);

        Assert.Empty(r.Actions);
        Assert.Equal(0, r.UnreadCount);
    }
}
