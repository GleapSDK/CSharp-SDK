using System.Collections.Generic;

namespace GleapSDK.Session;

/// <summary>Immutable view of everything the bootstrap sequence needs.</summary>
public sealed class SessionSnapshot
{
    public string SdkKey { get; set; } = "";
    public string ApiUrl { get; set; } = "";
    public string GleapId { get; set; } = "";
    public string GleapHash { get; set; } = "";
    public string FlowConfigJson { get; set; } = "{}";
    public string ProjectActionsJson { get; set; } = "{}";
    public string Language { get; set; } = "en";
    public string? UserId { get; set; }
    public string? Name { get; set; }
    public string? Email { get; set; }

    public IReadOnlyDictionary<string, object?>? PreFillFormData { get; set; }
}
