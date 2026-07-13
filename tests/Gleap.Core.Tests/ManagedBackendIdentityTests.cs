using Gleap.Core.Tests.Fakes;
using GleapSDK;
using GleapSDK.Http;
using GleapSDK.Models;
using GleapSDK.Serialization;
using GleapSDK.Session;

namespace Gleap.Core.Tests;

public class ManagedBackendIdentityTests
{
    private static (ManagedBackend backend, FakeWebViewChannel ch, FakeHttpTransport http) NewInitialized()
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
        return (backend, ch, http);
    }

    [Fact]
    public async Task IdentifyContact_ReSendsSessionUpdate_WithUser()
    {
        var (backend, ch, http) = NewInitialized();
        ch.SimulateIncoming("{\"name\":\"ping\"}");
        var scriptsBeforeIdentify = ch.ExecutedScripts.Count;
        http.Responses.Enqueue(new HttpResult(200, "{\"gleapId\":\"g2\",\"gleapHash\":\"h2\"}"));

        await backend.IdentifyContactAsync("u1", new GleapUserProperty { Email = "a@b.c" }, null, CancellationToken.None);

        var laterScripts = ch.ExecutedScripts.Skip(scriptsBeforeIdentify);
        Assert.Contains(laterScripts, s =>
            s.Contains("session-update") && s.Contains("\"userId\":\"u1\"") && s.Contains("a@b.c"));
        Assert.True(backend.IsUserIdentified());
        Assert.Equal("a@b.c", backend.GetIdentity()!.Email);
    }
}
