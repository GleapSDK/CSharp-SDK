using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using GleapSDK.Models;

namespace GleapSDK.Collection;

/// <summary>
/// Bounded buffer of network logs. Drops entries whose URL contains any blacklisted substring, and strips
/// ignored keys from the request headers, the request payload and the response body.
/// </summary>
public sealed class NetworkLogBuffer
{
    /// <summary>Never log the SDK's own traffic — it carries the project token and session headers, and
    /// would otherwise end up inside the tickets it creates. Always applied, on top of any configured
    /// blacklist (the JS SDK ships the same default).</summary>
    private static readonly string[] AlwaysBlocked = { "gleap.io" };

    private readonly RingBuffer<GleapNetworkLog> _buffer;
    private IReadOnlyList<string> _blacklist = Array.Empty<string>();
    private IReadOnlyList<string> _propsToIgnore = Array.Empty<string>();

    public NetworkLogBuffer(int capacity) => _buffer = new RingBuffer<GleapNetworkLog>(capacity);

    public void SetBlacklist(IReadOnlyList<string> blacklist) => _blacklist = blacklist;

    public void SetPropsToIgnore(IReadOnlyList<string> propsToIgnore) => _propsToIgnore = propsToIgnore;

    public bool Enabled { get; set; } = true;

    public void Add(GleapNetworkLog log)
    {
        if (!Enabled || IsBlacklisted(log.Url))
        {
            return;
        }

        // Redact before buffering, so an ignored key can never be read back out of the buffer.
        StripHeaders(log.Request.Headers as IDictionary<string, string>);
        if (log.Request.Payload is string payload)
        {
            log.Request.Payload = StripJson(payload);
        }
        log.Response.ResponseText = StripJson(log.Response.ResponseText) ?? "";

        _buffer.Add(log);
    }

    private bool IsBlacklisted(string url)
    {
        foreach (var blocked in AlwaysBlocked)
        {
            if (url.Contains(blocked))
            {
                return true;
            }
        }
        foreach (var blocked in _blacklist)
        {
            if (url.Contains(blocked))
            {
                return true;
            }
        }
        return false;
    }

    private void StripHeaders(IDictionary<string, string>? headers)
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

    /// <summary>
    /// Removes every ignored key from a JSON body. Non-JSON bodies (and bodies with nothing to strip) are
    /// returned untouched. Stripping recurses into nested objects and arrays: the point of the setting is
    /// that a named property never reaches Gleap, and a password nested one level down is exactly as
    /// sensitive as a top-level one.
    /// </summary>
    private string? StripJson(string? body)
    {
        if (string.IsNullOrEmpty(body) || _propsToIgnore.Count == 0)
        {
            return body;
        }
        try
        {
            var node = JsonNode.Parse(body!);
            if (node is null)
            {
                return body;
            }
            StripNode(node);
            return node.ToJsonString();
        }
        catch (JsonException)
        {
            return body;   // not JSON (form encoded, plain text, a sentinel, …) — nothing to strip
        }
    }

    private void StripNode(JsonNode node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var prop in _propsToIgnore)
                {
                    obj.Remove(prop);
                }
                foreach (var child in obj.ToList())
                {
                    if (child.Value is not null)
                    {
                        StripNode(child.Value);
                    }
                }
                break;
            case JsonArray arr:
                foreach (var item in arr.ToList())
                {
                    if (item is not null)
                    {
                        StripNode(item);
                    }
                }
                break;
        }
    }

    public IReadOnlyList<GleapNetworkLog> Snapshot() => _buffer.Snapshot();

    public void Clear() => _buffer.Clear();
}
