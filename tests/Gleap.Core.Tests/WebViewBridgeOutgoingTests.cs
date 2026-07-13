using Gleap.Core.Tests.Fakes;
using GleapSDK.Bridge;
using GleapSDK.Serialization;
using Xunit;

namespace Gleap.Core.Tests;

public class WebViewBridgeOutgoingTests
{
    private static WebViewBridge NewBridge(FakeWebViewChannel ch) =>
        new WebViewBridge(ch, new SystemTextJsonSerializer());

    [Fact]
    public void Send_BeforeConnected_IsQueued_NotExecuted()
    {
        var ch = new FakeWebViewChannel();
        var bridge = NewBridge(ch);

        bridge.Send(new GleapBridgeMessage { Name = "open-conversations" });

        Assert.Empty(ch.ExecutedScripts);
    }

    [Fact]
    public void Send_WrapsInSendMessageCall()
    {
        var ch = new FakeWebViewChannel();
        var bridge = NewBridge(ch);
        bridge.MarkConnectedForTest();

        bridge.Send(new GleapBridgeMessage { Name = "open-news" });

        Assert.Single(ch.ExecutedScripts);
        Assert.StartsWith("sendMessage(", ch.ExecutedScripts[0]);
        Assert.EndsWith(");", ch.ExecutedScripts[0]);
        Assert.Contains("\"name\":\"open-news\"", ch.ExecutedScripts[0]);
    }
}
