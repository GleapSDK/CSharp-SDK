# Gleap C# SDK — SP-1: Windows Desktop (WebView2 + WPF) Design

- **Date:** 2026-07-13
- **Status:** Design approved. Ready for implementation plan.
- **Goal:** Ship `Gleap.WebView2`, a Windows desktop binding that hosts the live Gleap widget (`messenger-app.gleap.io/appnew`) in a WebView2 control and drives it via the already-built `Gleap.Core` `ManagedBackend` — plus a WPF sample app that serves as the **first live validation** of the entire managed-bridge approach.
- **Builds on:** SP-0 Part 1 (facade, `ManagedBackend`, `WebViewBridge`, session/config bootstrap) + Part 2a (data-collection layer). 74 headless tests green.
- **Depends on the parent design:** `docs/superpowers/specs/2026-07-13-csharp-sdk-design.md` (§2 architecture, §5 bridge contract, §8 open items — this spec resolves the "messenger-app compatibility in WebView2" item).

---

## 1. Key finding: hosting works with ZERO Messenger-App changes

Investigation of `Messenger-App/` + `JavaScript-SDK/` established exactly how the widget bridges to a native host:

- The native entry point is **`https://messenger-app.gleap.io/appnew`** — a thin HTML wrapper (`Messenger-App/public/appnew.html`) that embeds the React widget in an inner iframe and relays messages. **Do not load the bare React app** as the top document: its `postMessage` targets `window.parent`, which for a top-level document is itself, so messages never reach the host.
- The wrapper's **outgoing** bridge (`appnew.html` `postMessageToApp`, ~line 1204) knows only two hosts: iOS `window.webkit.messageHandlers.gleapCallback.postMessage(obj)` and an Android-style **`GleapJSBridge.gleapCallback(jsonString)`**. There is no WebView2 branch and no per-platform switch — it fires whichever global exists.
- `GleapJSBridge` is a **generic, shimmable contract.** A WebView2 host injects it (routing to `window.chrome.webview.postMessage`) and the widget treats the host as native — **no changes to the Messenger-App.**
- The wrapper's **incoming** global is `window.sendMessage(obj)` (~line 1146); the host calls it (via `ExecuteScriptAsync`) with a JS object. Envelope `{ name, data, [shareToken], [newTab] }`, matching `Gleap.Core`'s `GleapBridgeMessage`.
- The wrapper does **not** answer `ping`; the host must reply with `config-update` (`isApp:true` + the real flow-config) then `session-update`, then `widget-status-update` on open. **`Gleap.Core`'s `WidgetBootstrapper` + `ManagedBackend.Open()` already do exactly this** (post-review-fix ordering). So Part 1's bridge is correct for WebView2 as-is; SP-1 only adds the transport.
- Globals must be present **before** `/appnew` scripts run → inject the shim via `CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync`.

### The exact shim (injected at document-create, before navigation)

```js
window.GleapJSBridge = {
  gleapCallback: function (jsonString) { window.chrome.webview.postMessage(jsonString); }
};
```

`chrome.webview.postMessage(string)` surfaces on the C# side as `CoreWebView2.WebMessageReceived` with `TryGetWebMessageAsString()`. Host→widget is `CoreWebView2.ExecuteScriptAsync("window.sendMessage(" + json + ")")`.

---

## 2. Architecture

`Gleap.Core` is unchanged. SP-1 adds one platform package + one sample.

```
Gleap.WebView2  (net8.0-windows; refs Microsoft.Web.WebView2 + Gleap.Core)
  WebView2Channel : IWebViewChannel      // the transport (the only WebView2-specific protocol code)
  WindowsMetadataProvider : IMetadataProvider
  FileKeyValueStore : IKeyValueStore     // persist gleapId/gleapHash under %LOCALAPPDATA%\Gleap
  GleapWebView2Host                      // one-call bootstrap: wire Dependencies + ManagedBackend + UseBackend + init + navigate

Gleap.Sample.Wpf  (net8.0-windows WPF app)
  MainWindow                             // WebView2 control + button panel calling the Gleap facade
```

### Component responsibilities

**`WebView2Channel : IWebViewChannel`** — control-agnostic over `CoreWebView2`.
- Construction/init (async): `await webView.EnsureCoreWebView2Async()`; `await CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(shimScript)`; subscribe `CoreWebView2.WebMessageReceived`; then the host navigates to the frame URL.
- `ExecuteJavaScript(script)` → marshals to the control's `Dispatcher` and calls `CoreWebView2.ExecuteScriptAsync(script)` (fire-and-forget; the bridge does not await).
- `WebMessageReceived` handler → `args.TryGetWebMessageAsString()` → raise `MessageReceived(json)`. Already on the UI thread.
- The shim string is a `const` in this class.

**`WindowsMetadataProvider : IMetadataProvider`** — returns `DefaultMetadataProvider`'s cross-platform subset plus Windows specifics mapped to the spec §7 field names: `systemName` ("Windows"), `systemVersion`/`releaseVersionNumber` (`RuntimeInformation.OSDescription` / `Environment.OSVersion`), `deviceModel`/`deviceName` (`Environment.MachineName`), `buildVersionNumber` (entry-assembly version), `preferredUserLocale` (`CultureInfo`), `screenWidth`/`screenHeight` (`SystemParameters`), `sdkType` = "NET/Windows", `sdkVersion`. Missing mobile-only fields (battery, disk) are simply omitted.

**`FileKeyValueStore : IKeyValueStore`** — JSON file at `%LOCALAPPDATA%\Gleap\session.json`; `Get`/`Set`/`Remove` read-modify-write. No registry permissions needed. Tolerates a missing/corrupt file (treats as empty).

**`GleapWebView2Host`** — `static Task<ManagedBackend> AttachAsync(Microsoft.Web.WebView2.Wpf.WebView2 webView, string sdkKey, GleapEndpoints? endpoints = null)`: builds `WebView2Channel`, `ManagedBackend.Dependencies { Http = new HttpTransport(), Json = new SystemTextJsonSerializer(), Store = new FileKeyValueStore(), Channel = channel, Endpoints = endpoints ?? GleapEndpoints.Default }`, `new ManagedBackend(deps)`, `Gleap.UseBackend(backend)`, `await backend.InitializeAsync(sdkKey)`, `channel.Navigate(endpoints.FrameUrl)`. Returns the backend.

**`Gleap.Sample.Wpf.MainWindow`** — WebView2 control filling the window, a right-hand panel of buttons wired to the **facade methods that currently exist**: `Gleap.Open/Close`, `StartConversation`, `StartBot`, `OpenConversation`, `OpenHelpCenter`, `OpenNews`, `ShowSurvey`, plus Part 2a data methods `TrackEvent`, `SetCustomData`, `Log`. sdkKey read from `gleap.sdkkey` env var or a text box. On load: `await GleapWebView2Host.AttachAsync(WebView, sdkKey)`. (The facade does not yet expose the full nav surface or identify — see §7; those are Core follow-ups, not needed to validate the bridge.)

---

## 3. Data flow (open widget)

1. App calls `Gleap.Open()` → `ManagedBackend.Open()` enqueues `widget-status-update{isWidgetOpen:true}` (bridge not yet connected).
2. `GleapWebView2Host.AttachAsync` already navigated the WebView2 to `/appnew`; the shim is installed; the wrapper loads its inner React iframe.
3. React app boots → posts `{name:"ping"}` → wrapper relays via `GleapJSBridge.gleapCallback(json)` → `chrome.webview.postMessage` → `WebMessageReceived` → `WebViewBridge` parses `ping`.
4. Bridge sets connected, raises `PingReceived` → `WidgetBootstrapper` sends `config-update{config, actions, overrideLanguage, isApp:true}` + `session-update{sessionData, apiUrl, sdkKey}` via `window.sendMessage(...)`; then flushes the queued `widget-status-update`.
5. Widget applies config/session, connects its socket, renders. Subsequent nav commands (`StartBot`, etc.) flow straight through (connected).

---

## 4. Threading

WebView2 raises events and requires `ExecuteScriptAsync` on the UI (STA) thread. `WebView2Channel` marshals outgoing scripts to the control's `Dispatcher`; incoming `WebMessageReceived` is already on the UI thread. The app calls the `Gleap` facade from UI handlers. Net effect: **all `WebViewBridge` interaction is single-threaded (UI thread)** — satisfying Core's documented single-thread assumption (the deferred M1). No locking added; this is the correct model for a UI-hosted WebView.

---

## 5. Validation (on the user's Windows machine)

WebView2 + WPF cannot be built on the macOS dev box, so validation is **manual, run by the user on Windows**, guided by a runbook the plan produces.

**Prerequisites:** .NET 8 SDK; WebView2 Runtime (preinstalled on current Win10/11); a valid **Gleap project SDK key** for a project with the messenger enabled (supplied via `gleap.sdkkey` env var or the sample's text box — never committed).

**Runbook (shape):** `git`-less copy or clone of `CSharp-SDK/` to Windows → `dotnet run --project examples/Gleap.Sample.Wpf` → enter sdkKey → click **Open** → the widget renders inside the window.

**Acceptance (what proves SP-1):**
- Widget renders in WebView2 and the `ping` handshake completes (config/session applied — home screen themed per project config).
- Open/Close works; the launcher/home shows.
- Navigation: Start Bot / Start Conversation / Open Help Center / Open News / Show Survey each route the widget to the right screen.
- AI/bot chat, help-center articles, news render and are interactive (these are widget+server flows; no native submission needed).
- `TrackEvent`/`SetCustomData`/`Log` populate the collected data without error.
- Starting a classic form triggers `collect-ticket-data`; the host reply is accepted (form pre-populated with collected data).

**Known limitation (expected, not a bug):** *Submitting* a classic feedback form (`send-feedback` → `POST /bugs/v2`) is **not** wired until SP-0 Part 2b, so a classic-form submit will not complete in SP-1. Conversational/AI/help-center/news flows are unaffected.

**Automated tests:** none new run on macOS (the package is `net8.0-windows`). Protocol logic is already covered by the 74 Core tests. Any pure helpers added (e.g. `FileKeyValueStore` round-trip, shim-string constant) may get a `net8.0-windows` test the user can run on Windows, but this is optional — SP-1's gate is the manual acceptance above.

---

## 6. Error handling

- `WebView2Channel` init failures (`EnsureCoreWebView2Async` throwing — e.g. missing WebView2 Runtime) surface as a clear exception from `AttachAsync` with a message pointing at the Runtime install. The sample shows it in a MessageBox.
- `InitializeAsync` already throws `GleapApiException` on a bad sdkKey / network failure (Part 1 review fix); the sample surfaces it so a wrong key is obvious during validation.
- `ExecuteScriptAsync` is fire-and-forget in the channel; failures are logged via `System.Diagnostics.Debug` but do not crash the app.

---

## 7. Open items / follow-ups

- **Facade parity gap (Core follow-up):** the facade/`IGleapBackend` currently exposes a subset of the messenger nav surface (Open/Close, StartConversation, StartBot, OpenConversation, OpenHelpCenter, OpenNews, ShowSurvey) + Part 2a data methods. The remaining `WidgetCommands` builders already exist but are not yet wired to the facade: `SearchHelpCenter`, `OpenHelpCenterArticle`/`Collection`, `OpenNewsArticle`, `OpenFeatureRequests`, `OpenChecklists`/`OpenChecklist`/`StartChecklist`, `AskAI`, `StartClassicForm`. Also **identify/updateContact/clearIdentity is not built** (needs `ApiClient` `/sessions/identify` + `/sessions/partialupdate` + `SessionManager.IdentifyAsync` + facade). These are small, headless Core additions — schedule as a "Core facade completion" follow-up (macOS-buildable). Not required to validate the WebView2 bridge (a guest session renders the widget fully).
- **sdkType/applicationType registration** (parent spec §8): SP-1 sends `sdkType:"NET/Windows"`. Confirm the Gleap backend/analytics attribute it (does not block validation; the widget renders regardless).
- **Part 2b** unblocks classic-form submission (`send-feedback` → `/bugs/v2`) — do after SP-1 proves the bridge.
- **NuGet packaging** of `Gleap.WebView2` is deferred until after live validation succeeds.
- If live validation reveals the widget needs additional bootstrap messages (e.g. `prefill-form-data`, `screenshot-update`) we haven't wired, capture them as fast-follow Core fixes.
