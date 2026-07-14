using System.Collections.Generic;
using System.Text.Json;

namespace GleapSDK.Outbound;

/// <summary>Parsed outbound update: actions + unread count. Delivered by the <c>POST /sessions/ping</c>
/// response (poll mode) and by the WebSocket <c>update</c> frame's <c>data</c> object — both share the
/// same <c>{ a: [actions], u: unread }</c> shape, so both use <see cref="Parse"/>.</summary>
public sealed class PingResponse
{
    public IReadOnlyList<OutboundAction> Actions { get; }
    public int UnreadCount { get; }

    public PingResponse(IReadOnlyList<OutboundAction> actions, int unreadCount)
    {
        Actions = actions;
        UnreadCount = unreadCount;
    }

    /// <summary>Parses the <c>{ a: [...], u: n }</c> shape from either a ping response root or a WebSocket
    /// <c>update</c> frame's <c>data</c> object. Action elements are cloned so they outlive the source
    /// <see cref="JsonDocument"/>.</summary>
    public static PingResponse Parse(JsonElement root)
    {
        var actions = new List<OutboundAction>();
        if (root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("a", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in arr.EnumerateArray())
            {
                var actionType = item.TryGetProperty("actionType", out var at) && at.ValueKind == JsonValueKind.String
                    ? at.GetString()! : "";
                var outboundId = item.TryGetProperty("outbound", out var ob) && ob.ValueKind == JsonValueKind.String
                    ? ob.GetString() : null;
                actions.Add(new OutboundAction(actionType, outboundId, item.Clone()));
            }
        }
        var unread = root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("u", out var u) && u.ValueKind == JsonValueKind.Number ? u.GetInt32() : 0;
        return new PingResponse(actions, unread);
    }
}
