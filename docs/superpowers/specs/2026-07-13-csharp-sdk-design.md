# Gleap C# SDK — Design & Feasibility Spec

- **Date:** 2026-07-13
- **Status:** Design approved (architecture + decomposition). Ready for SP-0 implementation plan.
- **Goal:** A production, publishable C# SDK with **full feature parity** to the existing Gleap Android & iOS SDKs, covering **Unity**, **.NET MAUI**, and **Windows Desktop (WPF/WinForms)**.
- **Non-goal:** MVP / proof-of-concept. This ships.

---

## 1. Key finding that shapes everything

The existing cross-platform SDKs (**Flutter**, **ReactNative**) do **not** reimplement Gleap in Dart/JS. On mobile they are **thin bridges** that marshal calls to the mature native iOS/Android SDKs (Flutter: a single `MethodChannel` named `gleap_sdk` → `io.gleap.Gleap.*` / ObjC `Gleap`; events pushed back over the same channel). On web they bridge to the JS SDK (`window.Gleap.*`). All hard work — replay, network capture (iOS URLSession / Android OkHttp), shake/screenshot activation, screenshotting — lives in the native SDKs.

**Consequence for C#:** the design question is *not* "reimplement everything in C#" but **"wrap where a native SDK already exists, reimplement in pure C# only where none does."** This is the industry-standard pattern (Firebase, Sentry, AppsFlyer, Adjust, Braze all ship their Unity/MAUI variants this way) and it is the future-proof choice: platform fixes (Apple privacy manifests, new OS capture rules, Android API levels) are inherited from the native SDKs rather than re-solved forever in C#.

### Where does a native SDK exist to wrap?

| Runtime under the C# target | Native Gleap SDK exists? | Backend strategy |
|---|---|---|
| Unity → iOS build | ✅ iOS SDK | wrap (ObjC native plugin) |
| Unity → Android build | ✅ Android SDK | wrap (JNI / `AndroidJavaObject`) |
| Unity → WebGL | ✅ JS SDK | wrap (`.jslib` → `window.Gleap`) |
| Unity → Desktop (Win/Mac/Linux) | ❌ | **pure C# Core + WebView plugin** |
| MAUI → iOS / Android | ✅ native SDKs | wrap (binding libraries) |
| MAUI → Windows | ❌ | **pure C# Core + WebView2** |
| WPF / WinForms | ❌ | **pure C# Core + WebView2** |
| Unity → consoles (PS/Xbox/Switch) | ❌ (no browser/WebView) | **out of scope — unsupported** |

The pure-C# core is only unavoidable for **Desktop**, but it doubles as the reference implementation and is reused across every desktop slice.

---

## 2. Chosen strategy: Hybrid (① — approved)

One stable public **`Gleap` facade** (identical API on every target, mirroring Flutter's single `Gleap` class), over a swappable internal backend contract.

```
Gleap  (Public Facade — IDENTICAL on every target)
  │
  ▼
IGleapBackend  (internal contract — one well-defined interface)
  ├─ NativeBridgeBackend   → marshals to an EXISTING native SDK
  │     • Unity iOS      (ObjC plugin, extern "C")
  │     • Unity Android  (AndroidJavaObject / JNI)
  │     • Unity WebGL    (.jslib → window.Gleap)
  │     • MAUI iOS       (binding lib → Gleap.framework)
  │     • MAUI Android   (binding lib → gleap-android-sdk .aar)
  │
  └─ ManagedBackend        → pure C#, NO native SDK underneath
        uses  Gleap.Core:
          • ApiClient        (/sessions, /config/{token}, /sessions/ping, /bugs/v2, uploads)
          • SessionManager   (identify/update/clear, gleapId + gleapHash)
          • ConfigManager
          • WebViewBridge    (envelope {name,data,shareToken}, ping-handshake, message queue)
          • Collectors       (Metadata, ConsoleLog, NetworkLog via HttpClient handler, Screenshot, Replay)
          • Buffers/DTOs     (events, logs, customData, ticketAttrs, tags, attachments)
        +  WebViewHost adapter:
          • WebView2Host     (WPF / WinForms / WinUI / MAUI-Windows)
          • UnityWebViewHost (Unity desktop via plugin)
```

**Future-proofing property:** the app-facing API is stable and decoupled from the backend. A future native Windows SDK, a new runtime, or a new native method is absorbed by adding/swapping a backend — the facade and customer code do not change. Each unit has one responsibility and is independently testable (the `WebViewBridge` in particular is testable headless, with no real WebView).

### Deliverable packages

| Package | Contents | Consumed by |
|---|---|---|
| `Gleap.Core` | .NET Standard 2.0, headless engine, UI-free | everything managed |
| `Gleap.WebView2` | WebView2 host adapter | WPF / WinForms / WinUI / MAUI-Win |
| `Gleap.Maui` | MAUI facade; Win via WebView2, mobile via bindings | MAUI apps |
| `Gleap.iOS.Binding`, `Gleap.Android.Binding` | .NET bindings over native SDKs | `Gleap.Maui` |
| `Gleap.Unity` | UPM package: facade + native plugins (iOS/Android) + WebGL jslib + desktop Core path | Unity projects |

---

## 3. Full public API surface (canonical parity target)

Compiled from the ReactNative + Flutter canonical surfaces. The C# facade must expose all of these (idiomatic C# naming, `async Task` where the native returns a value). "Mobile-only" = no-op / unsupported on desktop; noted per capability in §5.

**Init / lifecycle:** `Initialize(token)`, `Open()`, `Close()`, `IsOpened() → bool`, `ShowFeedbackButton(bool)`.

**Identity:** `IdentifyContact(userId, GleapUserProperty?, userHash?)`, `UpdateContact(GleapUserProperty)`, `ClearIdentity()`, `IsUserIdentified() → bool`, `GetIdentity() → GleapUserProperty?`. (`identify` kept as deprecated alias.)

**Messenger / UI open:** `StartConversation(showBackButton)`, `OpenConversation(shareToken)`, `OpenConversations()`, `StartBot(botId, showBackButton)`, `StartClassicForm(formId, showBackButton)`, `OpenHelpCenter(showBackButton)`, `OpenHelpCenterArticle(articleId, showBackButton)`, `OpenHelpCenterCollection(collectionId, showBackButton)`, `SearchHelpCenter(term, showBackButton)`, `OpenNews(showBackButton)`, `OpenNewsArticle(articleId, showBackButton)`, `OpenFeatureRequests(showBackButton)`, `OpenChecklists(showBackButton)`, `OpenChecklist(checklistId, showBackButton)`, `StartChecklist(outboundId, showBackButton)`, `ShowSurvey(surveyId, SurveyFormat)`, `AskAI(question, showBackButton)`. (`startFeedbackFlow` kept as deprecated alias of `StartClassicForm`.)

**Tracking / logging:** `TrackEvent(name, data?)`, `TrackPage(pageName)`, `Log(message, LogLevel = Info)`, `EnableDebugConsoleLog()` *(iOS-only)*, `DisableConsoleLog()`, `SendSilentCrashReport(description, Severity, excludeData?)`.

**Custom data:** `AttachCustomData(dict)`, `SetCustomData(key, value)`, `RemoveCustomDataForKey(key)`, `ClearCustomData()`, `PreFillForm(dict)`.

**Ticket attributes:** `SetTicketAttribute(key, value)`, `UnsetTicketAttribute(key)`, `ClearTicketAttributes()`.

**Tags:** `SetTags(string[])`.

**Attachments (mobile-only):** `AddAttachment(base64File, fileName)`, `RemoveAllAttachments()`. (Android validates extensions: jpeg/svg/png/mp4/webp/xml/plain/json.)

**Network logging:** `AttachNetworkLogs(GleapNetworkLog[])`, `SetNetworkLogsBlacklist(string[])`, `SetNetworkLogPropsToIgnore(string[])`, `StartNetworkLogging()`, `StopNetworkLogging()`.

**AI tools:** `SetAiTools(AITool[])`.

**Activation / config setters:** `SetActivationMethods(ActivationMethod[])` *(mobile-only)*, `SetLanguage(lang)`, `SetApiUrl(url)`, `SetFrameUrl(url)`, `SetDisableInAppNotifications(bool)`, `SetNotificationContainerOffset(x, y)`, `HandlePushNotification(data)` *(mobile-only)*, `RegisterCustomAction(callback)`.

**Callbacks / events (register side):** generic `RegisterListener(actionName, handler)` plus typed events. Full event catalog: `initialized`, `widgetOpened`, `widgetClosed`, `feedbackSent`, `feedbackSendingFailed`, `outboundSent`, `feedbackFlowStarted`, `customActionTriggered`, `toolExecution`, `notificationCountUpdated`, `registerPushMessageGroup`, `unregisterPushMessageGroup`.

**Enums / models:** `Severity {Low,Medium,High}`, `ActivationMethod {Shake,Screenshot}`, `LogLevel {Error,Warning,Info}`, `SurveyFormat {Survey,SurveyFull}` (serialized `"survey"`/`"survey_full"`), `AIParamType {String,Number,Boolean}`; `GleapUserProperty {userId,name,email,phone,plan,companyName,companyId,avatar,lang,value(double),sla(double),customData}`, `AITool {name,description,response,executionType,parameters[]}`, `AIToolParams {name,description,type,required,enums?}`, `GleapNetworkLog/Request/Response` (§7).

---

## 4. Capability × Platform matrix (parity reality)

✅ full · 🟡 partial/degraded · ❌ not possible on this platform.

| Capability | Unity (mobile/WebGL, via wrap) | Unity Desktop (Core) | MAUI iOS/Android (wrap) | MAUI Win / WPF / WinForms (Core) |
|---|---|---|---|---|
| Core API (init/identify/track/customdata/tags/tickets/AI tools/callbacks) | ✅ | ✅ | ✅ | ✅ |
| Messenger UI (all open/start methods) | ✅ | ✅ (WebView) | ✅ | ✅ (WebView2) |
| Device metadata | ✅ | ✅ | ✅ | ✅ |
| Console log capture | ✅ (`Application.logMessageReceived`) | ✅ (Trace hook) | ✅ | ✅ (Trace hook) |
| Network logging | ✅ mobile / 🟡 WebGL | 🟡 `HttpClient` handler only | ✅ | 🟡 `HttpClient` handler only |
| Screenshot | ✅ | ✅ (framebuffer/window) | ✅ | ✅ (window capture) |
| Replay (periodic screenshots) | ✅ | 🟡 (framebuffer, perf cost) | ✅ | 🟡 |
| Activation: shake | ✅ (mobile) | ❌ (no sensor) | ✅ | ❌ |
| Activation: screenshot-detect | ✅ (mobile) | ❌ | ✅ | ❌ |
| Feedback button | ✅ | ✅ | ✅ | ✅ |
| Crash / silent report | ✅ | ✅ (`AppDomain.UnhandledException`) | ✅ | ✅ |

The only true parity gaps are **desktop-only** (no shake/screenshot sensors) and **network logging depth** in pure-C# (only traffic through the SDK's `HttpClient` handler is captured; native URLSession/OkHttp swizzling cannot be matched from C#). Both are inherent to the platform, not to the design.

---

## 5. The WebView bridge contract (reused verbatim by `ManagedBackend`)

This is the exact protocol the native SDKs use with the web widget. `ManagedBackend` must speak it unchanged so the same messenger-app works.

- **Frame URL:** `https://messenger-app.gleap.io/appnew` (loaded verbatim; all session/config pushed via messages after load).
- **Envelope (both directions):** `{ "name": <string>, "data": <object|string>, "shareToken"?: <string> }`.
- **Native → Web:** `sendMessage(<json>)` executed in the page (`evaluateJavascript`). Banner/Modal surfaces use `appMessage(<json>)`.
- **Web → Native wiring:** iOS `WKScriptMessageHandler` named `gleapCallback`; Android `addJavascriptInterface(bridge, "GleapJSBridge")` with `@JavascriptInterface gleapCallback(String)`. The managed WebView host registers the equivalent handler for its WebView (WebView2: `WebMessageReceived` + injected `window.gleapCallback`/`sendMessage` shims).

### Handshake (mandatory order)
Do nothing until the widget sends **`ping`**. On `ping`, push in order: `config-update` → `session-update` → `prefill-form-data` → `screenshot-update`, then **flush the queued navigation commands**. `widget-status-update {isWidgetOpen:true}` is **not** part of the ping prologue — it is emitted when the widget is actually opened (`Open()`), which in practice lands *after* the handshake/flush. This matches the authoritative implementations: the native Android SDK sends `widget-status-update` ~100 ms after config/session, and the JS SDK sends it on open, not in the ping prologue. (Earlier drafts of this spec listed `widget-status-update` first — that was wrong; corrected after cross-checking Android + JS SDKs during the SP-0 code review.) All outgoing commands issued before `ping` must be queued.

### Web → Native messages
`ping` · `tool-execution` · `frontend-tool-execute` (reply `frontend-tool-result`) · `collect-ticket-data` (reply with assembled data) · `cleanup-drawings` · `close-widget` · `screenshot-updated` (base64 PNG data-URI) · `run-custom-action` (action name + top-level `shareToken`) · `open-url` · `notify-event` (`{type,data}`; `type=="flow-started"` fires feedbackFlowStarted) · `send-feedback` (`{formData, action, outboundId?, spamToken?}` → reply `feedback-sent` / `feedback-sending-failed`).

### Native → Web bootstrap/reply messages
- `widget-status-update` `{ isWidgetOpen: true }`
- `config-update` `{ config, actions, overrideLanguage, isApp: true }`
- `session-update` `{ sessionData: {gleapId, gleapHash, userId, name, email, value, sla, phone, companyName, avatar, plan, companyId}, apiUrl, sdkKey }`
- `prefill-form-data` `<prefill dict>`
- `screenshot-update` `"data:image/png;base64,<...>"`
- `collect-ticket-data` `{ customData, formData, metaData, consoleLog, networkLogs, customEventLog, tags }`
- `feedback-sent` `<server response>` · `feedback-sending-failed` `<error>` · `frontend-tool-result` `<result>`

### Native → Web navigation commands (`hideBackButton = !showBackButton`)
`start-bot` `{botId, hideBackButton}` (empty botId = startConversation) · `open-conversations` `{hideBackButton}` · `open-conversation` `{shareToken}` · `open-checklists` · `open-checklist` `{id, hideBackButton}` · `start-checklist` `{outboundId, hideBackButton}` · `open-news` · `open-news-article` `{id, hideBackButton}` · `open-feature-requests` · `open-helpcenter` · `open-helpcenter-search` `{term, hideBackButton}` · `open-help-article` `{articleId, hideBackButton}` · `open-help-collection` `{collectionId, hideBackButton}` · `ask-ai` `{question, hideBackButton}` · `start-feedbackflow` `{flow, ...}` · `start-survey` `{flow, isSurvey:true, format:"survey"|"survey_full", hideBackButton}`.

### Outbound Banner / Modal (separate WebViews)
URLs `https://outboundmedia.gleap.io` and `.../modal`; send-fn `appMessage`; handlers `gleapBannerCallback` / `gleapModalCallback`. Messages: `banner-loaded`/`modal-loaded` → reply `banner-data`/`modal-data` (modal-data injects `primaryColor`,`backgroundColor`); `*-data-set` (animate in); `*-close`; `*-height` `{height}`; plus action triggers `start-conversation` `{botId}`, `show-form` `{formId}`, `show-survey` `{formId, surveyFormat}`, `show-news-article` `{articleId}`, `show-checklist` `{checklistId}`, `show-help-article` `{articleId}`, `open-url`, `start-custom-action` `{action}`.

---

## 6. API endpoints & session/config flow (for `ManagedBackend`)

**Bases:** API `https://api.gleap.io` · WS `wss://ws.gleap.io` · frame `https://messenger-app.gleap.io/appnew` · banner `https://outboundmedia.gleap.io` · modal `.../modal`.

**Auth headers on every call:** `Api-Token: <sdkKey>`; once a session exists also `Gleap-Id`, `Gleap-Hash`.

**Init order:**
1. Set token; start console-log capture; start screenshot listener.
2. `POST /sessions` — body `{ lang, platform, deviceType }` (deviceType ∈ mobile/tablet/desktop; platform e.g. `"windows"`, `"unity-ios"` — see §8 note). Send stored guest `Gleap-Id`/`Gleap-Hash` to merge. Persist returned `gleapId`/`gleapHash`.
3. On session success → `GET /config/{token}?lang={lang}` → split into `flowConfig` (→ `config`) + `projectActions`; apply network/replay/activation settings; push `config-update`; fire `configLoaded`/`initialized`.

**Identity:** `POST /sessions/identify` (only when data changed); `POST /sessions/partialupdate` `{data, ws, type, sdkVersion}` for update; `clearIdentity` clears persisted ids + starts a fresh guest session.

**Event streaming / outbound polling:** `POST /sessions/ping` `{time, events, opened, ws, type, sdkVersion}` (interval 3s WS / 10s poll) → response `{ a: actions[], u: unreadCount }`; action `actionType` ∈ notification/banner/modal/else(survey|feedback-flow). WebSocket: `wss://ws.gleap.io?gleapId=&gleapHash=&apiKey=<token>&sdkVersion=`.

**Report submission:** `POST /bugs/v2` with assembled JSON (`metaData, formData, customData, consoleLog, networkLogs, customEventLog, tags, screenshotUrl, replay, attachments`) minus keys in `excludeData`; images/files uploaded first to obtain URLs.

**Push topic:** `gleapuser-<gleapHash>`.

---

## 7. Data collection details (parity reference)

**Metadata fields** (union of iOS + Android; `ManagedBackend` fills what the platform exposes): `deviceName, deviceModel, deviceIdentifier, bundleID, systemName, systemVersion, buildVersionNumber, releaseVersionNumber, sessionDuration, lastScreenName, preferredUserLocale, sdkType, sdkVersion, buildMode, batteryLevel, phoneChargingStatus, batterySaveMode, totalDiskSpace, totalFreeDiskSpace, devicePixelRatio, screenWidth, screenHeight`; Android-extra: `networkStatus, appRAMUsage, totalRAM`.

**Network log model:** `{ type (HTTP method), url, date (ISO), duration (ms), success (bool), request:{payload, headers}, response:{status, statusText, responseText} }`. Guards to replicate: payload/response > **1,000,000 bytes** → `"<payload_too_large>"` / `"<response_too_large>"`; non-serializable bodies → `"<response_type_not_supported>"`; ring buffer (~20 in interceptors / native trims oldest); blacklist drops matching URLs, `propsToIgnore` strips props. Managed capture = a `DelegatingHandler` on the app's `HttpClient`.

**Replay:** periodic screenshots on a timer (default interval 5s, overridable by config `replaysInterval`); ring buffer **60 steps**; skipped while widget open; paused in background. Sent as `{ replay: { interval, frames: [urls] } }`.

**Screenshot:** capture current surface just before the widget opens; user-edited version arrives via `screenshot-updated`; prefer the edited one when attaching.

**Activation:** `SHAKE` (accelerometer) and `SCREENSHOT` (OS screenshot detection) — mobile-only; desktop supports the feedback button only.

**Crash / silent report:** `type=CRASH`, `silent=true`, severity LOW/MEDIUM/HIGH, default `excludeData = {screenshot, replays, attachments}`; via `/bugs/v2`.

---

## 8. Open questions / coordination items

1. **SDK-type / application-type registration (server + native).** Native SDKs tag `applicationType` (FLUTTER/REACTNATIVE/CORDOVA/CAPACITOR…) and `sdkType` strings (`Flutter/iOS`, etc.). The C# SDK needs new values (`Unity/iOS`, `Unity/Android`, `Unity/WebGL`, `MAUI/iOS`, `MAUI/Android`, `MAUI/Windows`, `NET/Windows`). Confirm the **native SDKs and backend/analytics** accept and correctly attribute these. → owner: Server + native SDK teams.
2. **Unity WebView plugin decision (desktop standalone).** No first-party Unity WebView. Options: commercial (Vuplex, 3D WebView) vs. embedding a platform WebView. Licensing cost + redistribution terms must be cleared. WebGL avoids this (uses JS SDK). Consoles remain unsupported.
3. **`messenger-app.gleap.io` compatibility check** in WebView2 and in the chosen Unity WebView plugin (iOS/Android WebView is already proven). → owner: Web/Messenger-App team.
4. **Network-logging expectations on pure-C# targets.** Confirm "only SDK-`HttpClient` traffic" is acceptable for desktop; document the limitation.
5. **`includeFeatureRequests` / knowledge-scope and other server-side toggles** — out of SDK scope, ignore unless surfaced.
6. **Versioning discipline.** Wrappers must pin the native SDK version (Flutter pins `15.3.0`). Define the C# release ↔ native SDK version alignment policy up front.

---

## 9. Sub-project decomposition (each: own spec → plan → build → example app → CI → docs)

| # | Sub-project | Rationale for order |
|---|---|---|
| **SP-0** | **Shared contracts + `Gleap.Core` engine** — facade, `IGleapBackend`, `ApiClient`, `SessionManager`, `ConfigManager`, `WebViewBridge` (headless-testable), collectors, buffers, DTOs, serialization, versioning. Unit-tested against a headless bridge simulator. | Foundation + reference implementation. Everything else depends on it or mirrors its API. |
| **SP-1** | **Windows Desktop (WPF/WinForms) = `Gleap.Core` + `Gleap.WebView2`.** First shippable product; validates Core + bridge + WebView end-to-end against the live messenger-app. | Zero plugin risk (WebView2 first-class), cheapest to verify, proves the managed path. |
| **SP-2** | **Unity** — facade + native plugins (iOS ObjC, Android JNI) + WebGL `.jslib` + desktop via Core+WebView plugin. | Highest value + highest risk; mobile = proven Firebase-Unity wrap pattern; desktop reuses SP-0/SP-1. |
| **SP-3** | **MAUI** — `Gleap.iOS.Binding` + `Gleap.Android.Binding` + Windows via `Gleap.WebView2`. | Broadest coverage; bindings are the main effort, done after Core is proven. |

**Recommended next step:** brainstorm/plan **SP-0** in its own cycle.

---

## 10. Risks

- **Unity desktop WebView** (plugin licensing) — mitigated by WebGL/mobile using existing SDKs; desktop is the only affected slice.
- **MAUI iOS binding** over an Objective-C SDK (API projection via Objective Sharpie) is non-trivial.
- **Network-logging depth** on pure-C# targets is inherently below native (no swizzling).
- **Native SDK version pinning** creates a maintenance cadence obligation across all wrapped targets.
- **Console/consoles** (PS/Xbox/Switch) unsupported — must be stated openly in docs.
