using Gleap.Core.Tests.Fakes;
using GleapSDK;
using GleapSDK.Http;
using GleapSDK.Serialization;
using GleapSDK.Session;

namespace Gleap.Core.Tests;

public class ManagedBackendNavTests
{
    private static (ManagedBackend backend, FakeWebViewChannel ch) NewInitialized()
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
        backend.InitializeAsync("token-1", CancellationToken.None).GetAwaiter().GetResult();
        return (backend, ch);
    }

    [Fact]
    public void SearchHelpCenter_And_AskAI_SendExpectedCommands()
    {
        var (backend, ch) = NewInitialized();
        ch.SimulateIncoming("{\"name\":\"ping\"}");

        backend.SearchHelpCenter("reset", true);
        backend.AskAI("why?", true);

        Assert.Contains(ch.ExecutedScripts, s => s.Contains("open-helpcenter-search") && s.Contains("reset"));
        Assert.Contains(ch.ExecutedScripts, s => s.Contains("ask-ai") && s.Contains("why?"));
    }
}
