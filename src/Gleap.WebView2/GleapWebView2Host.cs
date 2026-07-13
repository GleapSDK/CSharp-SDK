using System.Threading;
using System.Threading.Tasks;
using GleapSDK.Http;
using GleapSDK.Serialization;

namespace GleapSDK.WebView2;

/// <summary>
/// One-call setup: builds a <see cref="WebView2Channel"/> for the given control, wires a
/// <see cref="ManagedBackend"/> with Windows persistence, attaches it to the <see cref="Gleap"/>
/// facade, initializes the session/config, and navigates to the widget.
/// </summary>
public static class GleapWebView2Host
{
    /// <summary>SDK version reported in metadata (bump with releases).</summary>
    public const string SdkVersion = "0.1.0";

    public static async Task<ManagedBackend> AttachAsync(
        Microsoft.Web.WebView2.Wpf.WebView2 webView, string sdkKey, GleapEndpoints? endpoints = null)
    {
        var resolvedEndpoints = endpoints ?? GleapEndpoints.Default;

        var channel = new WebView2Channel(webView);
        await channel.InitializeAsync().ConfigureAwait(true);

        var backend = new ManagedBackend(new ManagedBackend.Dependencies
        {
            Http = new HttpTransport(),
            Json = new SystemTextJsonSerializer(),
            Store = new FileKeyValueStore(),
            Channel = channel,
            Endpoints = resolvedEndpoints,
            Screenshot = new WindowsScreenshotProvider()
        });

        Gleap.UseBackend(backend);
        await backend.InitializeAsync(sdkKey, CancellationToken.None).ConfigureAwait(true);

        // Navigate only after the backend (and its WidgetBootstrapper) is listening, so the
        // widget's ping is handled and answered with config-update/session-update.
        channel.Navigate(resolvedEndpoints.FrameUrl);
        return backend;
    }
}
