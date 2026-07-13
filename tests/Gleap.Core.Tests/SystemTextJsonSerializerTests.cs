using GleapSDK.Models;
using GleapSDK.Serialization;

namespace Gleap.Core.Tests;

public class SystemTextJsonSerializerTests
{
    private readonly IJsonSerializer _json = new SystemTextJsonSerializer();

    [Fact]
    public void Serialize_UsesCamelCase_AndOmitsNulls()
    {
        var p = new GleapUserProperty { UserId = "u1", Value = 5.0 };

        var s = _json.Serialize(p);

        Assert.Contains("\"userId\":\"u1\"", s);
        Assert.Contains("\"value\":5", s);
        Assert.DoesNotContain("email", s); // null omitted
    }

    [Fact]
    public void Deserialize_RoundTrips()
    {
        var s = _json.Serialize(new GleapUserProperty { Email = "a@b.c" });
        var back = _json.Deserialize<GleapUserProperty>(s);
        Assert.Equal("a@b.c", back.Email);
    }
}
