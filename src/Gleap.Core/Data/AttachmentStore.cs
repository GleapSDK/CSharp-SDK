using System.Collections.Generic;
using GleapSDK.Models;

namespace GleapSDK.Data;

/// <summary>Pending file attachments for the next report.</summary>
public sealed class AttachmentStore
{
    private readonly List<GleapAttachment> _attachments = new();

    public void Add(string base64File, string fileName) =>
        _attachments.Add(new GleapAttachment { Base64File = base64File, FileName = fileName });

    public void Clear() => _attachments.Clear();

    public IReadOnlyList<GleapAttachment> Snapshot() => new List<GleapAttachment>(_attachments);
}
