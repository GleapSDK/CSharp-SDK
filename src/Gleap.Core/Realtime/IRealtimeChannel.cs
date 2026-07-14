using System;

namespace GleapSDK.Realtime;

/// <summary>
/// Real-time channel the backend uses to receive server pushes (outbound actions + unread count) instead
/// of waiting for the outbound poll. The production implementation is <see cref="GleapWebSocket"/>
/// (<c>wss://ws.gleap.io</c>); tests inject a fake. Leaving <c>ManagedBackend.Dependencies.Realtime</c>
/// null disables realtime — the backend then relies on the poll alone (its safety-net fallback).
/// </summary>
public interface IRealtimeChannel : IDisposable
{
    /// <summary>Raised with the raw JSON text of each inbound frame (on a background thread).</summary>
    event Action<string>? MessageReceived;

    /// <summary>Whether a live connection is currently established (drives the ping <c>ws</c> flag).</summary>
    bool IsConnected { get; }

    /// <summary>(Re)connect to <paramref name="url"/>. Safe to call again to switch identity: it drops any
    /// existing connection and connects to the new URL, auto-reconnecting if the connection drops.</summary>
    void Connect(string url);

    /// <summary>Stop connecting and close any live socket.</summary>
    void Disconnect();
}
