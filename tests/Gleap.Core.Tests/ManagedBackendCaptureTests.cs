using Gleap.Core.Tests.Fakes;
using GleapSDK;
using GleapSDK.Http;
using GleapSDK.Serialization;
using GleapSDK.Session;

namespace Gleap.Core.Tests;

public class ManagedBackendCaptureTests
{
    private static (ManagedBackend backend, FakeWebViewChannel ch, FakeHttpTransport http) NewInitialized(
        ManagedBackend.Dependencies? overrides = null)
    {
        var http = overrides?.Http as FakeHttpTransport ?? new FakeHttpTransport();
        http.Responses.Enqueue(new HttpResult(200, "{\"gleapId\":\"g1\",\"gleapHash\":\"h1\"}"));
        http.Responses.Enqueue(new HttpResult(200, "{\"flowConfig\":{},\"projectActions\":{}}"));
        var ch = new FakeWebViewChannel();
        var deps = overrides ?? new ManagedBackend.Dependencies();
        deps.Http = http;
        deps.Json = new SystemTextJsonSerializer();
        deps.Store = new InMemoryKeyValueStore();
        deps.Channel = ch;
        deps.Endpoints = GleapEndpoints.Default;
        var backend = new ManagedBackend(deps);
        backend.InitializeAsync("token-1", CancellationToken.None).GetAwaiter().GetResult();
        return (backend, ch, http);
    }

    [Fact]
    public async Task SendFeedback_IncludesScreenshot_FromScreenshotProvider()
    {
        var (backend, ch, http) = NewInitialized(new ManagedBackend.Dependencies
        {
            Screenshot = new FakeScreenshotProvider("data:image/png;base64,SHOT")
        });
        ch.SimulateIncoming("{\"name\":\"ping\"}");
        http.Responses.Enqueue(new HttpResult(200, "{\"shareToken\":\"st1\"}"));

        ch.SimulateIncoming("{\"name\":\"send-feedback\",\"data\":{\"formData\":{\"description\":\"boom\"},\"action\":{\"feedbackType\":\"BUG\"}}}");
        await backend.LastFeedbackTask!;

        var bugCall = Assert.Single(http.Calls, c => c.Url == "https://api.gleap.io/bugs/v2");
        Assert.Contains("data:image/png;base64,SHOT", bugCall.Body);
    }

    [Fact]
    public async Task SendFeedback_PrefersEditedScreenshot_OverFreshCapture()
    {
        var (backend, ch, http) = NewInitialized(new ManagedBackend.Dependencies
        {
            Screenshot = new FakeScreenshotProvider("data:image/png;base64,FRESH")
        });
        ch.SimulateIncoming("{\"name\":\"ping\"}");
        http.Responses.Enqueue(new HttpResult(200, "{\"shareToken\":\"st1\"}"));

        // The widget's editor sends back the user-annotated screenshot.
        ch.SimulateIncoming("{\"name\":\"screenshot-updated\",\"data\":\"data:image/png;base64,EDITED\"}");
        ch.SimulateIncoming("{\"name\":\"send-feedback\",\"data\":{\"formData\":{},\"action\":{\"feedbackType\":\"BUG\"}}}");
        await backend.LastFeedbackTask!;

        var bugCall = Assert.Single(http.Calls, c => c.Url == "https://api.gleap.io/bugs/v2");
        Assert.Contains("EDITED", bugCall.Body);
        Assert.DoesNotContain("FRESH", bugCall.Body);
    }

    [Fact]
    public async Task CleanupDrawings_RevertsToOriginalScreenshot()
    {
        var (backend, ch, http) = NewInitialized(new ManagedBackend.Dependencies
        {
            Screenshot = new FakeScreenshotProvider("data:image/png;base64,FRESH")
        });
        ch.SimulateIncoming("{\"name\":\"ping\"}");
        http.Responses.Enqueue(new HttpResult(200, "{\"shareToken\":\"st1\"}"));

        ch.SimulateIncoming("{\"name\":\"screenshot-updated\",\"data\":\"data:image/png;base64,EDITED\"}");
        ch.SimulateIncoming("{\"name\":\"cleanup-drawings\"}");   // user discards annotations
        ch.SimulateIncoming("{\"name\":\"send-feedback\",\"data\":{\"formData\":{},\"action\":{\"feedbackType\":\"BUG\"}}}");
        await backend.LastFeedbackTask!;

        var bugCall = Assert.Single(http.Calls, c => c.Url == "https://api.gleap.io/bugs/v2");
        Assert.Contains("FRESH", bugCall.Body);
        Assert.DoesNotContain("EDITED", bugCall.Body);
    }

    [Fact]
    public async Task PrepareScreenshot_SendsScreenshotUpdateToWidget()
    {
        var (backend, ch, _) = NewInitialized(new ManagedBackend.Dependencies
        {
            Screenshot = new FakeScreenshotProvider("data:image/png;base64,SHOT")
        });
        ch.SimulateIncoming("{\"name\":\"ping\"}");

        await backend.PrepareScreenshotAsync();

        Assert.Contains(ch.ExecutedScripts, s => s.Contains("screenshot-update") && s.Contains("SHOT"));
    }

    [Fact]
    public async Task AddReplayFrame_IncludesReplay()
    {
        var (backend, ch, http) = NewInitialized();
        ch.SimulateIncoming("{\"name\":\"ping\"}");
        http.Responses.Enqueue(new HttpResult(200, "{\"shareToken\":\"st1\"}"));

        backend.AddReplayFrame("FRAME1");
        ch.SimulateIncoming("{\"name\":\"send-feedback\",\"data\":{\"formData\":{\"description\":\"boom\"},\"action\":{\"feedbackType\":\"BUG\"}}}");
        await backend.LastFeedbackTask!;

        var bugCall = Assert.Single(http.Calls, c => c.Url == "https://api.gleap.io/bugs/v2");
        Assert.Contains("\"replay\"", bugCall.Body);
        Assert.Contains("FRAME1", bugCall.Body);
    }
}
