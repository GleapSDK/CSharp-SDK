using System.Threading;
using System.Threading.Tasks;
using GleapSDK.Bridge;
using GleapSDK.Http;
using GleapSDK.Serialization;

namespace GleapSDK.Unity
{
    /// <summary>
    /// Unity entry point. Reuses the platform-agnostic <see cref="ManagedBackend"/> with Unity-specific
    /// persistence and metadata. The caller supplies an <see cref="IWebViewChannel"/> (backed by a Unity
    /// WebView plugin, e.g. Vuplex/3D WebView, or a WebGL bridge) that loads the widget frame URL.
    /// </summary>
    public static class GleapUnity
    {
        public const string SdkVersion = "0.1.0";

        /// <summary>
        /// Wires the Gleap facade to a Unity-hosted WebView channel, then initializes the session/config.
        /// Defaults to the IL2CPP-safe <see cref="NewtonsoftJsonSerializer"/>; pass a different
        /// <see cref="IJsonSerializer"/> to override (e.g. <c>SystemTextJsonSerializer</c> on Mono builds).
        /// </summary>
        public static async Task<ManagedBackend> AttachAsync(
            IWebViewChannel channel, string sdkKey, IJsonSerializer? json = null)
        {
            var backend = new ManagedBackend(new ManagedBackend.Dependencies
            {
                Http = new HttpTransport(),
                Json = json ?? new NewtonsoftJsonSerializer(),
                Store = new UnityKeyValueStore(),
                Channel = channel,
                Endpoints = GleapEndpoints.Default,
                Metadata = new UnityMetadataProvider(SdkVersion)
            });

            Gleap.UseBackend(backend);
            await backend.InitializeAsync(sdkKey, CancellationToken.None);
            return backend;
        }
    }
}
