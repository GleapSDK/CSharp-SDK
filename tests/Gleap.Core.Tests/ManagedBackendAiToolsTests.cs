using Gleap.Core.Tests.Fakes;
using GleapSDK;
using GleapSDK.Http;
using GleapSDK.Models;
using GleapSDK.Serialization;
using GleapSDK.Session;

namespace Gleap.Core.Tests;

public class ManagedBackendAiToolsTests
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
    public void SetAiTools_BeforePing_IncludedInConfigUpdate()
    {
        var (backend, ch) = NewInitialized();

        backend.SetAiTools(new[] { new AITool { Name = "getOrder" } });
        ch.SimulateIncoming("{\"name\":\"ping\"}");

        Assert.Contains(ch.ExecutedScripts, s =>
            s.Contains("config-update") && s.Contains("\"aiTools\":") && s.Contains("getOrder"));
    }

    [Fact]
    public void SetAiTools_AfterConnected_ResendsConfigUpdate()
    {
        var (backend, ch) = NewInitialized();
        ch.SimulateIncoming("{\"name\":\"ping\"}");
        var before = ch.ExecutedScripts.Count(s => s.Contains("config-update"));

        backend.SetAiTools(new[] { new AITool { Name = "later" } });

        var after = ch.ExecutedScripts.Count(s => s.Contains("config-update"));
        Assert.True(after > before, "SetAiTools should re-send a config-update after connect");
        Assert.Contains(ch.ExecutedScripts, s => s.Contains("config-update") && s.Contains("later"));
    }
}
