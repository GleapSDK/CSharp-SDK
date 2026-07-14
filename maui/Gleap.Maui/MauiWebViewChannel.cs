using System;
using System.Threading.Tasks;
using GleapSDK.Bridge;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;

namespace GleapSDK.Maui;

/// <summary>
/// <see cref="IWebViewChannel"/> over a MAUI <see cref="WebView"/>, wiring the Gleap widget bridge on
/// the underlying native view per platform (see the package README for the contract):
/// Android <c>AddJavascriptInterface(…, "GleapJSBridge")</c>; iOS/MacCatalyst a
/// <c>WKScriptMessageHandler</c> named <c>gleapCallback</c>; Windows WebView2's
/// <c>GleapJSBridge</c> shim + <c>WebMessageReceived</c>. Only moves bytes — the ping handshake is
/// answered by <see cref="ManagedBackend"/>'s <c>WidgetBootstrapper</c>.
/// </summary>
public sealed class MauiWebViewChannel : IWebViewChannel
{
    private const string FrameUrl = "https://messenger-app.gleap.io/appnew";

    private readonly WebView _webView;
    private TaskCompletionSource<bool>? _ready;

    public MauiWebViewChannel(WebView webView) => _webView = webView;

    /// <inheritdoc />
    public event Action<string>? MessageReceived;

    /// <summary>Waits for the native handler, wires the bridge, and leaves the view ready to navigate.</summary>
    public async Task InitializeAsync()
    {
        await WaitForHandlerAsync().ConfigureAwait(true);
        await WirePlatformAsync().ConfigureAwait(true);
    }

    /// <summary>Navigates the WebView to the Gleap widget frame (call after the backend is listening).</summary>
    public void Navigate(string? url = null) =>
        RunOnMainThread(() => PlatformNavigate(url ?? FrameUrl));

    /// <inheritdoc />
    public void ExecuteJavaScript(string script) => RunOnMainThread(() => PlatformExecute(script));

    private void Raise(string json) => MessageReceived?.Invoke(json);

    private Task WaitForHandlerAsync()
    {
        if (_webView.Handler?.PlatformView != null)
        {
            return Task.CompletedTask;
        }
        _ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _webView.HandlerChanged += OnHandlerChanged;
        // In case the handler arrived between the check and the subscription.
        if (_webView.Handler?.PlatformView != null)
        {
            _webView.HandlerChanged -= OnHandlerChanged;
            _ready.TrySetResult(true);
        }
        return _ready.Task;
    }

    private void OnHandlerChanged(object? sender, EventArgs e)
    {
        if (_webView.Handler?.PlatformView != null)
        {
            _webView.HandlerChanged -= OnHandlerChanged;
            _ready?.TrySetResult(true);
        }
    }

    private static void RunOnMainThread(Action action)
    {
        if (MainThread.IsMainThread)
        {
            action();
        }
        else
        {
            MainThread.BeginInvokeOnMainThread(action);
        }
    }

#if ANDROID
    private Task WirePlatformAsync()
    {
        var native = (Android.Webkit.WebView)_webView.Handler!.PlatformView!;
        native.Settings.JavaScriptEnabled = true;
        native.AddJavascriptInterface(new AndroidBridge(this), "GleapJSBridge");
        return Task.CompletedTask;
    }

    private void PlatformExecute(string script)
    {
        var native = (Android.Webkit.WebView)_webView.Handler!.PlatformView!;
        native.EvaluateJavascript(script, null);
    }

    private void PlatformNavigate(string url)
    {
        var native = (Android.Webkit.WebView)_webView.Handler!.PlatformView!;
        native.LoadUrl(url);
    }

    private sealed class AndroidBridge : Java.Lang.Object
    {
        private readonly MauiWebViewChannel _owner;
        public AndroidBridge(MauiWebViewChannel owner) => _owner = owner;

        // The /appnew wrapper calls GleapJSBridge.gleapCallback(jsonString).
        [Android.Webkit.JavascriptInterface]
        [Java.Interop.Export("gleapCallback")]
        public void GleapCallback(string s) => _owner.Raise(s);
    }
#elif IOS || MACCATALYST
    private Task WirePlatformAsync()
    {
        var native = (WebKit.WKWebView)_webView.Handler!.PlatformView!;
        var controller = native.Configuration.UserContentController;
        // WKUserContentController throws if a handler name is added twice. Re-attach (e.g. the WebView's
        // handler being recreated) would otherwise crash — remove any prior "gleapCallback" first so
        // registration is idempotent.
        controller.RemoveScriptMessageHandler("gleapCallback");
        controller.AddScriptMessageHandler(new IosBridge(this), "gleapCallback");
        return Task.CompletedTask;
    }

    private void PlatformExecute(string script)
    {
        var native = (WebKit.WKWebView)_webView.Handler!.PlatformView!;
        native.EvaluateJavaScript(new Foundation.NSString(script), (_, _) => { });
    }

    private void PlatformNavigate(string url)
    {
        var native = (WebKit.WKWebView)_webView.Handler!.PlatformView!;
        native.LoadRequest(new Foundation.NSUrlRequest(new Foundation.NSUrl(url)));
    }

    private sealed class IosBridge : WebKit.WKScriptMessageHandler
    {
        private readonly MauiWebViewChannel _owner;
        public IosBridge(MauiWebViewChannel owner) => _owner = owner;

        // The wrapper posts the message OBJECT (not a string) on iOS; serialize it to JSON.
        public override void DidReceiveScriptMessage(WebKit.WKUserContentController userContentController, WebKit.WKScriptMessage message)
        {
            var body = message.Body;
            string json;
            if (body is Foundation.NSString s)
            {
                json = s.ToString();
            }
            else
            {
                var data = Foundation.NSJsonSerialization.Serialize(body, 0, out var error);
                json = (error == null && data != null)
                    ? new Foundation.NSString(data, Foundation.NSStringEncoding.UTF8).ToString()
                    : "{}";
            }
            _owner.Raise(json);
        }
    }
#elif WINDOWS
    private Microsoft.Web.WebView2.Core.CoreWebView2? _core;

    private async Task WirePlatformAsync()
    {
        var native = (Microsoft.UI.Xaml.Controls.WebView2)_webView.Handler!.PlatformView!;
        await native.EnsureCoreWebView2Async();
        _core = native.CoreWebView2;
        await _core.AddScriptToExecuteOnDocumentCreatedAsync(
            "window.GleapJSBridge = { gleapCallback: function (s) { window.chrome.webview.postMessage(s); } };");
        _core.WebMessageReceived += (_, e) =>
        {
            try { Raise(e.TryGetWebMessageAsString()); } catch { /* ignore malformed */ }
        };
    }

    private void PlatformExecute(string script) => _ = _core?.ExecuteScriptAsync(script);

    private void PlatformNavigate(string url) => _core?.Navigate(url);
#else
    private Task WirePlatformAsync() => Task.CompletedTask;
    private void PlatformExecute(string script) { }
    private void PlatformNavigate(string url) { }
#endif
}
