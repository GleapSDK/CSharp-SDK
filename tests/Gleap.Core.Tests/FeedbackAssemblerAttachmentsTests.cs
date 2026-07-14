using System.Text.Json;
using GleapSDK.Feedback;

namespace Gleap.Core.Tests;

public class FeedbackAssemblerAttachmentsTests
{
    // Attachments reach the assembler already uploaded — as {url, name, type} entries, never inline data.
    private static IReadOnlyList<IReadOnlyDictionary<string, object?>> Uploaded() => new IReadOnlyDictionary<string, object?>[]
    {
        new Dictionary<string, object?>
        {
            ["url"] = "https://uploads.gleap.io/a.txt",
            ["name"] = "a.txt",
            ["type"] = "text/plain"
        }
    };

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
            attachments: Uploaded());

        var attachments = Assert.IsAssignableFrom<IReadOnlyList<IReadOnlyDictionary<string, object?>>>(result["attachments"]);
        Assert.Single(attachments);
        var serialized = JsonSerializer.Serialize(attachments);
        Assert.Contains("a.txt", serialized);
        Assert.Contains("uploads.gleap.io/a.txt", serialized);
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
            attachments: Uploaded());

        Assert.False(result.ContainsKey("attachments"));
    }
}
