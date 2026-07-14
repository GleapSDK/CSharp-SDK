namespace GleapSDK.Http;

/// <summary>One file in a multipart upload: raw bytes + filename + MIME type.</summary>
public sealed class UploadFile
{
    public UploadFile(byte[] bytes, string fileName, string contentType)
    {
        Bytes = bytes;
        FileName = fileName;
        ContentType = contentType;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance", "CA1819:Properties should not return arrays",
        Justification = "File payload is a byte buffer; byte[] is the natural type for HttpContent.")]
    public byte[] Bytes { get; }

    public string FileName { get; }

    public string ContentType { get; }
}
