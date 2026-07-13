using System.Text.Json;
using GleapSDK.Feedback;
using GleapSDK.Models;

namespace Gleap.Core.Tests;

public class FeedbackAssemblerAttachmentsTests
{
    [Fact]
    public void Build_IncludesAttachments_WhenNotExcluded()
    {
        var result = FeedbackAssembler.Build(
            new Dictionary<string, object?>(),
            new Dictionary<string, object?>(),
            "BUG",
            null,
            false,
            excludeKeys: new HashSet<string>(),
            attachments: new[] { new GleapAttachment { Base64File = "Zm9v", FileName = "a.txt" } });

        var attachments = Assert.IsAssignableFrom<System.Collections.Generic.IReadOnlyList<GleapAttachment>>(result["attachments"]);
        Assert.Single(attachments);
        Assert.Contains("a.txt", JsonSerializer.Serialize(attachments));
    }

    [Fact]
    public void Build_OmitsAttachments_WhenExcluded()
    {
        var result = FeedbackAssembler.Build(
            new Dictionary<string, object?>(),
            new Dictionary<string, object?>(),
            "BUG",
            null,
            false,
            excludeKeys: new HashSet<string> { "attachments" },
            attachments: new[] { new GleapAttachment { Base64File = "Zm9v", FileName = "a.txt" } });

        Assert.False(result.ContainsKey("attachments"));
    }
}
