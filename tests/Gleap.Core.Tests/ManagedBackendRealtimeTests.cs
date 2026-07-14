using System.Text.Json;
using Gleap.Core.Tests.Fakes;
using GleapSDK;
using GleapSDK.Http;
using GleapSDK.Serialization;
using GleapSDK.Session;

namespace Gleap.Core.Tests;

public class ManagedBackendRealtimeTests
{
    private static async Task<(ManagedBackend backend, FakeWebViewChannel ch, FakeHttpTransport http, FakeRealtimeChannel rt)>
        NewInitializedAsync()
    {
        var http = new FakeHttpTransport();
        http.Responses.Enqueue(new HttpResult(200, "{\"gleapId\":\"g1\",\"gleapHash\":\"h1\"}"));
        http.Responses.Enqueue(new HttpResult(200, "{\"flowConfig\":{},\"projectActions\":{}}"));
        var ch = new FakeWebViewChannel();
        var rt = new FakeRealtimeChannel();
        var backend = new ManagedBackend(new ManagedBackend.Dependencies
        {
            Http = http,
            Json = new SystemTextJsonSerializer(),
            Store = new InMemoryKeyValueStore(),
            Channel = ch,
            Endpoints = GleapEndpoints.Default,
            Realtime = rt,
            SdkVersion = "9.9.9"
        });
        await backend.InitializeAsync("token-1", CancellationToken.None);
        return (backend, ch, http, rt);
    }

    [Fact]
    public async Task Realtime_ConnectsWithSessionIdentity_OnInit()
    {
        var (_, _, _, rt) = await NewInitializedAsync();

        Assert.Single(rt.ConnectUrls);
        var url = rt.ConnectUrls[0];
        Assert.StartsWith("wss://ws.gleap.io?", url);
        Assert.Contains("gleapId=g1", url);
        Assert.Contains("gleapHash=h1", url);
        Assert.Contains("apiKey=token-1", url);
        Assert.Contains("sdkVersion=9.9.9", url);
    }

    [Fact]
    public async Task Realtime_UpdateFrame_EmitsNotificationCount()
    {
        var (backend, _, _, rt) = await NewInitializedAsync();
        object? received = null;
        backend.RegisterListener("notificationCountUpdated", d => received = d);

        rt.Emit("{\"name\":\"update\",\"data\":{\"a\":[],\"u\":7}}");

        Assert.Equal(7, received);
    }

    [Fact]
    public async Task Realtime_UpdateFrame_EmitsOutboundSent()
    {
        var (backend, _, _, rt) = await NewInitializedAsync();
        object? received = null;
        backend.RegisterListener("outboundSent", d => received = d);

        rt.Emit("{\"name\":\"update\",\"data\":{\"a\":[{\"actionType\":\"notification\",\"outbound\":\"ob9\"}],\"u\":1}}");

        var json = JsonSerializer.Serialize(received);
        Assert.Contains("notification", json);
        Assert.Contains("ob9", json);
    }

    [Fact]
    public async Task Realtime_NonUpdateFrame_IsIgnored()
    {
        var (backend, _, _, rt) = await NewInitializedAsync();
        var fired = false;
        backend.RegisterListener("notificationCountUpdated", _ => fired = true);

        rt.Emit("{\"name\":\"checklist\",\"data\":{\"id\":\"cl1\"}}");
        rt.Emit("not even json");

        Assert.False(fired);
    }

    [Fact]
    public async Task Realtime_ReconnectsOnClearIdentity()
    {
        var (backend, _, http, rt) = await NewInitializedAsync();
        http.Responses.Enqueue(new HttpResult(200, "{\"gleapId\":\"g2\",\"gleapHash\":\"h2\"}")); // new guest session

        await backend.ClearIdentityAsync(CancellationToken.None);

        Assert.Equal(2, rt.ConnectUrls.Count);
        Assert.Contains("gleapId=g2", rt.ConnectUrls[^1]);
    }

    [Fact]
    public async Task Ping_SetsWsFlagTrue_WhenRealtimeConnected()
    {
        var (backend, ch, http, rt) = await NewInitializedAsync();
        ch.SimulateIncoming("{\"name\":\"ping\"}");
        rt.IsConnected = true;
        http.Responses.Enqueue(new HttpResult(200, "{}"));

        await backend.PollOutboundOnceAsync(CancellationToken.None);

        Assert.Contains("\"ws\":true", http.Calls[^1].Body);
    }
}
