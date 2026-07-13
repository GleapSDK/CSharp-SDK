using Gleap.Core.Tests.Fakes;
using GleapSDK.Http;
using GleapSDK.Serialization;

namespace Gleap.Core.Tests;

public class ApiClientTests
{
    private static ApiClient NewClient(FakeHttpTransport t) =>
        new ApiClient(t, new SystemTextJsonSerializer(), GleapEndpoints.Default, "sdk-key-123");

    [Fact]
    public async Task CreateSession_PostsToSessions_WithApiTokenHeader()
    {
        var t = new FakeHttpTransport();
        t.Responses.Enqueue(new HttpResult(200, "{\"gleapId\":\"g1\",\"gleapHash\":\"h1\"}"));
        var client = NewClient(t);

        var res = await client.CreateSessionAsync("en", "desktop", null, null, CancellationToken.None);

        var call = t.Calls[0];
        Assert.Equal("POST", call.Method);
        Assert.Equal("https://api.gleap.io/sessions", call.Url);
        Assert.Equal("sdk-key-123", call.Headers["Api-Token"]);
        Assert.Equal("g1", res.GleapId);
        Assert.Equal("h1", res.GleapHash);
    }

    [Fact]
    public async Task CreateSession_SendsGuestIdsWhenPresent()
    {
        var t = new FakeHttpTransport();
        t.Responses.Enqueue(new HttpResult(200, "{\"gleapId\":\"g2\",\"gleapHash\":\"h2\"}"));
        var client = NewClient(t);

        await client.CreateSessionAsync("en", "desktop", "guestId", "guestHash", CancellationToken.None);

        var call = t.Calls[0];
        Assert.Equal("guestId", call.Headers["Gleap-Id"]);
        Assert.Equal("guestHash", call.Headers["Gleap-Hash"]);
    }

    [Fact]
    public async Task LoadConfig_GetsConfigEndpoint_WithLang()
    {
        var t = new FakeHttpTransport();
        t.Responses.Enqueue(new HttpResult(200, "{\"flowConfig\":{\"a\":1},\"projectActions\":{\"b\":2}}"));
        var client = NewClient(t);

        var raw = await client.LoadConfigAsync("de", CancellationToken.None);

        var call = t.Calls[0];
        Assert.Equal("GET", call.Method);
        Assert.Equal("https://api.gleap.io/config/sdk-key-123?lang=de", call.Url);
        Assert.Contains("flowConfig", raw);
    }
}
