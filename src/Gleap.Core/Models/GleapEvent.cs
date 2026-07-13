namespace GleapSDK.Models;

/// <summary>A tracked event, as sent in <c>customEventLog</c>.</summary>
public sealed class GleapEvent
{
    public string Name { get; set; } = "";
    public object? Data { get; set; }
    public string Date { get; set; } = "";
}
