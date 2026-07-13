using GleapSDK.Bridge;
using GleapSDK.Serialization;

namespace Gleap.Core.Tests;

public class GleapBridgeMessageTests
{
    private readonly IJsonSerializer _json = new SystemTextJsonSerializer();

    [Fact]
    public void Serializes_ToNameDataShareToken()
    {
        var msg = new GleapBridgeMessage
        {
            Name = "open-conversation",
            Data = new Dictionary<string, object> { ["shareToken"] = "abc" }
        };

        var s = _json.Serialize(msg);

        Assert.Contains("\"name\":\"open-conversation\"", s);
        Assert.Contains("\"data\":", s);
    }

    [Fact]
    public void OmitsShareToken_WhenNull()
    {
        var s = _json.Serialize(new GleapBridgeMessage { Name = "ping" });
        Assert.DoesNotContain("shareToken", s);
    }
}
