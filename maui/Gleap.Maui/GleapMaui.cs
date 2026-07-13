using System.Threading;
using System.Threading.Tasks;
using GleapSDK.Bridge;
using GleapSDK.Http;
using GleapSDK.Serialization;

namespace GleapSDK.Maui;

/// <summary>
/// MAUI entry point. Reuses the platform-agnostic <see cref="ManagedBackend"/> with MAUI persistence
/// (<see cref="MauiKeyValueStore"/>) and metadata (<see cref="MauiMetadataProvider"/>). The caller
/// supplies an <see cref="IWebViewChannel"/> that wires the native WebView bridge for the current
/// platform (see the package README for the per-platform contract).
/// </summary>
public static class GleapMaui
{
    public const string SdkVersion = "0.1.0";

    public static async Task<ManagedBackend> AttachAsync(IWebViewChannel channel, string sdkKey)
    {
        var backend = new ManagedBackend(new ManagedBackend.Dependencies
        {
            Http = new HttpTransport(),
            Json = new SystemTextJsonSerializer(),
            Store = new MauiKeyValueStore(),
            Channel = channel,
            Endpoints = GleapEndpoints.Default,
            Metadata = new MauiMetadataProvider(SdkVersion)
        });

        Gleap.UseBackend(backend);
        await backend.InitializeAsync(sdkKey, CancellationToken.None).ConfigureAwait(false);
        return backend;
    }
}
