using System.Collections.Generic;

namespace GleapSDK.Feedback;

/// <summary>
/// Builds the <c>POST /bugs/v2</c> body from the collected ticket data plus the submitted form,
/// applying feedback type/priority/silent flags and removing any excluded keys.
/// </summary>
public static class FeedbackAssembler
{
    public static Dictionary<string, object?> Build(
        IReadOnlyDictionary<string, object?> ticketData,
        IReadOnlyDictionary<string, object?> formData,
        string type,
        string? priority,
        bool isSilent,
        ISet<string> excludeKeys,
        IReadOnlyList<IReadOnlyDictionary<string, object?>>? attachments = null,
        string? screenshotUrl = null,
        IReadOnlyDictionary<string, object?>? replay = null)
    {
        var body = new Dictionary<string, object?>();
        foreach (var kv in ticketData)
        {
            body[kv.Key] = kv.Value;
        }

        // The submitted form merges over any prefilled ticket formData.
        var mergedForm = new Dictionary<string, object?>();
        if (ticketData.TryGetValue("formData", out var existingForm)
            && existingForm is IReadOnlyDictionary<string, object?> existingFormDict)
        {
            foreach (var kv in existingFormDict)
            {
                mergedForm[kv.Key] = kv.Value;
            }
        }
        foreach (var kv in formData)
        {
            mergedForm[kv.Key] = kv.Value;
        }
        body["formData"] = mergedForm;

        body["type"] = type;
        if (!string.IsNullOrEmpty(priority))
        {
            body["priority"] = priority;
        }
        if (isSilent)
        {
            body["isSilent"] = true;
        }
        if (attachments != null && attachments.Count > 0)
        {
            body["attachments"] = attachments;
        }
        if (screenshotUrl != null)
        {
            body["screenshotUrl"] = screenshotUrl;
        }
        if (replay != null)
        {
            body["replay"] = replay;
        }

        foreach (var key in excludeKeys)
        {
            body.Remove(key);
        }
        if (excludeKeys.Contains("replays"))
        {
            body.Remove("replay");
        }
        if (excludeKeys.Contains("screenshot"))
        {
            body.Remove("screenshotUrl");
        }

        return body;
    }
}
