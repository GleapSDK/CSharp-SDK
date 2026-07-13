using Gleap.Core.Tests.Fakes;
using GleapSDK.Bridge;
using GleapSDK.Serialization;

namespace Gleap.Core.Tests;

public class WebViewBridgeCollectDataTests
{
    [Fact]
    public void CollectTicketData_RaisesEvent()
    {
        var ch = new FakeWebViewChannel();
        var bridge = new WebViewBridge(ch, new SystemTextJsonSerializer());
        var raised = false;
        bridge.CollectTicketDataRequested += () => raised = true;

        ch.SimulateIncoming("{\"name\":\"collect-ticket-data\"}");

        Assert.True(raised);
    }
}
