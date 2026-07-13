namespace GleapSDK.Models;

/// <summary>Request half of a <see cref="GleapNetworkLog"/>.</summary>
public sealed class GleapNetworkRequest
{
    public object? Payload { get; set; }
    public object? Headers { get; set; }
}
