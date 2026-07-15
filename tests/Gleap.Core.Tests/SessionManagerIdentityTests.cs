using Gleap.Core.Tests.Fakes;
using GleapSDK.Http;
using GleapSDK.Models;
using GleapSDK.Serialization;
using GleapSDK.Session;

namespace Gleap.Core.Tests;

public class SessionManagerIdentityTests
{
    [Fact]
    public async Task Identify_SkipsRedundantCall_ForUnchangedIdentity()
    {
        var http = new FakeHttpTransport();
        http.Responses.Enqueue(new HttpResult(200, "{\"gleapId\":\"g1\",\"gleapHash\":\"h1\"}"));
        http.Responses.Enqueue(new HttpResult(200, "{\"gleapId\":\"g2\",\"gleapHash\":\"h2\"}"));
        var api = new ApiClient(http, new SystemTextJsonSerializer(), GleapEndpoints.Default, "k");
        var session = new SessionManager(api, new InMemoryKeyValueStore());
        await session.StartAsync("en", "desktop", CancellationToken.None);

        await session.IdentifyAsync("u1", new GleapUserProperty { Name = "Bob" }, null, CancellationToken.None);
        var afterFirst = http.Calls.Count;
        // Apps call identifyContact on every screen; /sessions/identify is rate-limited server-side.
        await session.IdentifyAsync("u1", new GleapUserProperty { Name = "Bob" }, null, CancellationToken.None);

        Assert.Equal(afterFirst, http.Calls.Count);
    }

    [Fact]
    public async Task Identify_SendsAgain_WhenDataChanged()
    {
        var http = new FakeHttpTransport();
        http.Responses.Enqueue(new HttpResult(200, "{\"gleapId\":\"g1\",\"gleapHash\":\"h1\"}"));
        http.Responses.Enqueue(new HttpResult(200, "{\"gleapId\":\"g2\",\"gleapHash\":\"h2\"}"));
        http.Responses.Enqueue(new HttpResult(200, "{\"gleapId\":\"g3\",\"gleapHash\":\"h3\"}"));
        var api = new ApiClient(http, new SystemTextJsonSerializer(), GleapEndpoints.Default, "k");
        var session = new SessionManager(api, new InMemoryKeyValueStore());
        await session.StartAsync("en", "desktop", CancellationToken.None);

        await session.IdentifyAsync("u1", new GleapUserProperty { Name = "Bob" }, null, CancellationToken.None);
        var afterFirst = http.Calls.Count;
        await session.IdentifyAsync("u1", new GleapUserProperty { Name = "Bob", Plan = "pro" }, null, CancellationToken.None);

        Assert.Equal(afterFirst + 1, http.Calls.Count);
    }

    private static (SessionManager sm, InMemoryKeyValueStore store, FakeHttpTransport http) New()
    {
        var http = new FakeHttpTransport();
        var api = new ApiClient(http, new SystemTextJsonSerializer(), GleapEndpoints.Default, "key");
        var store = new InMemoryKeyValueStore();
        return (new SessionManager(api, store), store, http);
    }

    [Fact]
    public async Task Identify_UpdatesSessionAndCachesIdentity()
    {
        var (sm, store, http) = New();
        http.Responses.Enqueue(new HttpResult(200, "{\"gleapId\":\"g1\",\"gleapHash\":\"h1\"}"));
        await sm.StartAsync("en", "desktop", CancellationToken.None);
        http.Responses.Enqueue(new HttpResult(200, "{\"gleapId\":\"g2\",\"gleapHash\":\"h2\"}"));

        await sm.IdentifyAsync("u1", new GleapUserProperty { Email = "a@b.c" }, null, CancellationToken.None);

        Assert.Equal("g2", sm.GleapId);
        Assert.True(sm.IsIdentified);
        Assert.Equal("a@b.c", sm.Identity!.Email);
        Assert.Equal("u1", sm.Identity!.UserId);
        Assert.Equal("g2", store.Get("gleapId"));
    }

    [Fact]
    public async Task ClearIdentity_ResetsIdentityFlags()
    {
        var (sm, _, http) = New();
        http.Responses.Enqueue(new HttpResult(200, "{\"gleapId\":\"g1\",\"gleapHash\":\"h1\"}"));
        await sm.StartAsync("en", "desktop", CancellationToken.None);
        http.Responses.Enqueue(new HttpResult(200, "{\"gleapId\":\"g2\",\"gleapHash\":\"h2\"}"));
        await sm.IdentifyAsync("u1", new GleapUserProperty { Email = "a@b.c" }, null, CancellationToken.None);

        sm.ClearIdentity();

        Assert.False(sm.IsIdentified);
        Assert.Null(sm.Identity);
        Assert.Equal("", sm.GleapId);
    }
}
