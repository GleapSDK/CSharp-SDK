using System.Text.Json;
using Gleap.Core.Tests.Fakes;
using GleapSDK;
using GleapSDK.Http;
using GleapSDK.Serialization;
using GleapSDK.Session;

namespace Gleap.Core.Tests;

public class ManagedBackendOutboundTests
{
    private static async Task<(ManagedBackend backend, FakeWebViewChannel ch, FakeHttpTransport http)> NewInitializedAsync()
    {
        var http = new FakeHttpTransport();
        http.Responses.Enqueue(new HttpResult(200, "{\"gleapId\":\"g1\",\"gleapHash\":\"h1\"}"));
        http.Responses.Enqueue(new HttpResult(200, "{\"flowConfig\":{},\"projectActions\":{}}"));
        var ch = new FakeWebViewChannel();
        var backend = new ManagedBackend(new ManagedBackend.Dependencies
        {
            Http = http,
            Json = new SystemTextJsonSerializer(),
            Store = new InMemoryKeyValueStore(),
            Channel = ch,
            Endpoints = GleapEndpoints.Default
        });
        await backend.InitializeAsync("token-1", CancellationToken.None);
        return (backend, ch, http);
    }

    [Fact]
    public async Task Poll_EmitsNotificationCount()
    {
        var (backend, ch, http) = await NewInitializedAsync();
        ch.SimulateIncoming("{\"name\":\"ping\"}");
        object? received = null;
        backend.RegisterListener("notificationCountUpdated", data => received = data);
        http.Responses.Enqueue(new HttpResult(200, "{\"a\":[],\"u\":5}"));

        await backend.PollOutboundOnceAsync(CancellationToken.None);

        Assert.Equal(5, received);
    }

    [Fact]
    public async Task Poll_EmitsOutboundSent_PerAction()
    {
        var (backend, ch, http) = await NewInitializedAsync();
        ch.SimulateIncoming("{\"name\":\"ping\"}");
        var fireCount = 0;
        object? received = null;
        backend.RegisterListener("outboundSent", data =>
        {
            fireCount++;
            received = data;
        });
        http.Responses.Enqueue(new HttpResult(200, "{\"a\":[{\"actionType\":\"survey\",\"outbound\":\"ob1\"}],\"u\":0}"));

        await backend.PollOutboundOnceAsync(CancellationToken.None);

        Assert.Equal(1, fireCount);
        Assert.Contains("ob1", JsonSerializer.Serialize(received));
    }

    [Fact]
    public async Task Poll_OutboundSent_IncludesRawActionData()
    {
        var (backend, ch, http) = await NewInitializedAsync();
        ch.SimulateIncoming("{\"name\":\"ping\"}");
        object? received = null;
        backend.RegisterListener("outboundSent", data => received = data);
        http.Responses.Enqueue(new HttpResult(200,
            "{\"a\":[{\"actionType\":\"banner\",\"outbound\":\"ob1\",\"bannerColor\":\"#123456\"}],\"u\":0}"));

        await backend.PollOutboundOnceAsync(CancellationToken.None);

        var json = JsonSerializer.Serialize(received);
        Assert.Contains("bannerColor", json);
        Assert.Contains("#123456", json);
    }

    [Fact]
    public async Task Poll_AutoStartsSurvey()
    {
        var (backend, ch, http) = await NewInitializedAsync();
        ch.SimulateIncoming("{\"name\":\"ping\"}");
        http.Responses.Enqueue(new HttpResult(200,
            "{\"a\":[{\"actionType\":\"survey\",\"outbound\":\"ob1\",\"flow\":\"s1\"}],\"u\":0}"));

        await backend.PollOutboundOnceAsync(CancellationToken.None);

        Assert.Contains(ch.ExecutedScripts, s => s.Contains("start-survey"));
    }

    [Fact]
    public async Task Poll_FlushesEvents_NotResentNextCycle()
    {
        var (backend, ch, http) = await NewInitializedAsync();
        ch.SimulateIncoming("{\"name\":\"ping\"}");
        backend.TrackEvent("e1", null);
        http.Responses.Enqueue(new HttpResult(200, "{}"));
        http.Responses.Enqueue(new HttpResult(200, "{}"));

        await backend.PollOutboundOnceAsync(CancellationToken.None);
        var firstPingBody = http.Calls[^1].Body;

        await backend.PollOutboundOnceAsync(CancellationToken.None);
        var secondPingBody = http.Calls[^1].Body;

        Assert.Contains("e1", firstPingBody);
        Assert.DoesNotContain("e1", secondPingBody);
    }
}
