# Gleap C# SDK — Windows (SP-1) Runbook

Runs the WPF sample that hosts the live Gleap widget via WebView2. **Windows only** (the
`net8.0-windows10.0.19041.0` / WPF / WebView2 projects cannot build on macOS/Linux).

## Prerequisites
- Windows 10/11.
- **.NET 8 SDK** (the repo also builds on the .NET 10 SDK) + the **.NET 8 Desktop Runtime** (to run the
  `net8.0-windows` WPF app). `dotnet --version` should be >= 8.
- WebView2 Runtime (preinstalled on current Windows; else install the Evergreen Runtime from Microsoft).
- Optional: your own Gleap **project SDK key** (a public demo key is already hard-coded in the sample so
  it runs out of the box).

## Build & run
```
cd CSharp-SDK
dotnet run --project examples\Gleap.Sample.Wpf
```
To use your own project, set `MainWindow.DemoSdkKey` in the sample (or the `gleap.sdkkey` env var).

Optional strict build (quality gate): `dotnet build Gleap.Windows.sln -warnaserror`.

First build note: `Microsoft.Web.WebView2` is referenced as `1.0.*` (floats to latest). After the
first successful restore, pin it to the concrete version selected (`dotnet list
src\Gleap.WebView2 package`) for reproducible builds.

## What you should see
The sample is a **product-style demo** (a mock "Acme Dashboard"), not a control panel. Gleap is
initialized in code — no setup UI — via the reusable `GleapMessenger` control:

1. The window opens showing the mock dashboard with a **floating Gleap launcher** in the bottom-right
   (round, coloured from the project's `buttonColor`, styled with its `buttonLogo` if set). No flash —
   the messenger is preloaded invisibly.
2. **Click the launcher** → the messenger **slides up and fades in** as a rounded overlay with a soft
   shadow; the launcher morphs to a **✕** toggle. Click ✕ (or the widget's own ✕) → it slides back.
3. In the widget: type to the **AI bar / bot** and confirm replies stream; open **Messages / News /
   Roadmap / Help** and confirm content renders and articles open.
4. Start **Report an issue** (classic form) → fill it in and submit → the report reaches Gleap
   (`send-feedback` → `POST /bugs/v2`), with the app screenshot attached.

## Acceptance (SP-1)
- [ ] Launcher shows bottom-right (config-styled); no startup flash.
- [ ] Open/close: launcher ⇄ messenger overlay slides + fades smoothly; ✕ closes it.
- [ ] Bot / Help Center / News / Roadmap navigate and are interactive.
- [ ] The messenger keeps a fixed height and scrolls internally (shrinks only if the window is short).
- [ ] Submitting a classic form completes (a ticket appears in the Gleap project) with a screenshot.
- [ ] `Gleap.TrackEvent` / `SetCustomData` raise no errors; `collect-ticket-data` prefills a started form.

Outbound **banner/modal** only appear when the project has a matching outbound configured to fire
(the control auto-polls every 5s); the protocol is validated but end-to-end needs a real outbound.

## If it fails
- Blank window / launcher missing: check the WebView2 Runtime is installed (an exception dialog states
  if `CoreWebView2` could not start) and that `net8.0-windows10.0.19041.0` restored.
- Messenger area loads but stays unstyled/empty: right-click → **Inspect** (WebView2 DevTools) → check
  the Console and confirm `api.gleap.io/sessions` (201) and `/config/…` (200) succeed — a stuck
  handshake is usually a bootstrap-message shape to fix in `Gleap.Core`.
