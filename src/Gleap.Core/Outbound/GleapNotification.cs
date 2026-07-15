using System.Text.Json;

namespace GleapSDK.Outbound;

/// <summary>The kind of in-app notification preview card, mirroring the native SDKs' three renderers.</summary>
public enum GleapNotificationKind
{
    /// <summary>An agent chat reply (sender avatar + name + text).</summary>
    Message,

    /// <summary>A news article preview (cover image + title).</summary>
    News,

    /// <summary>A checklist progress card.</summary>
    Checklist
}

/// <summary>The agent/sender shown on a chat notification card.</summary>
public sealed class GleapNotificationSender
{
    /// <summary>Display name of the sender (agent).</summary>
    public string? Name { get; set; }

    /// <summary>Avatar image URL.</summary>
    public string? ProfileImageUrl { get; set; }
}

/// <summary>
/// A parsed in-app notification — the <c>notification</c> outbound action the server pushes when an agent
/// replies (or a proactive message/news/checklist fires). Portable across platform hosts, which render it
/// as a preview card above the launcher. Mirrors the iOS <c>GleapUIOverlayViewController</c> and JS
/// <c>GleapNotificationManager</c> data models: dedup by <see cref="Outbound"/>, click target derived from
/// <see cref="ConversationShareToken"/> / <see cref="NewsId"/> / <see cref="ChecklistId"/>.
/// </summary>
public sealed class GleapNotification
{
    /// <summary>Outbound id — the de-duplication key (one card per distinct outbound).</summary>
    public string? Outbound { get; set; }

    /// <summary>Whether to play a subtle sound when the card appears.</summary>
    public bool Sound { get; set; }

    /// <summary>Which card variant to render.</summary>
    public GleapNotificationKind Kind { get; set; }

    /// <summary>Body text, with any <c>{{name}}</c> placeholder already substituted.</summary>
    public string? Text { get; set; }

    /// <summary>Sender (agent) for a <see cref="GleapNotificationKind.Message"/> card.</summary>
    public GleapNotificationSender? Sender { get; set; }

    /// <summary>Conversation share token; when set, tapping the card opens that conversation.</summary>
    public string? ConversationShareToken { get; set; }

    /// <summary>News article id; when set (and no conversation), tapping opens that article.</summary>
    public string? NewsId { get; set; }

    /// <summary>Optional cover image URL (news cards).</summary>
    public string? CoverImageUrl { get; set; }

    /// <summary>Checklist id; when set (and no conversation), tapping opens that checklist.</summary>
    public string? ChecklistId { get; set; }

    /// <summary>Checklist popup type. <c>"widget"</c> means the host should open the checklist in the
    /// messenger directly instead of showing a preview card (native behavior).</summary>
    public string? ChecklistPopupType { get; set; }

    /// <summary>Current completed step (checklist cards).</summary>
    public int ChecklistCurrentStep { get; set; }

    /// <summary>Total steps (checklist cards).</summary>
    public int ChecklistTotalSteps { get; set; }

    /// <summary>Title of the next incomplete step (checklist cards).</summary>
    public string? ChecklistNextStepTitle { get; set; }

    /// <summary>
    /// Parses a <c>notification</c> outbound action's raw JSON (the whole action, which carries a nested
    /// <c>data</c> object) into a <see cref="GleapNotification"/>. Returns <c>null</c> when the payload has
    /// no usable notification data. <paramref name="userName"/> (the identified contact's full name, if
    /// any) is used to substitute the <c>{{name}}</c> placeholder in the text with the first name.
    /// </summary>
    public static GleapNotification? FromActionJson(string? actionJson, string? userName)
    {
        if (string.IsNullOrWhiteSpace(actionJson))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(actionJson!);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            // The forwarded action carries the notification fields inside `data`; there is nothing to show
            // without it.
            if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var n = new GleapNotification
            {
                Outbound = Str(root, "outbound"),
                Sound = root.TryGetProperty("sound", out var s)
                    && (s.ValueKind == JsonValueKind.True || (s.ValueKind == JsonValueKind.String && s.GetString() == "true")),
                Text = Substitute(Str(data, "text"), FirstName(userName)),
                CoverImageUrl = Str(data, "coverImageUrl")
            };

            if (data.TryGetProperty("sender", out var sender) && sender.ValueKind == JsonValueKind.Object)
            {
                n.Sender = new GleapNotificationSender
                {
                    Name = Str(sender, "name"),
                    ProfileImageUrl = Str(sender, "profileImageUrl")
                };
            }

            if (data.TryGetProperty("conversation", out var conv) && conv.ValueKind == JsonValueKind.Object)
            {
                n.ConversationShareToken = Str(conv, "shareToken");
            }

            if (data.TryGetProperty("news", out var news) && news.ValueKind == JsonValueKind.Object)
            {
                n.NewsId = Str(news, "id");
                n.CoverImageUrl ??= Str(news, "coverImageUrl");
            }

            if (data.TryGetProperty("checklist", out var checklist) && checklist.ValueKind == JsonValueKind.Object)
            {
                n.ChecklistId = Str(checklist, "id");
                n.ChecklistPopupType = Str(checklist, "popupType");
                n.ChecklistNextStepTitle = Str(checklist, "nextStepTitle");
                n.ChecklistCurrentStep = Int(checklist, "currentStep");
                n.ChecklistTotalSteps = Int(checklist, "totalSteps");
            }

            // Kind: honour an explicit data.type, else infer from the richest payload present.
            var type = Str(data, "type");
            n.Kind = type switch
            {
                "news" => GleapNotificationKind.News,
                "checklist" => GleapNotificationKind.Checklist,
                _ when n.NewsId != null => GleapNotificationKind.News,
                _ when n.ChecklistId != null => GleapNotificationKind.Checklist,
                _ => GleapNotificationKind.Message
            };

            return n;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static readonly char[] NameSeparators = { ' ', '@', '.', '+' };

    private static string? Str(JsonElement obj, string key) =>
        obj.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int Int(JsonElement obj, string key) =>
        obj.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : 0;

    /// <summary>First name for the <c>{{name}}</c> placeholder. Splits on space, <c>@</c>, <c>.</c> and
    /// <c>+</c> like the reference SDKs, so a contact identified only by an email address renders as
    /// "tobias" rather than "tobias@gleap.io".</summary>
    private static string? FirstName(string? fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName))
        {
            return null;
        }
        var parts = fullName!.Trim().Split(NameSeparators, System.StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 0 ? parts[0] : null;
    }

    private static string? Substitute(string? text, string? firstName)
    {
        if (string.IsNullOrEmpty(text) || text!.IndexOf("{{name}}", System.StringComparison.Ordinal) < 0)
        {
            return text;
        }
        var replaced = text.Replace("{{name}}", firstName ?? "");
        // Collapse the double space / dangling comma left when the name was empty.
        return replaced.Replace("  ", " ").Replace(" ,", ",").Trim();
    }
}
