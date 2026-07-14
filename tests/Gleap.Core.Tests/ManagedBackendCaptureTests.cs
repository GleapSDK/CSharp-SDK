using System.Text;
using Gleap.Core.Tests.Fakes;
using GleapSDK;
using GleapSDK.Http;
using GleapSDK.Serialization;
using GleapSDK.Session;

namespace Gleap.Core.Tests;

public class ManagedBackendCaptureTests
{
    // A valid base64 data-URI whose bytes decode back to the marker text (so uploads are inspectable).
    private static string DataUri(string marker) =>
        "data:image/png;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(marker));

    private static string Uploaded(FakeHttpTransport.Upload u) => Encoding.UTF8.GetString(u.File);

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
    public async Task SendFeedback_UploadsScreenshot_AndReferencesItByUrl()
    {
        var (backend, ch, http) = NewInitialized(new ManagedBackend.Dependencies
        {
            Screenshot = new FakeScreenshotProvider(DataUri("SHOT"))
        });
        ch.SimulateIncoming("{\"name\":\"ping\"}");
        http.Responses.Enqueue(new HttpResult(200, "{\"shareToken\":\"st1\"}"));

        ch.SimulateIncoming("{\"name\":\"send-feedback\",\"data\":{\"formData\":{\"description\":\"boom\"},\"action\":{\"feedbackType\":\"BUG\"}}}");
        await backend.LastFeedbackTask!;

        // The screenshot is uploaded to /uploads/sdk (not inlined), and the report references the URL.
        var upload = Assert.Single(http.Uploads, u => u.Url == "https://api.gleap.io/uploads/sdk");
        Assert.Equal("SHOT", Uploaded(upload));
        Assert.Equal("screenshot.png", upload.FileName);
        var bugCall = Assert.Single(http.Calls, c => c.Url == "https://api.gleap.io/bugs/v2");
        Assert.Contains("screenshotUrl", bugCall.Body);
        Assert.Contains("uploaded.png", bugCall.Body);       // the fake upload's returned fileUrl
        Assert.DoesNotContain("base64", bugCall.Body);        // no inline base64 in the bug body
    }

    [Fact]
    public async Task SendFeedback_PrefersEditedScreenshot_OverFreshCapture()
    {
        var (backend, ch, http) = NewInitialized(new ManagedBackend.Dependencies
        {
            Screenshot = new FakeScreenshotProvider(DataUri("FRESH"))
        });
        ch.SimulateIncoming("{\"name\":\"ping\"}");
        http.Responses.Enqueue(new HttpResult(200, "{\"shareToken\":\"st1\"}"));

        // The widget's editor sends back the user-annotated screenshot.
        ch.SimulateIncoming("{\"name\":\"screenshot-updated\",\"data\":\"" + DataUri("EDITED") + "\"}");
        ch.SimulateIncoming("{\"name\":\"send-feedback\",\"data\":{\"formData\":{},\"action\":{\"feedbackType\":\"BUG\"}}}");
        await backend.LastFeedbackTask!;

        var upload = Assert.Single(http.Uploads);
        Assert.Equal("EDITED", Uploaded(upload));   // the annotated one is uploaded, not the fresh capture
    }

    [Fact]
    public async Task CleanupDrawings_RevertsToOriginalScreenshot()
    {
        var (backend, ch, http) = NewInitialized(new ManagedBackend.Dependencies
        {
            Screenshot = new FakeScreenshotProvider(DataUri("FRESH"))
        });
        ch.SimulateIncoming("{\"name\":\"ping\"}");
        http.Responses.Enqueue(new HttpResult(200, "{\"shareToken\":\"st1\"}"));

        ch.SimulateIncoming("{\"name\":\"screenshot-updated\",\"data\":\"" + DataUri("EDITED") + "\"}");
        ch.SimulateIncoming("{\"name\":\"cleanup-drawings\"}");   // user discards annotations
        ch.SimulateIncoming("{\"name\":\"send-feedback\",\"data\":{\"formData\":{},\"action\":{\"feedbackType\":\"BUG\"}}}");
        await backend.LastFeedbackTask!;

        var upload = Assert.Single(http.Uploads);
        Assert.Equal("FRESH", Uploaded(upload));   // reverted to the fresh capture
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
    public async Task AddReplayFrame_UploadsFrames_AndReferencesThemByUrl()
    {
        var (backend, ch, http) = NewInitialized();
        ch.SimulateIncoming("{\"name\":\"ping\"}");
        http.Responses.Enqueue(new HttpResult(200, "{\"shareToken\":\"st1\"}"));

        backend.AddReplayFrame(DataUri("FRAMEBYTES"));
        ch.SimulateIncoming("{\"name\":\"send-feedback\",\"data\":{\"formData\":{\"description\":\"boom\"},\"action\":{\"feedbackType\":\"BUG\"}}}");
        await backend.LastFeedbackTask!;

        // Frames are uploaded to /uploads/sdksteps and referenced by URL in replay.frames — not inline.
        var upload = Assert.Single(http.MultiUploads, u => u.Url == "https://api.gleap.io/uploads/sdksteps");
        Assert.Single(upload.Files);
        var bugCall = Assert.Single(http.Calls, c => c.Url == "https://api.gleap.io/bugs/v2");
        Assert.Contains("\"replay\"", bugCall.Body);
        Assert.Contains("\"frames\"", bugCall.Body);
        Assert.Contains("uploads.gleap.io/u0.png", bugCall.Body);   // the fake upload's returned URL
        Assert.DoesNotContain("FRAMEBYTES", bugCall.Body);          // no inline frame payload
    }

    [Fact]
    public async Task SendFeedback_UploadsAttachments_AndReferencesThemByUrl()
    {
        var (backend, ch, http) = NewInitialized();
        ch.SimulateIncoming("{\"name\":\"ping\"}");
        http.Responses.Enqueue(new HttpResult(200, "{\"shareToken\":\"st1\"}"));

        var rawBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("LOGDATA"));
        backend.AddAttachment(rawBase64, "diagnostics.txt");
        ch.SimulateIncoming("{\"name\":\"send-feedback\",\"data\":{\"formData\":{},\"action\":{\"feedbackType\":\"BUG\"}}}");
        await backend.LastFeedbackTask!;

        // Custom attachments upload to /uploads/attachments and are referenced by URL (name/type preserved).
        var upload = Assert.Single(http.MultiUploads, u => u.Url == "https://api.gleap.io/uploads/attachments");
        Assert.Equal("diagnostics.txt", Assert.Single(upload.Files).FileName);
        var bugCall = Assert.Single(http.Calls, c => c.Url == "https://api.gleap.io/bugs/v2");
        Assert.Contains("attachments", bugCall.Body);
        Assert.Contains("uploads.gleap.io/u0.png", bugCall.Body);   // referenced by uploaded URL
        Assert.Contains("diagnostics.txt", bugCall.Body);           // name preserved
        Assert.Contains("text/plain", bugCall.Body);                // MIME derived from extension
    }

    // A canned inner handler so the network-logging test never touches the real network.
    private sealed class StubHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage { Content = new StringContent("{\"ok\":true}") });
    }

    [Fact]
    public async Task NetworkLoggingHandler_CapturesRequests_OntoTickets()
    {
        var (backend, ch, http) = NewInitialized();
        ch.SimulateIncoming("{\"name\":\"ping\"}");
        http.Responses.Enqueue(new HttpResult(200, "{\"shareToken\":\"st1\"}"));

        // Route an app HttpClient through the handler the backend hands out.
        using (var client = new HttpClient(backend.CreateNetworkLoggingHandler(new StubHandler())))
        {
            await client.GetAsync("https://example.com/api/widgets");
        }

        ch.SimulateIncoming("{\"name\":\"send-feedback\",\"data\":{\"formData\":{\"description\":\"boom\"},\"action\":{\"feedbackType\":\"BUG\"}}}");
        await backend.LastFeedbackTask!;

        // The captured request rides along in networkLogs on the submitted ticket.
        var bugCall = Assert.Single(http.Calls, c => c.Url == "https://api.gleap.io/bugs/v2");
        Assert.Contains("networkLogs", bugCall.Body);
        Assert.Contains("example.com/api/widgets", bugCall.Body);
        Assert.Contains("GET", bugCall.Body);
    }
}
