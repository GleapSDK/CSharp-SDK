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

    // The server has no "survey" actionType: for a survey outbound it puts the flow's own action id in
    // actionType, with `format` alongside it. These tests use that real shape.

    [Fact]
    public async Task Poll_AutoStartsSurvey_UsingActionTypeAsFlowId()
    {
        var (backend, ch, http) = await NewInitializedAsync();
        ch.SimulateIncoming("{\"name\":\"ping\"}");
        http.Responses.Enqueue(new HttpResult(200,
            "{\"a\":[{\"actionType\":\"6813abc\",\"outbound\":\"ob1\",\"format\":\"survey\"}],\"u\":0}"));

        await backend.PollOutboundOnceAsync(CancellationToken.None);

        var script = Assert.Single(ch.ExecutedScripts, s => s.Contains("start-survey"));
        Assert.Contains("6813abc", script);          // actionType IS the flow id
        Assert.Contains("\\\"format\\\":\\\"survey\\\"", script.Replace("\"", "\\\""));
    }

    [Fact]
    public async Task Poll_AutoStartsSurvey_HonorsSurveyFullFormat()
    {
        var (backend, ch, http) = await NewInitializedAsync();
        ch.SimulateIncoming("{\"name\":\"ping\"}");
        http.Responses.Enqueue(new HttpResult(200,
            "{\"a\":[{\"actionType\":\"6813abc\",\"outbound\":\"ob1\",\"format\":\"survey_full\"}],\"u\":0}"));

        await backend.PollOutboundOnceAsync(CancellationToken.None);

        Assert.Contains(ch.ExecutedScripts, s => s.Contains("start-survey") && s.Contains("survey_full"));
    }

    [Theory]
    [InlineData("notification")]
    [InlineData("banner")]
    [InlineData("modal")]
    [InlineData("tour")]
    public async Task Poll_DoesNotStartSurvey_ForHostRenderedOrOutOfScopeActions(string actionType)
    {
        var (backend, ch, http) = await NewInitializedAsync();
        ch.SimulateIncoming("{\"name\":\"ping\"}");
        http.Responses.Enqueue(new HttpResult(200,
            $"{{\"a\":[{{\"actionType\":\"{actionType}\",\"outbound\":\"ob1\"}}],\"u\":0}}"));

        await backend.PollOutboundOnceAsync(CancellationToken.None);

        Assert.DoesNotContain(ch.ExecutedScripts, s => s.Contains("start-survey"));
    }

    [Fact]
    public async Task Poll_DoesNotDispatchActions_WhileWidgetOpen()
    {
        var (backend, ch, http) = await NewInitializedAsync();
        ch.SimulateIncoming("{\"name\":\"ping\"}");
        backend.Open();                       // widget open -> outbound must not hijack the screen
        object? unread = null;
        backend.RegisterListener("notificationCountUpdated", d => unread = d);
        http.Responses.Enqueue(new HttpResult(200,
            "{\"a\":[{\"actionType\":\"6813abc\",\"outbound\":\"ob1\",\"format\":\"survey\"}],\"u\":4}"));

        await backend.PollOutboundOnceAsync(CancellationToken.None);

        Assert.DoesNotContain(ch.ExecutedScripts, s => s.Contains("start-survey"));
        Assert.Equal(4, unread);              // ...but the unread count still applies (iOS behaviour)
    }

    // The navigation API must also reveal the messenger — the reference SDKs end each of these in
    // showWidget(). Without it the command reaches a widget the user cannot see.

    [Fact]
    public async Task NavigationMethods_OpenTheWidget()
    {
        var (backend, ch, _) = await NewInitializedAsync();
        ch.SimulateIncoming("{\"name\":\"ping\"}");
        var opened = 0;
        backend.RegisterListener("widgetOpened", _ => opened++);

        backend.OpenHelpCenter(showBackButton: true);

        Assert.Equal(1, opened);
        Assert.True(backend.IsOpened());
        Assert.Contains(ch.ExecutedScripts, s => s.Contains("open-helpcenter"));
    }

    [Fact]
    public async Task Open_IsIdempotent()
    {
        var (backend, ch, _) = await NewInitializedAsync();
        ch.SimulateIncoming("{\"name\":\"ping\"}");
        var opened = 0;
        backend.RegisterListener("widgetOpened", _ => opened++);

        backend.Open();
        backend.StartBot("b1", true);   // navigating while already open must not re-fire
        backend.Open();

        Assert.Equal(1, opened);
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
