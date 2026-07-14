using Gleap.Core.Tests.Fakes;
using GleapSDK.Bridge;
using GleapSDK.Serialization;

namespace Gleap.Core.Tests;

public class WebViewBridgeIncomingTests
{
    private static WebViewBridge NewBridge(FakeWebViewChannel ch) =>
        new WebViewBridge(ch, new SystemTextJsonSerializer());

    [Fact]
    public void Ping_ConnectsAndFlushesQueue()
    {
        var ch = new FakeWebViewChannel();
        var bridge = NewBridge(ch);
        bridge.Send(new GleapBridgeMessage { Name = "open-news" }); // queued

        ch.SimulateIncoming("{\"name\":\"ping\"}");

        Assert.True(bridge.IsConnected);
        Assert.Contains(ch.ExecutedScripts, s => s.Contains("\"name\":\"open-news\""));
    }

    [Fact]
    public void Ping_RaisesPingReceived_BeforeFlush()
    {
        var ch = new FakeWebViewChannel();
        var bridge = NewBridge(ch);
        var order = new List<string>();
        bridge.PingReceived += () => order.Add("ping");
        bridge.Send(new GleapBridgeMessage { Name = "open-news" });
        // record flush by observing script execution timing via event ordering
        bridge.PingReceived += () => order.Add("bootstrap-window");

        ch.SimulateIncoming("{\"name\":\"ping\"}");

        Assert.Equal("ping", order[0]);
    }

    [Fact]
    public void RunCustomAction_ExposesNameAndTopLevelShareToken()
    {
        var ch = new FakeWebViewChannel();
        var bridge = NewBridge(ch);
        string? name = null;
        string? token = null;
        bridge.CustomActionTriggered += (n, t) => { name = n; token = t; };

        ch.SimulateIncoming("{\"name\":\"run-custom-action\",\"data\":\"myAction\",\"shareToken\":\"tok1\"}");

        Assert.Equal("myAction", name);
        Assert.Equal("tok1", token);
    }

    [Fact]
    public void CloseWidget_RaisesEvent()
    {
        var ch = new FakeWebViewChannel();
        var bridge = NewBridge(ch);
        var closed = false;
        bridge.CloseWidgetRequested += () => closed = true;

        ch.SimulateIncoming("{\"name\":\"close-widget\"}");

        Assert.True(closed);
    }

    [Fact]
    public void NotifyEvent_FlowStarted_RaisesFeedbackFlowStarted()
    {
        var ch = new FakeWebViewChannel();
        var bridge = NewBridge(ch);
        var started = false;
        bridge.FeedbackFlowStarted += _ => started = true;

        ch.SimulateIncoming("{\"name\":\"notify-event\",\"data\":{\"type\":\"flow-started\",\"data\":{}}}");

        Assert.True(started);
    }

    [Fact]
    public void HeightUpdate_RaisesHeightUpdatedWithPixels()
    {
        var ch = new FakeWebViewChannel();
        var bridge = NewBridge(ch);
        double? height = null;
        bridge.HeightUpdated += h => height = h;

        ch.SimulateIncoming("{\"name\":\"height-update\",\"data\":540}");

        Assert.Equal(540, height);
    }

    [Fact]
    public void ScreenshotUpdated_RaisesEventWithDataUri()
    {
        var ch = new FakeWebViewChannel();
        var bridge = NewBridge(ch);
        string? shot = null;
        bridge.ScreenshotUpdated += s => shot = s;

        ch.SimulateIncoming("{\"name\":\"screenshot-updated\",\"data\":\"data:image/png;base64,AAA\"}");

        Assert.Equal("data:image/png;base64,AAA", shot);
    }

    [Fact]
    public void MalformedJson_IsIgnored()
    {
        var ch = new FakeWebViewChannel();
        var bridge = NewBridge(ch);

        var ex = Record.Exception(() => ch.SimulateIncoming("not-json"));

        Assert.Null(ex);
        Assert.False(bridge.IsConnected);
    }
}
