using GleapSDK.Events;

namespace Gleap.Core.Tests;

public class GleapEventDispatcherTests
{
    [Fact]
    public void Emit_InvokesRegisteredHandlers()
    {
        var dispatcher = new GleapEventDispatcher();
        var first = false;
        var second = false;
        dispatcher.Register("widgetOpened", _ => first = true);
        dispatcher.Register("widgetOpened", _ => second = true);

        dispatcher.Emit("widgetOpened", null);

        Assert.True(first);
        Assert.True(second);
    }

    [Fact]
    public void Emit_PassesData()
    {
        var dispatcher = new GleapEventDispatcher();
        object? captured = null;
        dispatcher.Register("x", data => captured = data);

        dispatcher.Emit("x", "payload");

        Assert.Equal("payload", captured);
    }

    [Fact]
    public void Emit_UnknownEvent_NoOp()
    {
        var dispatcher = new GleapEventDispatcher();

        var ex = Record.Exception(() => dispatcher.Emit("nope", null));

        Assert.Null(ex);
    }

    [Fact]
    public void Unregister_StopsDelivery()
    {
        var dispatcher = new GleapEventDispatcher();
        var called = false;
        void Handler(object? data) => called = true;
        dispatcher.Register("x", Handler);

        dispatcher.Unregister("x", Handler);
        dispatcher.Emit("x", null);

        Assert.False(called);
    }
}
