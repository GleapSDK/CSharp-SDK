using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace GleapSDK.Models;

/// <summary>A single parameter of an <see cref="AITool"/>.</summary>
public sealed class AIToolParameter
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";

    public AIParamType Type { get; set; }

    public bool Required { get; set; }

    /// <summary>Allowed values. Serialized as "enum" to match the web widget.</summary>
    [JsonPropertyName("enum")]
    public List<string>? Enums { get; set; }
}
