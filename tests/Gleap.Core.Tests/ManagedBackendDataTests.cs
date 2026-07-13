using System.Threading;
using System.Threading.Tasks;
using Gleap.Core.Tests.Fakes;
using GleapSDK;
using GleapSDK.Http;
using GleapSDK.Serialization;
using GleapSDK.Session;
using Xunit;

namespace Gleap.Core.Tests;

public class ManagedBackendDataTests
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
    public void CollectTicketData_RepliesWithCollectedData()
    {
        var (backend, ch) = NewInitialized();
        backend.Log("boom", LogLevel.Error);
        backend.SetCustomData("plan", "pro");
        ch.SimulateIncoming("{\"name\":\"ping\"}"); // connect so replies are sent immediately

        ch.SimulateIncoming("{\"name\":\"collect-ticket-data\"}");

        Assert.Contains(ch.ExecutedScripts, s =>
            s.Contains("collect-ticket-data") && s.Contains("boom") && s.Contains("\"plan\":\"pro\""));
    }

    [Fact]
    public void TrackEvent_And_SetTags_DoNotThrow_BeforePing()
    {
        var (backend, _) = NewInitialized();
        backend.TrackEvent("opened", null);
        backend.SetTags(new[] { "vip" });
        backend.AddAttachment("Zm9v", "a.txt");
    }
}
