using Gleap.Core.Tests.Fakes;
using GleapSDK.Bridge;
using GleapSDK.Serialization;

namespace Gleap.Core.Tests;

public class WebViewBridgeToolExecutionTests
{
    [Fact]
    public void ToolExecution_RaisesEvent()
    {
        var ch = new FakeWebViewChannel();
        var bridge = new WebViewBridge(ch, new SystemTextJsonSerializer());
        var raised = false;
        bridge.ToolExecutionRequested += _ => raised = true;

        ch.SimulateIncoming("{\"name\":\"tool-execution\",\"data\":{\"tool\":\"x\"}}");

        Assert.True(raised);
    }
}
