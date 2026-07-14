using System;
using System.Collections.Generic;

namespace GleapSDK.Events;

/// <summary>Registry of app-supplied event listeners, keyed by Gleap event name.</summary>
public sealed class GleapEventDispatcher
{
    private readonly Dictionary<string, List<Action<object?>>> _listeners = new();

    public void Register(string eventName, Action<object?> handler)
    {
        if (!_listeners.TryGetValue(eventName, out var handlers))
        {
            handlers = new List<Action<object?>>();
            _listeners[eventName] = handlers;
        }
        handlers.Add(handler);
    }

    /// <summary>Removes a previously registered handler for the given event, if present.
    /// No-op when the event or handler is not registered — lets platform hosts (e.g.
    /// <c>GleapMessenger.Dispose</c>) detach without tracking registration state themselves.</summary>
    public void Unregister(string eventName, Action<object?> handler)
    {
        if (_listeners.TryGetValue(eventName, out var handlers))
        {
            handlers.Remove(handler);
        }
    }

    public void Emit(string eventName, object? data = null)
    {
        if (_listeners.TryGetValue(eventName, out var handlers))
        {
            foreach (var handler in handlers.ToArray())
            {
                handler(data);
            }
        }
    }
}
