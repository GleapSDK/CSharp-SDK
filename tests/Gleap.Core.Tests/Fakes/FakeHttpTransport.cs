using GleapSDK.Http;

namespace Gleap.Core.Tests.Fakes;

public sealed class FakeHttpTransport : IHttpTransport
{
    public sealed record Call(string Method, string Url, string? Body, IReadOnlyDictionary<string, string> Headers);

    public List<Call> Calls { get; } = new();
    public Queue<HttpResult> Responses { get; } = new();

    public Task<HttpResult> SendAsync(
        string method, string url, string? jsonBody,
        IReadOnlyDictionary<string, string> headers, CancellationToken ct)
    {
        Calls.Add(new Call(method, url, jsonBody, headers));
        var result = Responses.Count > 0 ? Responses.Dequeue() : new HttpResult(200, "{}");
        return Task.FromResult(result);
    }
}
