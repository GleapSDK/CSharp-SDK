using System.Collections.Generic;

namespace GleapSDK.Outbound;

/// <summary>Parsed <c>POST /sessions/ping</c> response: outbound actions + unread count.</summary>
public sealed class PingResponse
{
    public IReadOnlyList<OutboundAction> Actions { get; }
    public int UnreadCount { get; }

    public PingResponse(IReadOnlyList<OutboundAction> actions, int unreadCount)
    {
        Actions = actions;
        UnreadCount = unreadCount;
    }
}
