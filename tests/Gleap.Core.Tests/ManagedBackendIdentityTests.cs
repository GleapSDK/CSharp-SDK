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

    [Fact]
    public async Task IdentifyContact_SessionUpdate_CarriesAllUserFields()
    {
        var (backend, ch, http) = NewInitialized();
        ch.SimulateIncoming("{\"name\":\"ping\"}");
        http.Responses.Enqueue(new HttpResult(200, "{\"gleapId\":\"g2\",\"gleapHash\":\"h2\"}"));

        await backend.IdentifyContactAsync("u1", new GleapUserProperty
        {
            Email = "a@b.c",
            Phone = "123",
            Plan = "pro",
            CompanyName = "Acme",
            CompanyId = "c1",
            Avatar = "https://a",
            Value = 42.0,
            Sla = 3.0
        }, null, CancellationToken.None);

        var sessionUpdate = ch.ExecutedScripts.Last(s => s.Contains("session-update"));
        Assert.Contains("\"phone\":\"123\"", sessionUpdate);
        Assert.Contains("\"plan\":\"pro\"", sessionUpdate);
        Assert.Contains("\"companyName\":\"Acme\"", sessionUpdate);
        Assert.Contains("\"companyId\":\"c1\"", sessionUpdate);
        Assert.Contains("\"avatar\":\"https://a\"", sessionUpdate);
        Assert.Contains("\"value\":42", sessionUpdate);
        Assert.Contains("\"sla\":3", sessionUpdate);
    }
}
