using System.Collections.Generic;
using GleapSDK.Models;

namespace GleapSDK.Data;

/// <summary>Pending file attachments for the next report.</summary>
public sealed class AttachmentStore
{
    /// <summary>Cap on pending attachments, matching iOS's intent. Without it a host loop could queue an
    /// unbounded multipart upload. (iOS's own check is off by one — it tests <c>&gt; 6</c> and so permits
    /// 7; we implement the documented limit.)</summary>
    public const int MaxAttachments = 6;

    private readonly List<GleapAttachment> _attachments = new();

    /// <summary>Queues a file for the next report. Silently ignored once <see cref="MaxAttachments"/> are
    /// pending, like the native SDKs.</summary>
    public void Add(string base64File, string fileName)
    {
        if (_attachments.Count >= MaxAttachments)
        {
            return;
        }
        _attachments.Add(new GleapAttachment { Base64File = base64File, FileName = fileName });
    }

    public void Clear() => _attachments.Clear();

    public IReadOnlyList<GleapAttachment> Snapshot() => new List<GleapAttachment>(_attachments);
}
