using Gleap.Core.Tests.Fakes;
using GleapSDK.Http;
using GleapSDK.Serialization;
using GleapSDK.Session;

namespace Gleap.Core.Tests;

public class SessionManagerTests
{
    private static (SessionManager sm, InMemoryKeyValueStore store, FakeHttpTransport http) New()
    {
        var http = new FakeHttpTransport();
        var api = new ApiClient(http, new SystemTextJsonSerializer(), GleapEndpoints.Default, "key");
        var store = new InMemoryKeyValueStore();
        return (new SessionManager(api, store), store, http);
    }

    [Fact]
    public async Task Start_PersistsGleapIdAndHash()
    {
        var (sm, store, http) = New();
        http.Responses.Enqueue(new HttpResult(200, "{\"gleapId\":\"g1\",\"gleapHash\":\"h1\"}"));

        await sm.StartAsync("en", "desktop", CancellationToken.None);

        Assert.Equal("g1", sm.GleapId);
        Assert.Equal("h1", sm.GleapHash);
        Assert.Equal("g1", store.Get("gleapId"));
        Assert.Equal("h1", store.Get("gleapHash"));
    }

    [Fact]
    public async Task Start_ReusesStoredGuestIds()
    {
        var (sm, store, http) = New();
        store.Set("gleapId", "guest");
        store.Set("gleapHash", "guestHash");
        http.Responses.Enqueue(new HttpResult(200, "{\"gleapId\":\"g1\",\"gleapHash\":\"h1\"}"));

        await sm.StartAsync("en", "desktop", CancellationToken.None);

        Assert.Equal("guest", http.Calls[0].Headers["Gleap-Id"]);
    }

    [Fact]
    public async Task ClearIdentity_WipesStoredIds()
    {
        var (sm, store, http) = New();
        http.Responses.Enqueue(new HttpResult(200, "{\"gleapId\":\"g1\",\"gleapHash\":\"h1\"}"));
        await sm.StartAsync("en", "desktop", CancellationToken.None);

        sm.ClearIdentity();

        Assert.Null(store.Get("gleapId"));
        Assert.Equal("", sm.GleapId);
    }
}
