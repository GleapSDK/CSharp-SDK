using Gleap.Core.Tests.Fakes;
using GleapSDK;
using GleapSDK.Http;
using GleapSDK.Serialization;
using GleapSDK.Session;

namespace Gleap.Core.Tests;

public class ManagedBackendTests
{
    private static (ManagedBackend backend, FakeWebViewChannel ch, FakeHttpTransport http) New()
    {
        var http = new FakeHttpTransport();
        var ch = new FakeWebViewChannel();
        var deps = new ManagedBackend.Dependencies
        {
            Http = http,
            Json = new SystemTextJsonSerializer(),
            Store = new InMemoryKeyValueStore(),
            Channel = ch,
            Endpoints = GleapEndpoints.Default
        };
        return (new ManagedBackend(deps), ch, http);
    }

    [Fact]
    public async Task Initialize_CreatesSessionAndLoadsConfig()
    {
        var (backend, ch, http) = New();
        http.Responses.Enqueue(new HttpResult(200, "{\"gleapId\":\"g1\",\"gleapHash\":\"h1\"}")); // sessions
        http.Responses.Enqueue(new HttpResult(200, "{\"flowConfig\":{},\"projectActions\":{}}")); // config

        await backend.InitializeAsync("token-1", CancellationToken.None);

        Assert.Equal("https://api.gleap.io/sessions", http.Calls[0].Url);
        Assert.StartsWith("https://api.gleap.io/config/token-1", http.Calls[1].Url);
    }

    [Fact]
    public async Task Open_AfterPing_SendsBootstrapThenNoCommand()
    {
        var (backend, ch, http) = New();
        http.Responses.Enqueue(new HttpResult(200, "{\"gleapId\":\"g1\",\"gleapHash\":\"h1\"}"));
        http.Responses.Enqueue(new HttpResult(200, "{\"flowConfig\":{},\"projectActions\":{}}"));
        await backend.InitializeAsync("token-1", CancellationToken.None);

        backend.Open();                               // queued (not connected yet)
        ch.SimulateIncoming("{\"name\":\"ping\"}");   // handshake

        Assert.Contains(ch.ExecutedScripts, s => s.Contains("session-update"));
    }

    [Fact]
    public async Task StartBot_QueuesCommand_FlushedAfterPing()
    {
        var (backend, ch, http) = New();
        http.Responses.Enqueue(new HttpResult(200, "{\"gleapId\":\"g1\",\"gleapHash\":\"h1\"}"));
        http.Responses.Enqueue(new HttpResult(200, "{\"flowConfig\":{},\"projectActions\":{}}"));
        await backend.InitializeAsync("token-1", CancellationToken.None);

        backend.StartBot("bot42", showBackButton: false);
        ch.SimulateIncoming("{\"name\":\"ping\"}");

        Assert.Contains(ch.ExecutedScripts, s => s.Contains("\"botId\":\"bot42\""));
    }
}
