using Gleap.Core.Tests.Fakes;
using GleapSDK;
using GleapSDK.Http;
using GleapSDK.Serialization;
using GleapSDK.Session;

namespace Gleap.Core.Tests;

public class ManagedBackendFeedbackTests
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
    public async Task SendFeedback_AssemblesAndReplies_FeedbackSent()
    {
        var (backend, ch, http) = NewInitialized();
        ch.SimulateIncoming("{\"name\":\"ping\"}");
        http.Responses.Enqueue(new HttpResult(200, "{\"shareToken\":\"st1\"}"));

        ch.SimulateIncoming("{\"name\":\"send-feedback\",\"data\":{\"formData\":{\"description\":\"boom\"},\"action\":{\"feedbackType\":\"BUG\"}}}");
        await backend.LastFeedbackTask!;

        var bugCall = Assert.Single(http.Calls, c => c.Url == "https://api.gleap.io/bugs/v2");
        Assert.Contains("\"type\":\"BUG\"", bugCall.Body);
        Assert.Contains("boom", bugCall.Body);
        Assert.Contains(ch.ExecutedScripts, s => s.Contains("feedback-sent"));
    }

    [Fact]
    public async Task SendFeedback_OnApiError_RepliesFailed()
    {
        var (backend, ch, http) = NewInitialized();
        ch.SimulateIncoming("{\"name\":\"ping\"}");
        http.Responses.Enqueue(new HttpResult(500, "err"));

        ch.SimulateIncoming("{\"name\":\"send-feedback\",\"data\":{\"formData\":{\"description\":\"boom\"},\"action\":{\"feedbackType\":\"BUG\"}}}");
        await backend.LastFeedbackTask!;

        Assert.Contains(ch.ExecutedScripts, s => s.Contains("feedback-sending-failed"));
    }

    [Fact]
    public async Task SilentCrashReport_PostsCrash()
    {
        var (backend, ch, http) = NewInitialized();
        ch.SimulateIncoming("{\"name\":\"ping\"}");
        http.Responses.Enqueue(new HttpResult(200, "{\"shareToken\":\"st1\"}"));

        await backend.SendSilentCrashReportAsync("crashed", Severity.High, null, CancellationToken.None);

        var bugCall = Assert.Single(http.Calls, c => c.Url == "https://api.gleap.io/bugs/v2");
        Assert.Contains("\"type\":\"CRASH\"", bugCall.Body);
        Assert.Contains("\"priority\":\"HIGH\"", bugCall.Body);
        Assert.Contains("\"isSilent\":true", bugCall.Body);
        Assert.Contains("crashed", bugCall.Body);
    }
}
