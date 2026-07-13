# Gleap C# SDK

Cross-platform C# SDK for [Gleap](https://gleap.io) — customer support, in-app messenger, bug
reporting, feature requests, surveys and more — for **Unity**, **.NET MAUI**, and **Windows
Desktop (WPF/WebView2)**.

> **Status: early / pre-release.** The platform-agnostic core is implemented and unit-tested
> (121 tests, `-warnaserror` clean), but **nothing has been validated against the live Gleap
> service or widget yet**, and the platform bindings have not been compiled on their target
> runtimes. Server/message contracts are reverse-engineered from the native iOS/Android/JS SDKs.
> See [Status & parity](#status--parity) for the honest breakdown. Not yet production-ready.

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
src/Gleap.Core/             .NET Standard 2.0 engine - the reusable heart (unit-tested)
src/Gleap.WebView2/         Windows Desktop binding (WebView2) - net8.0-windows
examples/Gleap.Sample.Wpf/  WPF sample app (live test harness)
maui/Gleap.Maui/            .NET MAUI binding (reuses Gleap.Core)
unity/com.gleap.sdk/        Unity UPM package (reuses Gleap.Core)
tests/Gleap.Core.Tests/     xUnit tests for the core
docs/                       design specs + implementation plans (docs/superpowers)

Gleap.sln           Core + tests - builds cross-platform (macOS/Linux/Windows)
Gleap.Windows.sln   Core + WebView2 + WPF sample - Windows only
```

## Build & test (core)

Requires the **.NET 8 SDK**.

```bash
dotnet test Gleap.sln                   # build + run the core tests
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

// `WebView` is a Microsoft.Web.WebView2.Wpf.WebView2 control in your window.
await GleapWebView2Host.AttachAsync(WebView, "YOUR_SDK_KEY");
Gleap.Open();
Gleap.StartBot("");                     // or StartConversation / OpenHelpCenter / OpenNews / ShowSurvey
await Gleap.IdentifyContactAsync("user-123", new GleapSDK.Models.GleapUserProperty { Email = "a@b.c" });
Gleap.TrackEvent("checkout_completed");
```

## Status & parity

Unit-verified against fakes is not the same as validated live. Honest state:

| Capability | State |
|---|---|
| Messenger / Bot / AI chat / Help Center / News / Checklists / Feature Requests / Surveys / AskAI / Classic Forms | core done, not live-tested |
| Identify / updateContact / clearIdentity, events, logs, custom data, ticket attributes, tags | core done |
| Feedback submit (`/bugs/v2`) + silent crash + attachments + screenshot/replay in payload | core done (contract inferred) |
| Callbacks (`RegisterListener`), config setters (language, prefill, logging toggles, ...) | core done |
| Outbound polling (`/sessions/ping`, notification count, auto survey/feedback-flow) | core done; timer wired in the WPF sample |
| AI tools declaration (`SetAiTools`) | core done (execute-reply round-trip: follow-up) |
| Screenshot / replay capture | Windows done (WPF); Unity/MAUI providers: follow-up |
| Windows / Unity / MAUI packages | authored, NOT yet compiled on target runtimes |
| Banner/Modal outbound rendering, Unity/MAUI WebView channels, mobile activation (shake/screenshot), push, WebSocket | not built (runtime-dependent - next after first live validation) |

Design docs and per-feature implementation plans live under `docs/superpowers/`.

## License

Copyright (C) 2026 Gleap GmbH. See [`LICENSE.md`](LICENSE.md). Proprietary - use as-is; modifications
and reselling require written permission from Gleap GmbH.
