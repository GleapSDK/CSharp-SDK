using System.Collections.Generic;
using GleapSDK.Serialization;

namespace GleapSDK.Bridge;

/// <summary>
/// Speaks the Gleap web-widget protocol over an <see cref="IWebViewChannel"/>.
/// Outgoing messages are queued until the widget sends "ping" (handshake),
/// then flushed. Incoming dispatch is added in a later task.
/// </summary>
public sealed partial class WebViewBridge
{
    private readonly IWebViewChannel _channel;
    private readonly IJsonSerializer _json;
    private readonly Queue<GleapBridgeMessage> _queue = new();
    private bool _connected;

    public WebViewBridge(IWebViewChannel channel, IJsonSerializer json)
    {
        _channel = channel;
        _json = json;
        // Wrapped in a lambda (not a method group): OnMessageReceived is a partial
        // method whose body is added in Task 7. A method group of an unimplemented
        // partial method does not compile; a call inside a lambda is simply elided.
        _channel.MessageReceived += json => OnMessageReceived(json);
    }

    public bool IsConnected => _connected;

    /// <summary>Send a message, queuing it until the handshake completes.</summary>
    public void Send(GleapBridgeMessage message)
    {
        if (!_connected)
        {
            _queue.Enqueue(message);
            return;
        }
        Execute(message);
    }

    private void Execute(GleapBridgeMessage message)
    {
        var json = _json.Serialize(message);
        _channel.ExecuteJavaScript("sendMessage(" + json + ");");
    }

    private void FlushQueue()
    {
        while (_queue.Count > 0)
            Execute(_queue.Dequeue());
    }

    // Incoming dispatch is implemented in the partial in WebViewBridge.Incoming.cs (Task 7).
    partial void OnMessageReceived(string json);

    // Test hook — real connection happens on "ping" (Task 7).
    internal void MarkConnectedForTest()
    {
        _connected = true;
        FlushQueue();
    }
}
