using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using GleapSDK.Collection;
using GleapSDK.Time;

namespace GleapSDK.Http;

/// <summary>
/// Opt-in <see cref="DelegatingHandler"/> the host app adds to its <see cref="HttpClient"/>
/// so outbound traffic is captured into the Gleap network log. Only traffic routed through
/// this handler is captured (there is no global interception in managed C#).
/// </summary>
public sealed class GleapHttpHandler : DelegatingHandler
{
    /// <summary>Bodies above this are logged as a sentinel instead of their content — and when the length
    /// is declared up front, they are never read into memory at all. Mirrors the native SDKs' guard and
    /// keeps <see cref="NetworkLogFactory"/>'s cap.</summary>
    private const long MaxBodyLength = 1_000_000;

    private readonly NetworkLogBuffer _buffer;
    private readonly IClock _clock;

    public GleapHttpHandler(NetworkLogBuffer buffer, IClock clock)
    {
        _buffer = buffer;
        _clock = clock;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var start = _clock.UtcNow;
        var date = start.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
        var reqPayload = await ReadForLogAsync(request.Content, "<payload_too_large>").ConfigureAwait(false);

        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        var respBody = await ReadForLogAsync(response.Content, "<response_too_large>").ConfigureAwait(false);
        var duration = (_clock.UtcNow - start).TotalMilliseconds;

        _buffer.Add(NetworkLogFactory.Build(
            type: request.Method.Method,
            url: request.RequestUri?.ToString() ?? "",
            date: date,
            durationMs: duration,
            statusCode: (int)response.StatusCode,
            statusText: response.ReasonPhrase ?? "",
            requestPayload: reqPayload,
            requestHeaders: HeaderMap(request.Headers),
            responseBody: respBody));

        return response;
    }

    /// <summary>
    /// Reads a body for the network log without ever materializing one we would only throw away:
    /// non-text content is not logged at all (matching the native SDKs, which record text bodies only),
    /// and a declared <c>Content-Length</c> over the cap short-circuits to the sentinel before the body is
    /// touched. The post-read length check is the backstop for chunked bodies that declare no length.
    /// Reading via <c>ReadAsStringAsync</c> buffers the content, so the caller can still consume it.
    /// </summary>
    private static async Task<string?> ReadForLogAsync(HttpContent? content, string tooLargeSentinel)
    {
        if (content is null)
        {
            return null;
        }
        if (!IsTextual(content.Headers.ContentType?.MediaType))
        {
            return null;
        }
        var declaredLength = content.Headers.ContentLength;
        if (declaredLength.HasValue && declaredLength.Value > MaxBodyLength)
        {
            return tooLargeSentinel;
        }
        var text = await content.ReadAsStringAsync().ConfigureAwait(false);
        return text.Length > MaxBodyLength ? tooLargeSentinel : text;
    }

    /// <summary>Whether a media type carries text we can usefully log (binary payloads are skipped).</summary>
    private static bool IsTextual(string? mediaType)
    {
        if (string.IsNullOrEmpty(mediaType))
        {
            return false;
        }
        if (mediaType!.StartsWith("text/", StringComparison.OrdinalIgnoreCase)
            || mediaType.EndsWith("+json", StringComparison.OrdinalIgnoreCase)
            || mediaType.EndsWith("+xml", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        return mediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase)
            || mediaType.Equals("application/xml", StringComparison.OrdinalIgnoreCase)
            || mediaType.Equals("application/javascript", StringComparison.OrdinalIgnoreCase)
            || mediaType.Equals("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase);
    }

    private static Dictionary<string, string> HeaderMap(System.Net.Http.Headers.HttpHeaders headers)
    {
        var map = new Dictionary<string, string>();
        foreach (var header in headers)
        {
            map[header.Key] = string.Join(", ", header.Value);
        }
        return map;
    }
}
