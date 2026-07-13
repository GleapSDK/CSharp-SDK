namespace GleapSDK.Http;

/// <summary>Configurable Gleap service base URLs.</summary>
public sealed class GleapEndpoints
{
    public string ApiUrl { get; set; } = "https://api.gleap.io";
    public string WsUrl { get; set; } = "wss://ws.gleap.io";
    public string FrameUrl { get; set; } = "https://messenger-app.gleap.io/appnew";

    public static GleapEndpoints Default => new();
}
