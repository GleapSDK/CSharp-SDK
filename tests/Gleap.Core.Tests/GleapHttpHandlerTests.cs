using System.Net;
using Gleap.Core.Tests.Fakes;
using GleapSDK.Collection;
using GleapSDK.Http;

namespace Gleap.Core.Tests;

public class GleapHttpHandlerTests
{
    private sealed class StubInner : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("pong")
            });
    }

    [Fact]
    public async Task Captures_RequestAndResponse_IntoBuffer()
    {
        var buffer = new NetworkLogBuffer(100);
        var handler = new GleapHttpHandler(buffer, new FakeClock()) { InnerHandler = new StubInner() };
        using var client = new HttpClient(handler);

        await client.GetAsync("https://api.example.com/ping");

        var log = Assert.Single(buffer.Snapshot());
        Assert.Equal("GET", log.Type);
        Assert.Equal("https://api.example.com/ping", log.Url);
        Assert.Equal(200, log.Response.Status);
        Assert.Equal("pong", log.Response.ResponseText);
        Assert.True(log.Success);
    }
}
