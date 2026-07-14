using Gleap.Core.Tests.Fakes;
using GleapSDK.Http;
using GleapSDK.Models;
using GleapSDK.Serialization;

namespace Gleap.Core.Tests;

public class PlatformSeamTests
{
    private static ApiClient NewClient(FakeHttpTransport t) =>
        new ApiClient(t, new SystemTextJsonSerializer(), GleapEndpoints.Default, "sdk-key-123",
            platform: "android", sdkVersion: "1.2.3");

    [Fact]
    public async Task CreateSession_UsesInjectedPlatform_NotHardcodedWindows()
    {
        var t = new FakeHttpTransport();
        t.Responses.Enqueue(new HttpResult(200, "{\"gleapId\":\"g1\",\"gleapHash\":\"h1\"}"));
        var client = NewClient(t);

        await client.CreateSessionAsync("en", "mobile", null, null, CancellationToken.None);

        Assert.Contains("\"platform\":\"android\"", t.Calls[0].Body);
    }

    [Fact]
    public async Task Ping_UsesInjectedPlatformAndSdkVersion()
    {
        var t = new FakeHttpTransport();
        t.Responses.Enqueue(new HttpResult(200, "{}"));
        var client = NewClient(t);

        await client.PingAsync(1, Array.Empty<object?>(), false, "g1", "h1", CancellationToken.None);

        Assert.Contains("\"type\":\"android\"", t.Calls[0].Body);
        Assert.Contains("\"sdkVersion\":\"1.2.3\"", t.Calls[0].Body);
    }

    [Fact]
    public async Task UpdateContact_UsesInjectedPlatformAndSdkVersion()
    {
        var t = new FakeHttpTransport();
        t.Responses.Enqueue(new HttpResult(200, "{}"));
        var client = NewClient(t);

        await client.UpdateContactAsync(new GleapUserProperty(), "g1", "h1", CancellationToken.None);

        Assert.Contains("\"type\":\"android\"", t.Calls[0].Body);
        Assert.Contains("\"sdkVersion\":\"1.2.3\"", t.Calls[0].Body);
    }
}
