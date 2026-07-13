namespace GleapSDK.Models;

/// <summary>One captured HTTP exchange, as sent in <c>networkLogs</c>.</summary>
public sealed class GleapNetworkLog
{
    public string Type { get; set; } = "";
    public string Url { get; set; } = "";
    public string Date { get; set; } = "";
    public double Duration { get; set; }
    public bool Success { get; set; }
    public GleapNetworkRequest Request { get; set; } = new();
    public GleapNetworkResponse Response { get; set; } = new();
}
