# Gleap C# SDK

Cross-platform C# SDK for [Gleap](https://gleap.io) — customer support, in-app messenger, bug
reporting, feature requests, surveys and more — for **Unity**, **.NET MAUI**, and **Windows
Desktop (WPF/WebView2)**.

> **Status: early access.** The platform-agnostic core is implemented, unit-tested
> (129 tests, `-warnaserror` clean), and — the important part — **validated live against the real
> Gleap server and web widget**: the bridge handshake, `config`/`session`, messenger navigation,
> feedback submit (`/bugs/v2`), and the banner/modal outbound protocol were all confirmed against the
> live services. The host-side behaviour now **mirrors the native iOS/Android SDKs** (studied from
> their source), not just inference. Windows (WPF/WebView2) renders the live messenger natively; the
> MAUI and Unity bindings compile (their mobile/WebGL targets need the platform SDKs to build + run on
> a device). See [Status & parity](#status--parity). Past "does it work", not yet a 1.0.

## How it works

The real chat/messenger/help-center/survey UI is the same **web widget** used by all Gleap SDKs
(`messenger-app.gleap.io`), hosted in a native WebView. This SDK follows the industry-standard
pattern (Firebase/Sentry/AppsFlyer-style): a **stable public `Gleap` facade** over a small,
platform-agnostic engine (`Gleap.Core`), with thin per-platform packages supplying the WebView
host, persistence, metadata, and capture.

```
Gleap  (public facade — identical API everywhere)
  -> IGleapBackend
      -> ManagedBackend  (pure C# engine in Gleap.Core: session, config, the web-widget
                          bridge protocol, data collection, feedback, outbound polling, callbacks)
          -> IWebViewChannel / IHttpTransport / IKeyValueStore / IMetadataProvider / IScreenshotProvider
             (platform packages implement these seams)
```

## Repository layout

```
src/Gleap.Core/              .NET Standard 2.0 engine - the reusable heart (unit-tested)
src/Gleap.WebView2/         Windows Desktop binding (WebView2) - net8.0-windows10.0.19041.0
examples/Gleap.Sample.Wpf/  WPF sample: a floating launcher + messenger overlay (GleapMessenger)
maui/Gleap.Maui/            .NET MAUI binding (reuses Gleap.Core) - net10.0-*
examples/Gleap.Sample.Maui/ MAUI sample app
unity/com.gleap.sdk/        Unity UPM package (reuses Gleap.Core; Core DLLs bundled)
tests/Gleap.Core.Tests/     xUnit tests for the core
docs/                       design specs + implementation plans (docs/superpowers)

Gleap.sln           Core + tests - builds cross-platform (macOS/Linux/Windows)
Gleap.Windows.sln   Core + WebView2 + WPF sample - Windows only
```

## Build & test (core)

Requires the **.NET 8 SDK** (the repo also builds on the .NET 10 SDK). The WPF sample additionally
needs the **.NET 8 Desktop Runtime**; the MAUI binding needs the **`maui` workload**
(`dotnet workload install maui`).

```bash
dotnet test Gleap.sln                   # build + run the core tests (129)
dotnet build Gleap.sln -warnaserror     # quality gate (0 warnings)
dotnet format Gleap.sln --verify-no-changes
```

`Gleap.Windows.sln` (WebView2/WPF) builds on **Windows** only - see
[`WINDOWS-RUNBOOK.md`](WINDOWS-RUNBOOK.md) to run the sample app against the live widget.
Unity and MAUI have their own READMEs
([`unity/com.gleap.sdk/README.md`](unity/com.gleap.sdk/README.md),
[`maui/Gleap.Maui/README.md`](maui/Gleap.Maui/README.md)).

## Quick start (Windows Desktop)

```csharp
using GleapSDK;
using GleapSDK.WebView2;

// Drop the launcher control into your window: it floats a button (styled from your project config)
// that slides the messenger in as an overlay and back, like the web SDK. Session/config load lazily.
var messenger = new GleapMessenger { SdkKey = "YOUR_SDK_KEY" };
rootGrid.Children.Add(messenger);

// Drive it from anywhere via the stable facade:
Gleap.StartBot("");                     // or StartConversation / OpenHelpCenter / OpenNews / ShowSurvey
await Gleap.IdentifyContactAsync("user-123", new GleapSDK.Models.GleapUserProperty { Email = "a@b.c" });
Gleap.TrackEvent("checkout_completed");
```

(For full control you can instead host a `WebView2` yourself and call
`GleapWebView2Host.AttachAsync(webView, "YOUR_SDK_KEY")`.)

## Status & parity

Honest state (✅ = done + verified where noted; the host-side behaviour was checked against the
native iOS/Android SDK source):

| Capability | State |
|---|---|
| Messenger / Bot / AI chat / Help Center / News / Surveys / Classic Forms | ✅ **live-validated** — handshake, `config`/`session`, and navigation confirmed against the real `messenger-app.gleap.io` widget; renders natively in the WPF app |
| Identify / updateContact / clearIdentity, events, logs, custom data, ticket attributes, tags | ✅ core done |
| Feedback submit (`/bugs/v2`) + attachments + replay | ✅ **`/bugs/v2` confirmed live** (HTTP 201) |
| Interactive screenshot (capture → annotate in-widget → attach) | ✅ wired to iOS parity (`screenshot-update` / `screenshot-updated` / `cleanup-drawings`) |
| Silent crash report; callbacks (`RegisterListener`); config setters; `open-url` (system browser) | ✅ done |
| Outbound polling (`/sessions/ping`, unread count, auto survey/feedback-flow) | ✅ core done + auto-polled by the Windows launcher |
| Outbound **Banner + Modal** rendering | ✅ Windows host built to the native contract; the `appMessage` / `banner-data` / `modal-data` protocol **validated live** against `outboundmedia.gleap.io` (end-to-end with a real configured outbound: pending) |
| AI / Frontend tools (`RegisterAgentTool(name, handler)`) | ✅ full execution round-trip — runs the registered handler on `frontend-tool-execute` and posts `frontend-tool-result` back to the agent (matches the JS SDK; missing-handler/error/empty folded into the result string, deduped by `toolCallId`) |
| **Windows** (WPF/WebView2) | ✅ runs live — composition-hosted messenger + native launcher (styled from config); `-warnaserror` clean |
| **.NET MAUI** (Android / iOS / Windows) | ✅ `MauiWebViewChannel` per platform + sample; **net10.0-windows compiles**; Android/iOS build+run need the platform SDKs (Android Studio / a Mac) |
| **Unity** (UPM) | ✅ package + bundled Core DLLs + IL2CPP-safe `NewtonsoftJsonSerializer` (camelCase + null-omit + raw `JsonElement` pass-through, and honors System.Text.Json `[JsonPropertyName]` so it stays byte-identical to the STJ output) + `link.xml` IL2CPP strip-protection + plugin-agnostic channel; the Unity-dependent runtime (PlayerPrefs/metadata marshalled onto the main-thread pump) is verified on Unity editor import |
| In-app `notification` toasts, mobile activation (shake/screenshot), push, WebSocket, Unity WebGL (JS-SDK path) | not built (platform/runtime-dependent) |

Design docs and per-feature implementation plans live under `docs/superpowers/`.

## License

Copyright (C) 2026 Gleap GmbH. See [`LICENSE.md`](LICENSE.md). Proprietary - use as-is; modifications
and reselling require written permission from Gleap GmbH.
