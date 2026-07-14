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

    /// <summary>Multipart <c>POST</c> of a single file under the form field <c>file</c> (for image uploads
    /// to <c>/uploads/sdk</c>, which the report references by URL).</summary>
    Task<HttpResult> UploadAsync(
        string url, byte[] fileBytes, string fileName, string contentType,
        IReadOnlyDictionary<string, string> headers, CancellationToken ct);

    /// <summary>Multipart <c>POST</c> of several files, each under the repeated form field <c>file</c>
    /// (for <c>/uploads/attachments</c> and <c>/uploads/sdksteps</c>, which return a <c>fileUrls</c> array).</summary>
    Task<HttpResult> UploadManyAsync(
        string url, IReadOnlyList<UploadFile> files,
        IReadOnlyDictionary<string, string> headers, CancellationToken ct);
}
