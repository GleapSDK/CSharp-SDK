using GleapSDK.Collection;
using GleapSDK.Models;

namespace Gleap.Core.Tests;

public class NetworkLogBufferTests
{
    private static GleapNetworkLog Log(string url) => new()
    {
        Type = "GET",
        Url = url,
        Date = "d",
        Success = true,
        Request = new GleapNetworkRequest { Headers = new Dictionary<string, string> { ["Authorization"] = "secret", ["Accept"] = "json" } },
        Response = new GleapNetworkResponse { Status = 200 }
    };

    [Fact]
    public void Add_SkipsBlacklistedUrls()
    {
        var buffer = new NetworkLogBuffer(100);
        buffer.SetBlacklist(new[] { "gleap.io" });

        buffer.Add(Log("https://api.gleap.io/sessions"));
        buffer.Add(Log("https://api.example.com/x"));

        Assert.Single(buffer.Snapshot());
        Assert.Equal("https://api.example.com/x", buffer.Snapshot()[0].Url);
    }

    [Fact]
    public void Add_StripsIgnoredHeaderProps()
    {
        var buffer = new NetworkLogBuffer(100);
        buffer.SetPropsToIgnore(new[] { "Authorization" });

        buffer.Add(Log("https://api.example.com/x"));

        var headers = (Dictionary<string, string>)buffer.Snapshot()[0].Request.Headers!;
        Assert.False(headers.ContainsKey("Authorization"));
        Assert.True(headers.ContainsKey("Accept"));
    }
}
