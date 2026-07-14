using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GleapSDK.Http;

public sealed class HttpTransport : IHttpTransport
{
    private readonly HttpClient _http;

    public HttpTransport(HttpClient? http = null) => _http = http ?? new HttpClient();

    public async Task<HttpResult> SendAsync(
        string method, string url, string? jsonBody,
        IReadOnlyDictionary<string, string> headers, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(new HttpMethod(method), url);
        if (jsonBody != null)
        {
            req.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        }

        foreach (var kv in headers)
        {
            req.Headers.TryAddWithoutValidation(kv.Key, kv.Value);
        }

        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
        return new HttpResult((int)resp.StatusCode, body);
    }

    public async Task<HttpResult> UploadAsync(
        string url, byte[] fileBytes, string fileName, string contentType,
        IReadOnlyDictionary<string, string> headers, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        using var form = new MultipartFormDataContent("BBBOUNDARY");
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        form.Add(fileContent, "file", fileName);
        req.Content = form;

        foreach (var kv in headers)
        {
            req.Headers.TryAddWithoutValidation(kv.Key, kv.Value);
        }

        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
        return new HttpResult((int)resp.StatusCode, body);
    }

    public async Task<HttpResult> UploadManyAsync(
        string url, IReadOnlyList<UploadFile> files,
        IReadOnlyDictionary<string, string> headers, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        using var form = new MultipartFormDataContent("BBBOUNDARY");
        foreach (var file in files)
        {
            var fileContent = new ByteArrayContent(file.Bytes);
            fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(file.ContentType);
            form.Add(fileContent, "file", file.FileName);
        }
        req.Content = form;

        foreach (var kv in headers)
        {
            req.Headers.TryAddWithoutValidation(kv.Key, kv.Value);
        }

        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
        return new HttpResult((int)resp.StatusCode, body);
    }
}
