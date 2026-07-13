using System.Collections.Generic;

namespace GleapSDK.Metadata;

/// <summary>
/// Supplies device/session metadata for reports. Platform packages implement this with
/// native device info; <see cref="DefaultMetadataProvider"/> supplies the runtime-agnostic subset.
/// </summary>
public interface IMetadataProvider
{
    IReadOnlyDictionary<string, object?> Collect();
}
