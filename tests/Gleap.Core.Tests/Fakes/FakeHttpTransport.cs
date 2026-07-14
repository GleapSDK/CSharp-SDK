using GleapSDK.Http;

namespace Gleap.Core.Tests.Fakes;

public sealed class FakeHttpTransport : IHttpTransport
{
    public sealed record Call(string Method, string Url, string? Body, IReadOnlyDictionary<string, string> Headers);
    public sealed record Upload(string Url, byte[] File, string FileName, string ContentType, IReadOnlyDictionary<string, string> Headers);
    public sealed record MultiUpload(string Url, IReadOnlyList<UploadFile> Files, IReadOnlyDictionary<string, string> Headers);

    public List<Call> Calls { get; } = new();
    public List<Upload> Uploads { get; } = new();
    public List<MultiUpload> MultiUploads { get; } = new();
    public Queue<HttpResult> Responses { get; } = new();
    public Queue<HttpResult> UploadResponses { get; } = new();
    public Queue<HttpResult> MultiUploadResponses { get; } = new();

    public Task<HttpResult> SendAsync(
        string method, string url, string? jsonBody,
        IReadOnlyDictionary<string, string> headers, CancellationToken ct)
    {
        Calls.Add(new Call(method, url, jsonBody, headers));
        var result = Responses.Count > 0 ? Responses.Dequeue() : new HttpResult(200, "{}");
        return Task.FromResult(result);
    }

    public Task<HttpResult> UploadAsync(
        string url, byte[] fileBytes, string fileName, string contentType,
        IReadOnlyDictionary<string, string> headers, CancellationToken ct)
    {
        Uploads.Add(new Upload(url, fileBytes, fileName, contentType, headers));
        var result = UploadResponses.Count > 0
            ? UploadResponses.Dequeue()
            : new HttpResult(200, "{\"fileUrl\":\"https://uploads.gleap.io/uploaded.png\"}");
        return Task.FromResult(result);
    }

    public Task<HttpResult> UploadManyAsync(
        string url, IReadOnlyList<UploadFile> files,
        IReadOnlyDictionary<string, string> headers, CancellationToken ct)
    {
        MultiUploads.Add(new MultiUpload(url, files, headers));
        HttpResult result;
        if (MultiUploadResponses.Count > 0)
        {
            result = MultiUploadResponses.Dequeue();
        }
        else
        {
            // Default: one fake URL per uploaded file, in order.
            var urls = string.Join(",", files.Select((_, i) => $"\"https://uploads.gleap.io/u{i}.png\""));
            result = new HttpResult(200, "{\"fileUrls\":[" + urls + "]}");
        }
        return Task.FromResult(result);
    }
}
