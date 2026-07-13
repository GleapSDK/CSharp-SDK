using System;
using System.Collections.Generic;
using GleapSDK.Models;

namespace GleapSDK.Collection;

/// <summary>
/// Bounded buffer of network logs. Drops entries whose URL contains any blacklisted
/// substring, and strips ignored keys from request/response header maps.
/// </summary>
public sealed class NetworkLogBuffer
{
    private readonly RingBuffer<GleapNetworkLog> _buffer;
    private IReadOnlyList<string> _blacklist = Array.Empty<string>();
    private IReadOnlyList<string> _propsToIgnore = Array.Empty<string>();

    public NetworkLogBuffer(int capacity) => _buffer = new RingBuffer<GleapNetworkLog>(capacity);

    public void SetBlacklist(IReadOnlyList<string> blacklist) => _blacklist = blacklist;

    public void SetPropsToIgnore(IReadOnlyList<string> propsToIgnore) => _propsToIgnore = propsToIgnore;

    public bool Enabled { get; set; } = true;

    public void Add(GleapNetworkLog log)
    {
        if (!Enabled)
        {
            return;
        }

        foreach (var blocked in _blacklist)
        {
            if (log.Url.Contains(blocked))
            {
                return;
            }
        }

        Strip(log.Request.Headers as IDictionary<string, object>);
        _buffer.Add(log);
    }

    private void Strip(IDictionary<string, object>? headers)
    {
        if (headers is null)
        {
            return;
        }
        foreach (var prop in _propsToIgnore)
        {
            headers.Remove(prop);
        }
    }

    public IReadOnlyList<GleapNetworkLog> Snapshot() => _buffer.Snapshot();

    public void Clear() => _buffer.Clear();
}
