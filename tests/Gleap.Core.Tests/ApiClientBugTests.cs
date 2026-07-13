using Gleap.Core.Tests.Fakes;
using GleapSDK.Http;
using GleapSDK.Serialization;

namespace Gleap.Core.Tests;

public class ApiClientBugTests
{
    private static ApiClient NewClient(FakeHttpTransport t) =>
        new ApiClient(t, new SystemTextJsonSerializer(), GleapEndpoints.Default, "sdk-key-123");

    [Fact]
    public async Task SubmitBug_PostsToBugsV2_WithHeaders()
    {
        var t = new FakeHttpTransport();
        t.Responses.Enqueue(new HttpResult(200, "{\"shareToken\":\"st1\"}"));
        var client = NewClient(t);

        var raw = await client.SubmitBugAsync(
            new Dictionary<string, object?> { ["type"] = "BUG" }, "g1", "h1", CancellationToken.None);

        var call = t.Calls[0];
        Assert.Equal("POST", call.Method);
        Assert.Equal("https://api.gleap.io/bugs/v2", call.Url);
        Assert.Equal("g1", call.Headers["Gleap-Id"]);
        Assert.Contains("\"type\":\"BUG\"", call.Body);
        Assert.Contains("shareToken", raw);
    }

    [Fact]
    public async Task SubmitBug_NonSuccess_Throws()
    {
        var t = new FakeHttpTransport();
        t.Responses.Enqueue(new HttpResult(500, "err"));
        var client = NewClient(t);

        await Assert.ThrowsAsync<GleapApiException>(() =>
            client.SubmitBugAsync(new Dictionary<string, object?> { ["type"] = "BUG" }, "g1", "h1", CancellationToken.None));
    }
}
