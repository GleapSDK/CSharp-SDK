using System.Text.Json;
using Gleap.Core.Tests.Fakes;
using GleapSDK;
using GleapSDK.Http;
using GleapSDK.Serialization;
using GleapSDK.Session;

namespace Gleap.Core.Tests;

public class ManagedBackendEventsTests
{
    private static (ManagedBackend backend, FakeWebViewChannel ch, FakeHttpTransport http) NewUninitialized()
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
        return (backend, ch, http);
    }

    private static async Task<(ManagedBackend backend, FakeWebViewChannel ch, FakeHttpTransport http)> NewInitializedAsync()
    {
        var (backend, ch, http) = NewUninitialized();
        await backend.InitializeAsync("token-1", CancellationToken.None);
        return (backend, ch, http);
    }

    [Fact]
    public async Task Initialized_FiresAfterInit()
    {
        var (backend, _, _) = NewUninitialized();
        var fired = false;
        backend.RegisterListener("initialized", _ => fired = true);

        await backend.InitializeAsync("token-1", CancellationToken.None);

        Assert.True(fired);
    }

    [Fact]
    public async Task WidgetOpened_FiresOnOpen()
    {
        var (backend, ch, _) = await NewInitializedAsync();
        ch.SimulateIncoming("{\"name\":\"ping\"}");
        var fired = false;
        backend.RegisterListener("widgetOpened", _ => fired = true);

        backend.Open();

        Assert.True(fired);
    }

    [Fact]
    public async Task WidgetClosed_FiresOnClose()
    {
        var (backend, ch, _) = await NewInitializedAsync();
        ch.SimulateIncoming("{\"name\":\"ping\"}");
        var fired = false;
        backend.RegisterListener("widgetClosed", _ => fired = true);

        backend.Close();

        Assert.True(fired);
    }

    [Fact]
    public async Task CustomAction_FiresWithName()
    {
        var (backend, ch, _) = await NewInitializedAsync();
        object? received = null;
        backend.RegisterListener("customActionTriggered", data => received = data);

        ch.SimulateIncoming("{\"name\":\"run-custom-action\",\"data\":\"myAction\",\"shareToken\":\"t\"}");

        Assert.NotNull(received);
        Assert.Contains("myAction", JsonSerializer.Serialize(received));
    }

    [Fact]
    public async Task FeedbackSent_FiresOnSuccess()
    {
        var (backend, ch, http) = await NewInitializedAsync();
        ch.SimulateIncoming("{\"name\":\"ping\"}");
        var fired = false;
        backend.RegisterListener("feedbackSent", _ => fired = true);
        http.Responses.Enqueue(new HttpResult(200, "{\"shareToken\":\"st1\"}"));

        ch.SimulateIncoming("{\"name\":\"send-feedback\",\"data\":{\"formData\":{\"description\":\"boom\"},\"action\":{\"feedbackType\":\"BUG\"}}}");
        await backend.LastFeedbackTask!;

        Assert.True(fired);
    }
}
