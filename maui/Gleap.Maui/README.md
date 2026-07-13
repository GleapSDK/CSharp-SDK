# Gleap for .NET MAUI (`Gleap.Maui`)

MAUI binding that reuses the platform-agnostic **`Gleap.Core`** engine. MAUI supplies persistence
(`Preferences`), device metadata (`DeviceInfo`), and — the one piece you wire per platform — the
native WebView bridge.

> **Status: authored, NOT yet compiled/verified** (requires the MAUI workload + platform SDKs). The
> Core-reusing parts (store, metadata, bootstrap) are complete; the **WebView channel is the
> verify-first piece** — see below.

## Use
```csharp
using GleapSDK;
using GleapSDK.Maui;

// `channel` implements IWebViewChannel over the platform WebView (see next section).
await GleapMaui.AttachAsync(channel, "YOUR_SDK_KEY");
Gleap.Open();
```

## The WebView channel (per-platform bridge you must implement)
Implement `GleapSDK.Bridge.IWebViewChannel` over a MAUI `WebView` (or a platform-native WebView),
load `GleapEndpoints.Default.FrameUrl` (`https://messenger-app.gleap.io/appnew`), and wire the bridge
the way the widget's `/appnew` wrapper expects (confirmed by reverse-engineering the wrapper):

- **iOS / MacCatalyst (WKWebView):** the wrapper posts to `window.webkit.messageHandlers.gleapCallback`.
  Get the `WKWebView` from the MAUI WebView handler (`handler.PlatformView`), add a
  `WKScriptMessageHandler` named `gleapCallback` to `Configuration.UserContentController` → raise
  `MessageReceived` (serialize the posted body to JSON). `ExecuteJavaScript` → `EvaluateJavaScriptAsync`.
- **Android (Android.Webkit.WebView):** the wrapper calls `GleapJSBridge.gleapCallback(jsonString)`.
  `SetJavaScriptEnabled(true)`, `AddJavascriptInterface(bridge, "GleapJSBridge")` where `bridge` exposes
  `[JavascriptInterface] void gleapCallback(string s)` → raise `MessageReceived(s)`. `ExecuteJavaScript`
  → `webView.EvaluateJavascript(script, null)`.
- **Windows (WebView2):** identical to the shipped `Gleap.WebView2` `WebView2Channel` — inject
  `window.GleapJSBridge = { gleapCallback: s => window.chrome.webview.postMessage(s) }` via
  `AddScriptToExecuteOnDocumentCreatedAsync`, handle `WebMessageReceived`, `ExecuteScriptAsync` to send.
  (You can literally reuse `WebView2Channel` from `Gleap.WebView2` on the Windows target.)

The host must answer `ping` with `config-update`(`isApp:true`)+`session-update` — but that is already
handled by `Gleap.Core`'s `WidgetBootstrapper`, so your channel only moves bytes.

## Known risks / verify-first
- The per-platform handler wiring above is the only unverified part; expect to iterate on first build.
- The exact iOS message-body→JSON conversion (WKScriptMessage.Body is an `NSObject`) needs
  `NSJSONSerialization`; the Android/Windows paths already deliver a JSON string.
- `net8.0-windows` target only builds on Windows; the csproj adds it conditionally.
- Alternative "wrap-native" strategy (bind the mature native iOS/Android Gleap SDKs instead of reusing
  Core over a WebView) remains available if deeper native capture (URLSession/OkHttp network logs,
  native replay) is required later — see the parent design spec §2.
