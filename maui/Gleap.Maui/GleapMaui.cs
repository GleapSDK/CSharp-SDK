using System;
using System.Threading;
using System.Threading.Tasks;
using GleapSDK.Http;
using GleapSDK.Serialization;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Devices;

namespace GleapSDK.Maui;

/// <summary>
/// MAUI entry point. Reuses the platform-agnostic <see cref="ManagedBackend"/> with MAUI persistence
/// (<see cref="MauiKeyValueStore"/>) and metadata (<see cref="MauiMetadataProvider"/>). Give it a
/// <see cref="MauiWebViewChannel"/> over a MAUI <see cref="Microsoft.Maui.Controls.WebView"/>; it wires
/// the native bridge, initializes the session/config, and navigates to the widget.
/// </summary>
public static class GleapMaui
{
    public const string SdkVersion = Gleap.Version;

    public static async Task<ManagedBackend> AttachAsync(MauiWebViewChannel channel, string sdkKey)
    {
        await channel.InitializeAsync().ConfigureAwait(true);

        var (platform, deviceType) = ResolvePlatform();
        var backend = new ManagedBackend(new ManagedBackend.Dependencies
        {
            Http = new HttpTransport(),
            Json = new SystemTextJsonSerializer(),
            Store = new MauiKeyValueStore(),
            Channel = channel,
            Endpoints = GleapEndpoints.Default,
            Metadata = new MauiMetadataProvider(SdkVersion),
            Platform = platform,
            DeviceType = deviceType,
            SdkVersion = SdkVersion
        });

        Gleap.UseBackend(backend);
        backend.RegisterListener("openURL", url => OpenExternalUrl(url as string));
        await backend.InitializeAsync(sdkKey, CancellationToken.None).ConfigureAwait(true);

        // Navigate only after the backend (WidgetBootstrapper) is listening for the widget's ping.
        channel.Navigate();
        return backend;
    }

    /// <summary>Maps the running MAUI platform to the <c>platform</c>/<c>type</c> values the Gleap API
    /// expects (matching the native SDKs' lowercase ids), so a session is not misreported as
    /// windows/desktop. Mobile targets report <c>"mobile"</c>; desktop targets report <c>"desktop"</c>.</summary>
    private static (string Platform, string DeviceType) ResolvePlatform()
    {
        var p = DeviceInfo.Current.Platform;
        if (p == DevicePlatform.Android)
        {
            return ("android", "mobile");
        }
        if (p == DevicePlatform.iOS)
        {
            return ("ios", "mobile");
        }
        if (p == DevicePlatform.MacCatalyst || p == DevicePlatform.macOS)
        {
            return ("macos", "desktop");
        }
        if (p == DevicePlatform.WinUI)
        {
            return ("windows", "desktop");
        }
        // Unknown/other (e.g. Tizen): lower-case the platform id, default to desktop.
        var name = p.ToString();
        return (string.IsNullOrEmpty(name) ? "windows" : name.ToLowerInvariant(), "desktop");
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
