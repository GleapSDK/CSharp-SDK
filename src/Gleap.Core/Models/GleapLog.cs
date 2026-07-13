namespace GleapSDK.Models;

/// <summary>A single captured console/log line, as sent in <c>consoleLog</c>.</summary>
public sealed class GleapLog
{
    public string Date { get; set; } = "";
    public string Log { get; set; } = "";
    public string Priority { get; set; } = "INFO";
}
