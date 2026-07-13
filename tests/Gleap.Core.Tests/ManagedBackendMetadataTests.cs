using Gleap.Core.Tests.Fakes;
using GleapSDK;
using GleapSDK.Http;
using GleapSDK.Metadata;
using GleapSDK.Serialization;
using GleapSDK.Session;

namespace Gleap.Core.Tests;

public class ManagedBackendMetadataTests
{
    private sealed class FakeMetadataProvider : IMetadataProvider
    {
        public IReadOnlyDictionary<string, object?> Collect() => new Dictionary<string, object?>
        {
            ["sdkType"] = "TEST/X"
        };
    }

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
            Endpoints = GleapEndpoints.Default,
            Metadata = new FakeMetadataProvider()
        });
        backend.InitializeAsync("token-1", CancellationToken.None).GetAwaiter().GetResult();
        return (backend, ch);
    }

    [Fact]
    public void CollectTicketData_UsesInjectedMetadataProvider()
    {
        var (backend, ch) = NewInitialized();
        ch.SimulateIncoming("{\"name\":\"ping\"}");

        ch.SimulateIncoming("{\"name\":\"collect-ticket-data\"}");

        Assert.Contains(ch.ExecutedScripts, s => s.Contains("\"sdkType\":\"TEST/X\""));
    }
}
