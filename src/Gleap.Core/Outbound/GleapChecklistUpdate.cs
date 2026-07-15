using System.Collections.Generic;
using System.Text.Json;

namespace GleapSDK.Outbound;

/// <summary>One step of a checklist, as carried by the live update frame.</summary>
public sealed class GleapChecklistStep
{
    /// <summary>Step id (the only field the SDKs key on; the rest is rendered by the widget).</summary>
    public string? Id { get; set; }

    /// <summary>Step title, when the outbound config provides one.</summary>
    public string? Title { get; set; }

    /// <summary>Zero-based position in the checklist's <c>steps</c> array.</summary>
    public int Index { get; set; }
}

/// <summary>
/// A live checklist progress update: the server's WebSocket <c>checklist</c> frame, sent whenever a step
/// is incremented. Note the server pushes this to every session, not just JS ones — the iOS SDK simply
/// drops it, so this is the C# SDK's own handling.
/// </summary>
/// <remarks>
/// The reference JS implementation (<c>GleapStreamedEvent.js</c>) has a defect in its diff: it tests
/// <c>!completedStepsBefore.includes(step)</c>, comparing an id-string array against a step *object*, which
/// is always true — so it re-fires "step completed" for every already-completed step. This port implements
/// the intended semantics instead: only steps in <c>completedSteps</c> but NOT in <c>completedStepsBefore</c>
/// count as newly completed. <see cref="GleapChecklistStep.Index"/> is likewise the index into
/// <c>steps</c> (the step's real position), not the index into <c>completedSteps</c>.
/// </remarks>
public sealed class GleapChecklistUpdate
{
    /// <summary>Per-session checklist instance id (frame <c>data.id</c>).</summary>
    public string? ChecklistId { get; set; }

    /// <summary>Outbound/campaign id — the id passed to <c>StartChecklist</c> (frame <c>data.outboundId</c>).</summary>
    public string? OutboundId { get; set; }

    /// <summary>Checklist status: <c>draft</c> | <c>active</c> | <c>done</c>.</summary>
    public string? Status { get; set; }

    /// <summary>Step ids completed after this increment.</summary>
    public IReadOnlyList<string> CompletedSteps { get; set; } = new List<string>();

    /// <summary>Step ids that were completed before this increment.</summary>
    public IReadOnlyList<string> CompletedStepsBefore { get; set; } = new List<string>();

    /// <summary>The checklist's full step definitions, in order.</summary>
    public IReadOnlyList<GleapChecklistStep> Steps { get; set; } = new List<GleapChecklistStep>();

    /// <summary>True once every step is done (server sets <c>status == "done"</c>).</summary>
    public bool IsCompleted => Status == "done";

    /// <summary>The steps completed by <em>this</em> update (in <see cref="CompletedSteps"/> but not in
    /// <see cref="CompletedStepsBefore"/>), resolved against <see cref="Steps"/> and ordered by position.</summary>
    public IReadOnlyList<GleapChecklistStep> NewlyCompletedSteps()
    {
        var before = new HashSet<string>(CompletedStepsBefore);
        var result = new List<GleapChecklistStep>();
        foreach (var step in Steps)
        {
            if (step.Id != null && !before.Contains(step.Id) && Contains(CompletedSteps, step.Id))
            {
                result.Add(step);
            }
        }
        return result;
    }

    /// <summary>Parses the <c>data</c> object of a WebSocket <c>checklist</c> frame. Returns null when the
    /// payload carries no usable checklist.</summary>
    public static GleapChecklistUpdate? FromFrameData(JsonElement data)
    {
        if (data.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var update = new GleapChecklistUpdate
        {
            ChecklistId = Str(data, "id"),
            OutboundId = Str(data, "outboundId"),
            Status = Str(data, "status"),
            CompletedSteps = StringList(data, "completedSteps"),
            CompletedStepsBefore = StringList(data, "completedStepsBefore")
        };

        if (update.ChecklistId == null && update.OutboundId == null)
        {
            return null;
        }

        var steps = new List<GleapChecklistStep>();
        if (data.TryGetProperty("steps", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            var i = 0;
            foreach (var item in arr.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object)
                {
                    steps.Add(new GleapChecklistStep
                    {
                        Id = Str(item, "id"),
                        Title = Str(item, "title"),
                        Index = i
                    });
                }
                i++;
            }
        }
        update.Steps = steps;
        return update;
    }

    private static bool Contains(IReadOnlyList<string> list, string value)
    {
        for (var i = 0; i < list.Count; i++)
        {
            if (list[i] == value)
            {
                return true;
            }
        }
        return false;
    }

    private static string? Str(JsonElement obj, string key) =>
        obj.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static List<string> StringList(JsonElement obj, string key)
    {
        var list = new List<string>();
        if (obj.TryGetProperty(key, out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in arr.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    var s = item.GetString();
                    if (s != null)
                    {
                        list.Add(s);
                    }
                }
            }
        }
        return list;
    }
}
