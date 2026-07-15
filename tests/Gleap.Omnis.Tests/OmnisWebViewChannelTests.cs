using GleapSDK.Omnis;

namespace Gleap.Omnis.Tests;

/// <summary>
/// Covers the OBrowser transport: unwrapping the <c>sendMessage(&lt;json&gt;);</c> wrapper that
/// Gleap.Core emits into the bare payload the Omnis glue forwards, FIFO queueing, and the inbound
/// <c>PushMessage</c> path. This is the one piece of the Omnis layer with real logic (the rest is thin
/// delegation to Gleap.Core or Win32/COM plumbing), so it carries the unit tests.
/// </summary>
public sealed class OmnisWebViewChannelTests
{
    [Fact]
    public void ExecuteJavaScript_UnwrapsSendMessagePayload()
    {
        var channel = new OmnisWebViewChannel();

        channel.ExecuteJavaScript("sendMessage({\"name\":\"config-update\",\"data\":{}});");

        Assert.True(channel.TryDequeueOutgoing(out var payload));
        Assert.Equal("{\"name\":\"config-update\",\"data\":{}}", payload);
    }

    [Fact]
    public void ExecuteJavaScript_RaisesOutgoingMessageWithUnwrappedPayload()
    {
        var channel = new OmnisWebViewChannel();
        var received = new List<string>();
        channel.OutgoingMessage += received.Add;

        channel.ExecuteJavaScript("sendMessage({\"name\":\"ping\"});");

        Assert.Single(received);
        Assert.Equal("{\"name\":\"ping\"}", received[0]);
    }

    [Fact]
    public void ExecuteJavaScript_WithoutTrailingSemicolon_StillUnwraps()
    {
        var channel = new OmnisWebViewChannel();

        channel.ExecuteJavaScript("sendMessage({\"name\":\"open\"})");

        Assert.True(channel.TryDequeueOutgoing(out var payload));
        Assert.Equal("{\"name\":\"open\"}", payload);
    }

    [Fact]
    public void ExecuteJavaScript_PreservesFifoOrder()
    {
        var channel = new OmnisWebViewChannel();

        channel.ExecuteJavaScript("sendMessage({\"n\":1});");
        channel.ExecuteJavaScript("sendMessage({\"n\":2});");
        channel.ExecuteJavaScript("sendMessage({\"n\":3});");

        Assert.Equal(3, channel.PendingOutgoingCount);
        channel.TryDequeueOutgoing(out var first);
        channel.TryDequeueOutgoing(out var second);
        channel.TryDequeueOutgoing(out var third);
        Assert.Equal("{\"n\":1}", first);
        Assert.Equal("{\"n\":2}", second);
        Assert.Equal("{\"n\":3}", third);
    }

    [Fact]
    public void ExecuteJavaScript_UnknownShape_ForwardsVerbatim()
    {
        var channel = new OmnisWebViewChannel();

        channel.ExecuteJavaScript("someOtherCall(42);");

        Assert.True(channel.TryDequeueOutgoing(out var payload));
        Assert.Equal("someOtherCall(42);", payload);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void ExecuteJavaScript_EmptyOrNull_QueuesNothing(string? script)
    {
        var channel = new OmnisWebViewChannel();

        channel.ExecuteJavaScript(script!);

        Assert.Equal(0, channel.PendingOutgoingCount);
        Assert.False(channel.TryDequeueOutgoing(out _));
    }

    [Fact]
    public void TryDequeueOutgoing_WhenEmpty_ReturnsFalseAndNull()
    {
        var channel = new OmnisWebViewChannel();

        Assert.False(channel.TryDequeueOutgoing(out var payload));
        Assert.Null(payload);
    }

    [Fact]
    public void PushMessage_RaisesMessageReceivedWithRawJson()
    {
        var channel = new OmnisWebViewChannel();
        string? received = null;
        channel.MessageReceived += json => received = json;

        channel.PushMessage("{\"name\":\"ping\"}");

        Assert.Equal("{\"name\":\"ping\"}", received);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void PushMessage_EmptyOrNull_DoesNotRaise(string? json)
    {
        var channel = new OmnisWebViewChannel();
        var raised = false;
        channel.MessageReceived += _ => raised = true;

        channel.PushMessage(json!);

        Assert.False(raised);
    }
}
