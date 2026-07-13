using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace GleapSDK.Http;

/// <summary>Minimal HTTP seam so the API layer is testable without a real network.</summary>
public interface IHttpTransport
{
    Task<HttpResult> SendAsync(
        string method, string url, string? jsonBody,
        IReadOnlyDictionary<string, string> headers, CancellationToken ct);
}
