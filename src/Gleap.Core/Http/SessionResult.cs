namespace GleapSDK.Http;

/// <summary>Parsed <c>POST /sessions</c> response.</summary>
public sealed class SessionResult
{
    public string GleapId { get; set; } = "";
    public string GleapHash { get; set; } = "";
}
