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
        var reqPayload = request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync().ConfigureAwait(false);

        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        var respBody = response.Content is null
            ? ""
            : await response.Content.ReadAsStringAsync().ConfigureAwait(false);
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
