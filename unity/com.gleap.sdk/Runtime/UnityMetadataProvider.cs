using System.Collections.Generic;
using GleapSDK.Metadata;
using UnityEngine;

namespace GleapSDK.Unity
{
    /// <summary>Unity device metadata layered over <see cref="DefaultMetadataProvider"/>.</summary>
    public sealed class UnityMetadataProvider : IMetadataProvider
    {
        private readonly DefaultMetadataProvider _baseProvider;

        public UnityMetadataProvider(string sdkVersion)
        {
            _baseProvider = new DefaultMetadataProvider("Unity", sdkVersion);
        }

        public IReadOnlyDictionary<string, object> Collect()
        {
            var meta = new Dictionary<string, object>();
            foreach (var kv in _baseProvider.Collect())
            {
                meta[kv.Key] = kv.Value;
            }

            meta["systemName"] = SystemInfo.operatingSystemFamily.ToString();
            meta["systemVersion"] = SystemInfo.operatingSystem;
            meta["deviceModel"] = SystemInfo.deviceModel;
            meta["deviceName"] = SystemInfo.deviceName;
            meta["deviceIdentifier"] = SystemInfo.deviceUniqueIdentifier;
            meta["screenWidth"] = Screen.width;
            meta["screenHeight"] = Screen.height;

            return meta;
        }
    }
}
