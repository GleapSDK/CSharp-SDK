using System.Text.Json;

namespace GleapSDK.Bridge;

/// <summary>Parsed inbound message from the web widget.</summary>
public readonly struct IncomingBridgeMessage
{
    public string Name { get; }
    public JsonElement Data { get; }        // may be Undefined
    public string? ShareToken { get; }      // top-level (used by run-custom-action)

    public IncomingBridgeMessage(string name, JsonElement data, string? shareToken)
    {
        Name = name;
        Data = data;
        ShareToken = shareToken;
    }

    public static IncomingBridgeMessage Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var name = root.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
        var data = root.TryGetProperty("data", out var d) ? d.Clone() : default;
        string? token = root.TryGetProperty("shareToken", out var t) ? t.GetString() : null;
        return new IncomingBridgeMessage(name, data, token);
    }
}
