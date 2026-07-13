using System.Collections.Generic;

namespace GleapSDK.Models;

public sealed class GleapUserProperty
{
    public string? UserId { get; set; }
    public string? Name { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Plan { get; set; }
    public string? CompanyName { get; set; }
    public string? CompanyId { get; set; }
    public string? Avatar { get; set; }
    public string? Lang { get; set; }
    public double? Value { get; set; }
    public double? Sla { get; set; }
    public Dictionary<string, object>? CustomData { get; set; }
}
