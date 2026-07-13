using System.Collections.Generic;
using GleapSDK.Metadata;
using Microsoft.Maui.Devices;

namespace GleapSDK.Maui;

/// <summary>MAUI device metadata (via <see cref="DeviceInfo"/>) layered over <see cref="DefaultMetadataProvider"/>.</summary>
public sealed class MauiMetadataProvider : IMetadataProvider
{
    private readonly DefaultMetadataProvider _baseProvider;

    public MauiMetadataProvider(string sdkVersion)
    {
        var platform = DeviceInfo.Current.Platform.ToString();
        _baseProvider = new DefaultMetadataProvider($"MAUI/{platform}", sdkVersion);
    }

    public IReadOnlyDictionary<string, object?> Collect()
    {
        var meta = new Dictionary<string, object?>();
        foreach (var kv in _baseProvider.Collect())
        {
            meta[kv.Key] = kv.Value;
        }

        meta["systemName"] = DeviceInfo.Current.Platform.ToString();
        meta["systemVersion"] = DeviceInfo.Current.VersionString;
        meta["deviceModel"] = DeviceInfo.Current.Model;
        meta["deviceName"] = DeviceInfo.Current.Name;
        meta["buildMode"] = DeviceInfo.Current.DeviceType.ToString();

        return meta;
    }
}
