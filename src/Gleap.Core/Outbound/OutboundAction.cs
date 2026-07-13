using System.Text.Json;

namespace GleapSDK.Outbound;

/// <summary>One server-pushed outbound action from a ping response.</summary>
public sealed class OutboundAction
{
    public string ActionType { get; }
    public string? OutboundId { get; }
    public JsonElement Data { get; }

    public OutboundAction(string actionType, string? outboundId, JsonElement data)
    {
        ActionType = actionType;
        OutboundId = outboundId;
        Data = data;
    }
}
