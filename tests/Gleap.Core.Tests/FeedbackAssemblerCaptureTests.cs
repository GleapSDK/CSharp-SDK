using GleapSDK.Feedback;

namespace Gleap.Core.Tests;

public class FeedbackAssemblerCaptureTests
{
    [Fact]
    public void Build_IncludesScreenshotAndReplay_WhenNotExcluded()
    {
        var result = FeedbackAssembler.Build(
            new Dictionary<string, object?>(),
            new Dictionary<string, object?>(),
            "BUG",
            null,
            false,
            new HashSet<string>(),
            attachments: null,
            screenshotUrl: "https://uploads.gleap.io/x.png",
            replay: new Dictionary<string, object?> { ["interval"] = 1000 });

        Assert.Equal("https://uploads.gleap.io/x.png", result["screenshotUrl"]);
        Assert.True(result.ContainsKey("replay"));
    }

    [Fact]
    public void Build_OmitsScreenshot_WhenExcluded()
    {
        var result = FeedbackAssembler.Build(
            new Dictionary<string, object?>(),
            new Dictionary<string, object?>(),
            "BUG",
            null,
            false,
            excludeKeys: new HashSet<string> { "screenshot", "replays" },
            attachments: null,
            screenshotUrl: "https://uploads.gleap.io/x.png",
            replay: new Dictionary<string, object?> { ["interval"] = 1000 });

        Assert.False(result.ContainsKey("screenshotUrl"));
        Assert.False(result.ContainsKey("replay"));
    }
}
