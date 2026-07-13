namespace GleapSDK.Models;

/// <summary>Response half of a <see cref="GleapNetworkLog"/>.</summary>
public sealed class GleapNetworkResponse
{
    public int Status { get; set; }
    public string StatusText { get; set; } = "";
    public string ResponseText { get; set; } = "";
}
