using System;
using System.Threading;
using System.Threading.Tasks;
using GleapSDK.Http;
using GleapSDK.Serialization;
using Microsoft.Maui.ApplicationModel;

namespace GleapSDK.Maui;

/// <summary>
/// MAUI entry point. Reuses the platform-agnostic <see cref="ManagedBackend"/> with MAUI persistence
/// (<see cref="MauiKeyValueStore"/>) and metadata (<see cref="MauiMetadataProvider"/>). Give it a
/// <see cref="MauiWebViewChannel"/> over a MAUI <see cref="Microsoft.Maui.Controls.WebView"/>; it wires
/// the native bridge, initializes the session/config, and navigates to the widget.
/// </summary>
public static class GleapMaui
{
    public const string SdkVersion = "0.1.0";

    public static async Task<ManagedBackend> AttachAsync(MauiWebViewChannel channel, string sdkKey)
    {
        await channel.InitializeAsync().ConfigureAwait(true);

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
        backend.RegisterListener("openURL", url => OpenExternalUrl(url as string));
        await backend.InitializeAsync(sdkKey, CancellationToken.None).ConfigureAwait(true);

        // Navigate only after the backend (WidgetBootstrapper) is listening for the widget's ping.
        channel.Navigate();
        return backend;
    }

    private static void OpenExternalUrl(string? url)
    {
        if (!string.IsNullOrEmpty(url)
            && Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            _ = Launcher.Default.OpenAsync(uri);
        }
    }
}
