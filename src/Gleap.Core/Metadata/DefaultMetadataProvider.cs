using System.Collections.Generic;
using System.Globalization;

namespace GleapSDK.Metadata;

/// <summary>
/// The cross-platform metadata subset available without native APIs. Platform packages
/// wrap or replace this in SP-1 to add device model, OS version, battery, disk, etc.
/// </summary>
public sealed class DefaultMetadataProvider : IMetadataProvider
{
    private readonly string _sdkType;
    private readonly string _sdkVersion;

    public DefaultMetadataProvider(string sdkType, string sdkVersion)
    {
        _sdkType = sdkType;
        _sdkVersion = sdkVersion;
    }

    public IReadOnlyDictionary<string, object?> Collect() => new Dictionary<string, object?>
    {
        ["sdkType"] = _sdkType,
        ["sdkVersion"] = _sdkVersion,
        ["preferredUserLocale"] = CultureInfo.CurrentCulture.Name
    };
}
