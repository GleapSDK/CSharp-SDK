using System.Collections.Generic;

namespace GleapSDK.Models;

/// <summary>A custom AI tool declared to the Gleap agent.</summary>
public sealed class AITool
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Response { get; set; } = "";
    public string ExecutionType { get; set; } = "";
    public List<AIToolParameter> Parameters { get; set; } = new();
}
