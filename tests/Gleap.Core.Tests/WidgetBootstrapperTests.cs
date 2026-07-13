using System.Linq;
using Gleap.Core.Tests.Fakes;
using GleapSDK.Bridge;
using GleapSDK.Serialization;
using GleapSDK.Session;
using Xunit;

namespace Gleap.Core.Tests;

public class WidgetBootstrapperTests
{
    [Fact]
    public void OnPing_PushesBootstrapSequence_InOrder()
    {
        var ch = new FakeWebViewChannel();
        var bridge = new WebViewBridge(ch, new SystemTextJsonSerializer());
        var state = new SessionSnapshot
        {
            SdkKey = "key", ApiUrl = "https://api.gleap.io",
            GleapId = "g1", GleapHash = "h1",
            FlowConfigJson = "{\"color\":\"#fff\"}", ProjectActionsJson = "{\"x\":1}",
            Language = "en"
        };
        _ = new WidgetBootstrapper(bridge, () => state);

        ch.SimulateIncoming("{\"name\":\"ping\"}");

        var names = ch.ExecutedScripts;
        int Idx(string n) => names.FindIndex(s => s.Contains("\"name\":\"" + n + "\""));
        Assert.True(Idx("widget-status-update") >= 0);
        Assert.True(Idx("config-update") > Idx("widget-status-update"));
        Assert.True(Idx("session-update") > Idx("config-update"));
    }

    [Fact]
    public void SessionUpdate_CarriesSessionDataAndSdkKey()
    {
        var ch = new FakeWebViewChannel();
        var bridge = new WebViewBridge(ch, new SystemTextJsonSerializer());
        var state = new SessionSnapshot
        {
            SdkKey = "key-xyz", ApiUrl = "https://api.gleap.io",
            GleapId = "gid", GleapHash = "gh",
            FlowConfigJson = "{}", ProjectActionsJson = "{}", Language = "en"
        };
        _ = new WidgetBootstrapper(bridge, () => state);

        ch.SimulateIncoming("{\"name\":\"ping\"}");

        var sessionMsg = ch.ExecutedScripts.First(s => s.Contains("session-update"));
        Assert.Contains("\"sdkKey\":\"key-xyz\"", sessionMsg);
        Assert.Contains("\"gleapId\":\"gid\"", sessionMsg);
    }

    [Fact]
    public void QueuedNavCommand_FlushesAfterBootstrap()
    {
        var ch = new FakeWebViewChannel();
        var bridge = new WebViewBridge(ch, new SystemTextJsonSerializer());
        var state = new SessionSnapshot
        {
            SdkKey = "key", ApiUrl = "https://api.gleap.io", GleapId = "g", GleapHash = "h",
            FlowConfigJson = "{}", ProjectActionsJson = "{}", Language = "en"
        };
        _ = new WidgetBootstrapper(bridge, () => state);
        bridge.Send(WidgetCommands.OpenNews(showBackButton: true)); // queued pre-ping

        ch.SimulateIncoming("{\"name\":\"ping\"}");

        int cfg = ch.ExecutedScripts.FindIndex(s => s.Contains("config-update"));
        int news = ch.ExecutedScripts.FindIndex(s => s.Contains("open-news"));
        Assert.True(news > cfg, "nav command must flush after bootstrap");
    }
}
