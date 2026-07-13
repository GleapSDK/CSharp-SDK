using System.Collections.Generic;
using Gleap.Core.Tests.Fakes;
using Xunit;

namespace Gleap.Core.Tests;

public class FakeWebViewChannelTests
{
    [Fact]
    public void RecordsExecutedScripts()
    {
        var ch = new FakeWebViewChannel();
        ch.ExecuteJavaScript("sendMessage({});");
        Assert.Single(ch.ExecutedScripts);
    }

    [Fact]
    public void RaisesMessageReceived_WhenPageSends()
    {
        var ch = new FakeWebViewChannel();
        var got = new List<string>();
        ch.MessageReceived += got.Add;

        ch.SimulateIncoming("{\"name\":\"ping\"}");

        Assert.Equal("{\"name\":\"ping\"}", got[0]);
    }
}
