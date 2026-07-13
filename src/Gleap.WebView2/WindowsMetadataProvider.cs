using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using GleapSDK.Metadata;

namespace GleapSDK.WebView2;

/// <summary>
/// Windows device metadata layered over <see cref="DefaultMetadataProvider"/>'s cross-platform subset.
/// </summary>
public sealed class WindowsMetadataProvider : IMetadataProvider
{
    private readonly DefaultMetadataProvider _baseProvider;

    public WindowsMetadataProvider(string sdkVersion) =>
        _baseProvider = new DefaultMetadataProvider("NET/Windows", sdkVersion);

    public IReadOnlyDictionary<string, object?> Collect()
    {
        var meta = new Dictionary<string, object?>();
        foreach (var kv in _baseProvider.Collect())
        {
            meta[kv.Key] = kv.Value;
        }

        meta["systemName"] = "Windows";
        meta["systemVersion"] = RuntimeInformation.OSDescription;
        meta["releaseVersionNumber"] = Environment.OSVersion.Version.ToString();
        meta["deviceName"] = Environment.MachineName;
        meta["deviceModel"] = Environment.MachineName;
        meta["buildVersionNumber"] = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "1.0.0";

        return meta;
    }
}
