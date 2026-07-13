using System.Text.Json;
using GleapSDK.Outbound;

namespace Gleap.Core.Tests;

public class OutboundModelTests
{
    [Fact]
    public void OutboundAction_ExposesActionTypeAndOutboundId()
    {
        var action = new OutboundAction("survey", "ob1", default);

        Assert.Equal("survey", action.ActionType);
        Assert.Equal("ob1", action.OutboundId);
    }

    [Fact]
    public void PingResponse_ExposesActionsAndUnreadCount()
    {
        var action = new OutboundAction("survey", "ob1", default(JsonElement));

        var response = new PingResponse(new[] { action }, 3);

        Assert.Equal(3, response.UnreadCount);
        Assert.Single(response.Actions);
    }
}
