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
        /// After this returns, navigate the channel's WebView to <c>GleapEndpoints.Default.FrameUrl</c>
        /// (the channel implementation typically does this in its own setup).
        /// </summary>
        public static async Task<ManagedBackend> AttachAsync(IWebViewChannel channel, string sdkKey)
        {
            var backend = new ManagedBackend(new ManagedBackend.Dependencies
            {
                Http = new HttpTransport(),
                Json = new SystemTextJsonSerializer(),
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
