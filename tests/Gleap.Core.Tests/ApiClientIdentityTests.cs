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

    // The server treats a null known-key as an explicit unset ($unset), so any property the caller did not
    // set must be absent from the body — otherwise identifying with a partial profile wipes the rest of the
    // contact. These assert ABSENCE; the older tests only assert presence and so never caught this.

    [Theory]
    [InlineData("phone")]
    [InlineData("plan")]
    [InlineData("companyName")]
    [InlineData("companyId")]
    [InlineData("avatar")]
    [InlineData("lang")]
    [InlineData("value")]
    [InlineData("sla")]
    [InlineData("customData")]
    public async Task Identify_OmitsUnsetProperties(string absentKey)
    {
        var t = new FakeHttpTransport();
        t.Responses.Enqueue(new HttpResult(200, "{}"));
        var client = NewClient(t);

        await client.IdentifyAsync(
            "u1", new GleapUserProperty { Name = "Bob", Email = "b@x.com" }, null, "g1", "h1",
            CancellationToken.None);

        Assert.DoesNotContain($"\"{absentKey}\"", t.Calls[0].Body);
    }

    [Fact]
    public async Task Identify_KeepsSetProperties()
    {
        var t = new FakeHttpTransport();
        t.Responses.Enqueue(new HttpResult(200, "{}"));
        var client = NewClient(t);

        await client.IdentifyAsync(
            "u1", new GleapUserProperty { Name = "Bob", Plan = "pro", Value = 42 }, null, "g1", "h1",
            CancellationToken.None);

        var body = t.Calls[0].Body;
        Assert.Contains("\"userId\":\"u1\"", body);
        Assert.Contains("\"name\":\"Bob\"", body);
        Assert.Contains("\"plan\":\"pro\"", body);
        Assert.Contains("\"value\":42", body);
        Assert.DoesNotContain("\"email\"", body);
    }

    [Fact]
    public async Task UpdateContact_OmitsUnsetProperties()
    {
        var t = new FakeHttpTransport();
        t.Responses.Enqueue(new HttpResult(200, "{}"));
        var client = NewClient(t);

        await client.UpdateContactAsync(new GleapUserProperty { Name = "Ada" }, "g1", "h1", CancellationToken.None);

        var body = t.Calls[0].Body;
        Assert.Contains("\"name\":\"Ada\"", body);
        Assert.DoesNotContain("\"plan\"", body);
        Assert.DoesNotContain("\"sla\"", body);
    }

    [Fact]
    public async Task Identify_KeepsExplicitEmptyString()
    {
        var t = new FakeHttpTransport();
        t.Responses.Enqueue(new HttpResult(200, "{}"));
        var client = NewClient(t);

        // "" is a deliberate value (clear the field), not an unset — it must still be sent, like iOS.
        await client.IdentifyAsync(
            "u1", new GleapUserProperty { Name = "" }, null, "g1", "h1", CancellationToken.None);

        Assert.Contains("\"name\":\"\"", t.Calls[0].Body);
    }
}
