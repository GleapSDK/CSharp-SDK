using System;
using System.Collections.Concurrent;
using GleapSDK.Bridge;

namespace GleapSDK.Omnis;

/// <summary>
/// <see cref="IWebViewChannel"/> for the Omnis OBrowser host. Unlike the WebView2 channel, the .NET side
/// cannot run JavaScript directly — Omnis' OBrowser only calls <em>named</em> html-control methods via
/// <c>$callmethod</c> and delivers page-&gt;host payloads through <c>evControlEvent</c>. This channel
/// therefore bridges the two directions as plain strings the Omnis 4GL glue shuttles across the COM
/// boundary:
/// <list type="bullet">
/// <item><description><b>host → widget</b>: <see cref="ExecuteJavaScript"/> receives the
/// <c>sendMessage(&lt;json&gt;);</c> wrapper produced by <see cref="WebViewBridge"/>. We unwrap the inner
/// JSON payload, queue it, and raise <see cref="OutgoingMessage"/>. The glue forwards each payload to the
/// bridge page via <c>$callmethod("gleapDeliver", payload)</c>, which posts it into the Gleap iframe.</description></item>
/// <item><description><b>widget → host</b>: the bridge page's <c>GleapJSBridge.gleapCallback(json)</c> hands
/// the raw JSON string to Omnis (<c>sendMessageToFatClient</c> → <c>evControlEvent</c>); the glue calls
/// <see cref="PushMessage"/>, which raises <see cref="IWebViewChannel.MessageReceived"/> for Gleap.Core to
/// dispatch.</description></item>
/// </list>
/// Outbound payloads are buffered in a thread-safe queue so the glue can either react to
/// <see cref="OutgoingMessage"/> immediately or drain via <see cref="TryDequeueOutgoing"/> on its own timer
/// (both are supported; polling is the more robust option across Omnis COM-event configurations).
/// <see cref="ExecuteJavaScript"/> can be called from any thread (e.g. the WebSocket receive loop), so all
/// queue access is thread-safe.
/// </summary>
public sealed class OmnisWebViewChannel : IWebViewChannel
{
    private const string SendMessagePrefix = "sendMessage(";

    private readonly ConcurrentQueue<string> _outbound = new ConcurrentQueue<string>();

    /// <inheritdoc />
    public event Action<string>? MessageReceived;

    /// <summary>
    /// Raised on the calling thread for every host→widget payload, immediately after it is queued. The
    /// Omnis glue may subscribe (via the COM event surface) to push payloads to the bridge page without
    /// polling; payloads are still retained in the queue until <see cref="TryDequeueOutgoing"/> drains them.
    /// </summary>
    public event Action<string>? OutgoingMessage;

    /// <summary>
    /// Number of host→widget payloads currently buffered and not yet drained. The glue can read this to
    /// decide whether a <c>$callmethod</c> flush is needed on its next timer tick.
    /// </summary>
    public int PendingOutgoingCount => _outbound.Count;

    /// <inheritdoc />
    public void ExecuteJavaScript(string script)
    {
        if (string.IsNullOrEmpty(script))
        {
            return;
        }

        var payload = UnwrapSendMessage(script);
        _outbound.Enqueue(payload);
        OutgoingMessage?.Invoke(payload);
    }

    /// <summary>
    /// Dequeues the next host→widget payload for delivery to the bridge page, or returns <c>false</c> with
    /// <paramref name="payload"/> set to <c>null</c> when the queue is empty. The Omnis glue loops on this
    /// each timer tick and forwards every payload via <c>$callmethod("gleapDeliver", payload)</c>.
    /// </summary>
    public bool TryDequeueOutgoing(out string? payload)
    {
        if (_outbound.TryDequeue(out var value))
        {
            payload = value;
            return true;
        }

        payload = null;
        return false;
    }

    /// <summary>
    /// Feeds a widget→host message (the raw <c>{name,data}</c> JSON the bridge page received from the Gleap
    /// iframe) into Gleap.Core. Called by the Omnis glue when <c>evControlEvent</c> fires.
    /// </summary>
    public void PushMessage(string json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return;
        }

        MessageReceived?.Invoke(json);
    }

    /// <summary>
    /// Recovers the JSON payload from the <c>sendMessage(&lt;json&gt;);</c> wrapper that
    /// <see cref="WebViewBridge"/> emits. Any other shape is forwarded verbatim so the bridge page can
    /// decide what to do with it.
    /// </summary>
    private static string UnwrapSendMessage(string script)
    {
        var trimmed = script.Trim();
        if (!trimmed.StartsWith(SendMessagePrefix, StringComparison.Ordinal))
        {
            return trimmed;
        }

        var start = SendMessagePrefix.Length;
        var end = trimmed.EndsWith(");", StringComparison.Ordinal)
            ? trimmed.Length - 2
            : trimmed.Length > 0 && trimmed[trimmed.Length - 1] == ')'
                ? trimmed.Length - 1
                : trimmed.Length;

        return end > start ? trimmed.Substring(start, end - start).Trim() : trimmed;
    }
}
