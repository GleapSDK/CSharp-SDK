using System.Text.Json;
using GleapSDK.Outbound;

namespace Gleap.Core.Tests;

public class GleapChecklistUpdateTests
{
    private static JsonElement Data(string json) => JsonDocument.Parse(json).RootElement;

    private const string TwoOfThree = """
    {
      "id": "cl-1",
      "outboundId": "ob-1",
      "status": "active",
      "completedStepsBefore": ["s1"],
      "completedSteps": ["s1", "s2"],
      "steps": [{"id":"s1","title":"One"},{"id":"s2","title":"Two"},{"id":"s3","title":"Three"}]
    }
    """;

    [Fact]
    public void FromFrameData_ParsesFrame()
    {
        var u = GleapChecklistUpdate.FromFrameData(Data(TwoOfThree))!;

        Assert.Equal("cl-1", u.ChecklistId);
        Assert.Equal("ob-1", u.OutboundId);
        Assert.Equal("active", u.Status);
        Assert.False(u.IsCompleted);
        Assert.Equal(new[] { "s1", "s2" }, u.CompletedSteps);
        Assert.Equal(new[] { "s1" }, u.CompletedStepsBefore);
        Assert.Equal(3, u.Steps.Count);
        Assert.Equal("Two", u.Steps[1].Title);
        Assert.Equal(1, u.Steps[1].Index);
    }

    [Fact]
    public void NewlyCompletedSteps_OnlyReturnsStepsNotAlreadyCompleted()
    {
        var u = GleapChecklistUpdate.FromFrameData(Data(TwoOfThree))!;

        var newly = u.NewlyCompletedSteps();

        // s1 was already done before this frame; only s2 is newly completed.
        // (The JS reference re-fires s1 too — a bug we deliberately do not port.)
        var step = Assert.Single(newly);
        Assert.Equal("s2", step.Id);
        Assert.Equal(1, step.Index);   // index into steps, not into completedSteps
    }

    [Fact]
    public void NewlyCompletedSteps_IsEmpty_WhenNothingChanged()
    {
        var u = GleapChecklistUpdate.FromFrameData(Data("""
        {
          "id": "cl-1", "outboundId": "ob-1", "status": "active",
          "completedStepsBefore": ["s1"], "completedSteps": ["s1"],
          "steps": [{"id":"s1"},{"id":"s2"}]
        }
        """))!;

        Assert.Empty(u.NewlyCompletedSteps());
    }

    [Fact]
    public void IsCompleted_WhenStatusDone()
    {
        var u = GleapChecklistUpdate.FromFrameData(Data("""
        {
          "id": "cl-1", "outboundId": "ob-1", "status": "done",
          "completedStepsBefore": ["s1"], "completedSteps": ["s1","s2"],
          "steps": [{"id":"s1"},{"id":"s2"}]
        }
        """))!;

        Assert.True(u.IsCompleted);
        Assert.Equal("s2", Assert.Single(u.NewlyCompletedSteps()).Id);
    }

    [Fact]
    public void FromFrameData_ToleratesMissingArrays()
    {
        var u = GleapChecklistUpdate.FromFrameData(Data("""{"id":"cl-1"}"""))!;

        Assert.NotNull(u);
        Assert.Empty(u.CompletedSteps);
        Assert.Empty(u.Steps);
        Assert.Empty(u.NewlyCompletedSteps());
    }

    [Fact]
    public void FromFrameData_ReturnsNull_WithoutAnyId()
    {
        Assert.Null(GleapChecklistUpdate.FromFrameData(Data("""{"status":"active"}""")));
        Assert.Null(GleapChecklistUpdate.FromFrameData(Data("[]")));
    }
}
