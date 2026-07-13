using Gleap.Core.Tests.Fakes;
using GleapSDK.Http;
using GleapSDK.Models;
using GleapSDK.Serialization;

namespace Gleap.Core.Tests;

public class ApiClientIdentityTests
{
    private static ApiClient NewClient(FakeHttpTransport t) =>
        new ApiClient(t, new SystemTextJsonSerializer(), GleapEndpoints.Default, "sdk-key-123");

    [Fact]
    public async Task Identify_PostsToIdentify_WithUserAndHeaders()
    {
        var t = new FakeHttpTransport();
        t.Responses.Enqueue(new HttpResult(200, "{\"gleapId\":\"g2\",\"gleapHash\":\"h2\"}"));
        var client = NewClient(t);

        var res = await client.IdentifyAsync(
            "u1", new GleapUserProperty { Email = "a@b.c" }, "hash1", "g1", "h1", CancellationToken.None);

        var call = t.Calls[0];
        Assert.Equal("https://api.gleap.io/sessions/identify", call.Url);
        Assert.Equal("POST", call.Method);
        Assert.Equal("g1", call.Headers["Gleap-Id"]);
        Assert.Contains("\"userId\":\"u1\"", call.Body);
        Assert.Contains("\"email\":\"a@b.c\"", call.Body);
        Assert.Contains("\"userHash\":\"hash1\"", call.Body);
        Assert.Equal("g2", res.GleapId);
    }

    [Fact]
    public async Task UpdateContact_PostsToPartialUpdate()
    {
        var t = new FakeHttpTransport();
        t.Responses.Enqueue(new HttpResult(200, "{}"));
        var client = NewClient(t);

        await client.UpdateContactAsync(new GleapUserProperty { Name = "Ada" }, "g1", "h1", CancellationToken.None);

        var call = t.Calls[0];
        Assert.Equal("https://api.gleap.io/sessions/partialupdate", call.Url);
        Assert.Contains("\"data\"", call.Body);
        Assert.Contains("Ada", call.Body);
    }
}
