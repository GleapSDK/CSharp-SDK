namespace GleapSDK.Bridge;

/// <summary>Outgoing message envelope: { name, data, shareToken? }.</summary>
public sealed class GleapBridgeMessage
{
    public string Name { get; set; } = "";
    public object? Data { get; set; }
    public string? ShareToken { get; set; }
}
