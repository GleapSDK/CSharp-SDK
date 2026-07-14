using System.Collections.Generic;
using GleapSDK.Metadata;
using UnityEngine;

namespace GleapSDK.Unity
{
    /// <summary>Unity device metadata layered over <see cref="DefaultMetadataProvider"/>.</summary>
    /// <remarks>
    /// The Unity APIs read here (<see cref="SystemInfo"/>, <see cref="Screen"/>) are main-thread-only, but
    /// <c>Collect()</c> is invoked from Core's ticket-build path which can run on a ThreadPool continuation
    /// (<c>ConfigureAwait(false)</c>). So the snapshot is captured once in the constructor — which runs on
    /// the main thread during <see cref="GleapUnity.AttachAsync"/> — and <see cref="Collect"/> just returns
    /// the cache, never touching Unity APIs off-thread.
    /// </remarks>
    public sealed class UnityMetadataProvider : IMetadataProvider
    {
        private readonly IReadOnlyDictionary<string, object> _snapshot;

        public UnityMetadataProvider(string sdkVersion)
        {
            var meta = new Dictionary<string, object>();
            foreach (var kv in new DefaultMetadataProvider("Unity", sdkVersion).Collect())
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

            _snapshot = meta;
        }

        public IReadOnlyDictionary<string, object> Collect() => _snapshot;
    }
}
