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
                Headers = requestHeaders
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
}
