namespace GleapSDK.Models;

/// <summary>A base64-encoded file attached to a report.</summary>
public sealed class GleapAttachment
{
    public string Base64File { get; set; } = "";
    public string FileName { get; set; } = "";
}
