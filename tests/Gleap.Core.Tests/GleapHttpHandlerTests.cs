using System.Net;
using System.Net.Http.Headers;
using Gleap.Core.Tests.Fakes;
using GleapSDK.Collection;
using GleapSDK.Http;

namespace Gleap.Core.Tests;

public class GleapHttpHandlerTests
{
    private sealed class StubInner : HttpMessageHandler
    {
        private readonly Func<HttpContent> _content;
        public StubInner() : this(() => new StringContent("pong")) { }
        public StubInner(Func<HttpContent> content) => _content = content;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = _content() });
    }

    /// <summary>Declares a huge Content-Length and throws if anything actually reads it — so the test fails
    /// loudly if the handler buffers the body before applying the cap.</summary>
    private sealed class ExplodingHugeContent : HttpContent
    {
        public ExplodingHugeContent() => Headers.ContentType = new MediaTypeHeaderValue("application/json");

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            throw new InvalidOperationException("the body must never be read when Content-Length exceeds the cap");

        protected override bool TryComputeLength(out long length)
        {
            length = 50_000_000;
            return true;
        }
    }

    private static (NetworkLogBuffer buffer, HttpClient client) NewClient(Func<HttpContent>? content = null)
    {
        var buffer = new NetworkLogBuffer(100);
        var handler = new GleapHttpHandler(buffer, new FakeClock())
        {
            InnerHandler = content is null ? new StubInner() : new StubInner(content)
        };
        return (buffer, new HttpClient(handler));
    }

    [Fact]
    public async Task Captures_RequestAndResponse_IntoBuffer()
    {
        var (buffer, client) = NewClient();
        using var _ = client;

        await client.GetAsync("https://api.example.com/ping");

        var log = Assert.Single(buffer.Snapshot());
        Assert.Equal("GET", log.Type);
        Assert.Equal("https://api.example.com/ping", log.Url);
        Assert.Equal(200, log.Response.Status);
        Assert.Equal("pong", log.Response.ResponseText);
        Assert.True(log.Success);
    }

    [Fact]
    public async Task OversizedResponse_IsNotRead_AndLogsSentinel()
    {
        var (buffer, client) = NewClient(() => new ExplodingHugeContent());
        using var _ = client;

        // ResponseHeadersRead so HttpClient itself doesn't buffer the body — this isolates the handler,
        // which would throw here if it read the body instead of trusting the declared Content-Length.
        await client.GetAsync("https://api.example.com/huge", HttpCompletionOption.ResponseHeadersRead);

        var log = Assert.Single(buffer.Snapshot());
        Assert.Equal("<response_too_large>", log.Response.ResponseText);
    }

    [Fact]
    public async Task BinaryResponse_IsNotLogged()
    {
        var (buffer, client) = NewClient(() =>
        {
            var c = new ByteArrayContent(new byte[] { 1, 2, 3, 4 });
            c.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            return c;
        });
        using var _ = client;

        await client.GetAsync("https://api.example.com/blob");

        var log = Assert.Single(buffer.Snapshot());
        Assert.Equal("", log.Response.ResponseText);
    }

    [Fact]
    public async Task JsonResponse_IsLogged()
    {
        var (buffer, client) = NewClient(() => new StringContent("{\"ok\":true}", System.Text.Encoding.UTF8, "application/json"));
        using var _ = client;

        await client.GetAsync("https://api.example.com/json");

        var log = Assert.Single(buffer.Snapshot());
        Assert.Equal("{\"ok\":true}", log.Response.ResponseText);
    }

    [Fact]
    public async Task RequestPayload_IsCaptured()
    {
        var (buffer, client) = NewClient();
        using var _ = client;

        await client.PostAsync("https://api.example.com/x",
            new StringContent("{\"a\":1}", System.Text.Encoding.UTF8, "application/json"));

        var log = Assert.Single(buffer.Snapshot());
        Assert.Equal("{\"a\":1}", log.Request.Payload);
    }
}
