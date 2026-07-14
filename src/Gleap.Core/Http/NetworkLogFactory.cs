using System.Collections.Generic;
using GleapSDK.Models;

namespace GleapSDK.Http;

/// <summary>
/// Builds <see cref="GleapNetworkLog"/> entries, applying the native SDKs' safety guard:
/// payloads/responses over 1,000,000 bytes are replaced with a sentinel rather than logged.
/// </summary>
public static class NetworkLogFactory
{
    private const int MaxBodyLength = 1_000_000;

    public static GleapNetworkLog Build(
        string type, string url, string date, double durationMs,
        int statusCode, string statusText,
        string? requestPayload, IReadOnlyDictionary<string, string>? requestHeaders,
        string? responseBody)
    {
        return new GleapNetworkLog
        {
            Type = type,
            Url = url,
            Date = date,
            Duration = durationMs,
            Success = statusCode is >= 200 and < 300,
            Request = new GleapNetworkRequest
            {
                Payload = Guard(requestPayload, "<payload_too_large>"),
                Headers = CopyHeaders(requestHeaders)
            },
            Response = new GleapNetworkResponse
            {
                Status = statusCode,
                StatusText = statusText,
                ResponseText = Guard(responseBody, "<response_too_large>") ?? ""
            }
        };
    }

    private static string? Guard(string? body, string sentinel)
    {
        if (body is null)
        {
            return null;
        }
        return body.Length > MaxBodyLength ? sentinel : body;
    }

    /// <summary>
    /// Copies the caller's header map into a concrete <see cref="Dictionary{TKey, TValue}"/> so
    /// <see cref="Collection.NetworkLogBuffer"/> can mutate it in place (header redaction) without
    /// touching the caller's own dictionary, and so the stored value's runtime type is exactly
    /// <c>Dictionary&lt;string, string&gt;</c> — the shape the redaction cast expects.
    /// </summary>
    private static Dictionary<string, string>? CopyHeaders(IReadOnlyDictionary<string, string>? requestHeaders)
    {
        if (requestHeaders is null)
        {
            return null;
        }
        var copy = new Dictionary<string, string>();
        foreach (var kv in requestHeaders)
        {
            copy[kv.Key] = kv.Value;
        }
        return copy;
    }
}
