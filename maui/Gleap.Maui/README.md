# Gleap for .NET MAUI (`Gleap.Maui`)

MAUI binding that reuses the platform-agnostic **`Gleap.Core`** engine. MAUI supplies persistence
(`Preferences`), device metadata (`DeviceInfo`), and the native WebView bridge, which ships in this
package as `MauiWebViewChannel`. Setup guide: [.NET MAUI SDK setup](https://docs.gleap.ai/en/articles/640-net-maui-sdk-setup).

> **Status: authored, NOT yet compiled/verified** (requires the MAUI workload + platform SDKs). The
> Core-reusing parts (store, metadata, bootstrap) are complete; the **per-platform WebView wiring
> inside `MauiWebViewChannel` is the verify-first piece**. See below.

## Use
Put a MAUI `WebView` on your page, wrap it in the included `MauiWebViewChannel`, and attach:

```xml
<WebView x:Name="Web" />
```

```csharp
using GleapSDK;
using GleapSDK.Maui;

var channel = new MauiWebViewChannel(Web);               // the WebView from your XAML
await GleapMaui.AttachAsync(channel, "YOUR_PROJECT_ID"); // Project ID: Gleap dashboard, Settings → Project → General
Gleap.Open();
```

`GleapMaui.AttachAsync(MauiWebViewChannel channel, string projectId)` waits for the WebView's native
handler, wires the bridge, initializes the session and config, then loads the widget into the WebView.
It returns the `ManagedBackend`. You don't implement a channel and don't navigate the WebView yourself.

If your app's namespace contains `Gleap` (for example `MyCompany.Gleap.App`), a bare `Gleap` resolves
to that namespace. Alias the facade instead: `using GleapSdk = GleapSDK.Gleap;`. The sample app in
[`examples/Gleap.Sample.Maui`](../../examples/Gleap.Sample.Maui) does this.

## How `MauiWebViewChannel` wires each platform
`MauiWebViewChannel` implements `GleapSDK.Bridge.IWebViewChannel` over the MAUI `WebView`'s native
view (`Handler.PlatformView`), loads `https://messenger-app.gleap.io/appnew`, and wires the bridge
the way the widget's `/appnew` wrapper expects:

- **iOS / MacCatalyst (WKWebView):** the wrapper posts to `window.webkit.messageHandlers.gleapCallback`.
  The channel registers a `WKScriptMessageHandler` named `gleapCallback` on
  `Configuration.UserContentController` (re-attaching replaces it), serializes the posted body to
  JSON with `NSJSONSerialization`, and sends with `EvaluateJavaScript`.
- **Android (Android.Webkit.WebView):** the wrapper calls `GleapJSBridge.gleapCallback(jsonString)`.
  The channel enables JavaScript, adds a `GleapJSBridge` JavaScript interface, and sends with
  `EvaluateJavascript`.
- **Windows (WebView2):** the channel injects
  `window.GleapJSBridge = { gleapCallback: s => window.chrome.webview.postMessage(s) }` via
  `AddScriptToExecuteOnDocumentCreatedAsync`, handles `WebMessageReceived`, and sends with
  `ExecuteScriptAsync`, like `WebView2Channel` in `Gleap.WebView2`.

The host must answer `ping` with `config-update`(`isApp:true`)+`session-update`. `Gleap.Core`'s
`WidgetBootstrapper` does that, so the channel only moves bytes. Links the widget opens (`openURL`,
http and https only) open in the system browser.

## Known risks / verify-first
- The per-platform handler wiring in `MauiWebViewChannel` is the only unverified part; expect to
  iterate on first build.
- The iOS path converts `WKScriptMessage.Body` (an `NSObject`) to JSON with `NSJSONSerialization` and
  falls back to `{}` if that fails; the Android and Windows paths already deliver a JSON string.
- The `net10.0-windows10.0.19041.0` target only builds on Windows; the csproj adds it conditionally.
- Alternative "wrap-native" strategy (bind the mature native iOS/Android Gleap SDKs instead of reusing
  Core over a WebView) remains available if deeper native capture (URLSession/OkHttp network logs,
  native replay) is required later. See the parent design spec §2.
