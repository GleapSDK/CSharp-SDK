using GleapSDK.Feedback;

namespace Gleap.Core.Tests;

public class FeedbackAssemblerTests
{
    [Fact]
    public void Build_MergesTypeFormDataAndTicketData()
    {
        var ticketData = new Dictionary<string, object?>
        {
            ["metaData"] = new Dictionary<string, object?> { ["sdkType"] = "NET" },
            ["formData"] = new Dictionary<string, object?> { ["priority"] = "low" },
            ["tags"] = new[] { "vip" }
        };
        var formData = new Dictionary<string, object?> { ["description"] = "it broke" };

        var result = FeedbackAssembler.Build(ticketData, formData, "BUG", null, false, new HashSet<string>());

        Assert.Equal("BUG", result["type"]);
        var resultFormData = Assert.IsType<Dictionary<string, object?>>(result["formData"]);
        Assert.Equal("it broke", resultFormData["description"]);
        Assert.Equal("low", resultFormData["priority"]);
        Assert.True(result.ContainsKey("metaData"));
        Assert.False(result.ContainsKey("isSilent"));
        Assert.False(result.ContainsKey("priority"));
    }

    [Fact]
    public void Build_OmitsExcludedKeys()
    {
        var ticketData = new Dictionary<string, object?>
        {
            ["metaData"] = new Dictionary<string, object?> { ["sdkType"] = "NET" },
            ["tags"] = new[] { "vip" }
        };
        var formData = new Dictionary<string, object?>();
        var excludeKeys = new HashSet<string> { "metaData", "tags" };

        var result = FeedbackAssembler.Build(ticketData, formData, "BUG", null, false, excludeKeys);

        Assert.False(result.ContainsKey("metaData"));
        Assert.False(result.ContainsKey("tags"));
    }

    [Fact]
    public void Build_CrashSetsPriorityAndSilent()
    {
        var result = FeedbackAssembler.Build(
            new Dictionary<string, object?>(),
            new Dictionary<string, object?> { ["description"] = "crashed" },
            "CRASH", "HIGH", true, new HashSet<string>());

        Assert.Equal("CRASH", result["type"]);
        Assert.Equal("HIGH", result["priority"]);
        Assert.Equal(true, result["isSilent"]);
    }
}
