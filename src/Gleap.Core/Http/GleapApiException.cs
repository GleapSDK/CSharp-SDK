using System;

namespace GleapSDK.Http;

/// <summary>Thrown when a Gleap API request fails (non-success status or unparseable body).</summary>
public sealed class GleapApiException : Exception
{
    public int StatusCode { get; }

    public GleapApiException(int statusCode, string message) : base(message) => StatusCode = statusCode;
}
