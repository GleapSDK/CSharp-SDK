# Gleap Omnis SDK — design

**Date:** 2026-07-15
**Status:** .NET layer implemented & verified; Omnis glue authored as reference (needs on-Omnis smoke test).
**Target customer:** native Omnis Studio (Windows) app, Omnis 11 → 12.

## Goal

Give a native Omnis Studio (Windows, fat-client) app the Gleap capabilities of the iOS / C# SDKs — a
"ready SDK", not an MVP — **without** reimplementing the widget UI and **without** shipping the internal C#
SDK or a .NET-runtime/WebView2 dependency to the customer.

## Key finding that shapes the architecture

The Gleap widget UI is a web app ("the frame"). Every native SDK (iOS, Android, C# WebView2) is a **thin
native host** around it, not a reimplementation. Two facts (verified in-repo) make an Omnis host viable:

1. **Omnis' OBrowser is CEF/Chromium** on Windows (since Studio 8.x; 10.22 = CEF 103), not legacy IE — so
   the widget renders — and it has a documented two-way bridge for **html-controls**: `$callmethod` (host→JS)
   and `sendMessageToFatClient` → `evControlEvent` (JS→host). OBrowser cannot inject script into an external
   page, only into an Omnis html-control it serves.
2. **The frame is transport-agnostic.** It speaks only `postMessage` to its parent and knows nothing of
   `GleapJSBridge`/`webkit`. "Native mode" is just a `config-update {isApp:true}` flag. Gleap's `/appnew` is
   itself only a **wrapper page** that embeds the frame as a cross-origin iframe and relays the protocol to
   the native bridge (`Messenger-App/public/appnew.html`).

⇒ We host **our own local wrapper page** (an Omnis html-control) that embeds the frame as a cross-origin
iframe and relays via `postMessage`, with Omnis' `$callmethod`/`evControlEvent` playing the role
`webkit`/`GleapJSBridge` play for the native SDKs. **No change to Messenger-App is required.**

## Architecture

```
Omnis app → OBrowser (CEF) → gleap-omnis-bridge.html → iframe → Gleap frame (messenger-app.gleap.io)
                │  $callmethod / evControlEvent
           Omnis 4GL glue → COM → Gleap.Omnis.dll → ManagedBackend (Gleap.Core, reused verbatim)
```

**Reuse:** the entire `Gleap.Core` (netstandard2.0) protocol engine — bridge handshake, ticket assembly,
session, config, outbound polling, and the `GleapWebSocket` realtime channel — is used unchanged and stays
in sync with the C# SDK. Only the thin Omnis platform seam is new.

### New components (`src/Gleap.Omnis`, net48 shipped + net8.0 for tests)

| Component | Responsibility |
|-----------|----------------|
| `GleapOmnisClient` (COM, ProgId `Gleap.OmnisClient`) | Wires `ManagedBackend` with the Omnis providers; exposes the full Gleap facade as string/scalar COM methods; owns the app-event queue and lifecycle. |
| `OmnisWebViewChannel : IWebViewChannel` | Unwraps `sendMessage(<json>)` → payload queue drained by Omnis (`DequeueWidgetMessage`); `PushMessage` feeds `evControlEvent` JSON back into Core. |
| `Win32ScreenshotProvider : IScreenshotProvider` | `PrintWindow` on the Omnis top-level HWND → base64 PNG; primary-screen fallback; failures degrade to no-screenshot. |
| `OmnisMetadataProvider : IMetadataProvider` | Windows device/app metadata; sdkType `NET/Omnis`; app identity supplied by the glue. |
| `OmnisFileKeyValueStore : IKeyValueStore` | Persists session ids under `%LOCALAPPDATA%\Gleap\omnis-session.json` for returning-user continuity. |
| `web/gleap-omnis-bridge.html` | The OBrowser html-control wrapper: embeds the frame, relays `postMessage` ⇆ Omnis callback. |
| `omnis/gleap-omnis-glue.md` | Reference Omnis 4GL: construct/init, drain timer, `evControlEvent` handler, open/identify/…. |

### COM boundary design

All arguments are strings/scalars; structured data crosses as JSON (parsed internally). Async facade calls
are fire-and-forget (never block Omnis' thread); nothing throws across COM (errors are logged) so a bad
argument can't destabilise Omnis. Transport is **poll-based** (two drain queues on the glue's timer), which
is the most robust option across Omnis COM-event configurations — no connection-point events required.

## Capability scope

**In (parity with iOS/C#):** messenger/live chat, bug & feedback flows, surveys, feature requests, help
center, news, checklists, identify + user data, custom data / ticket attributes / tags, prefill, attachments,
silent reports, language/endpoints, events, native window screenshot, device metadata, console log, network
log (opt-in), **WebSocket realtime**, notification/unread count.

**Out by design (as iOS/C#):** product tours (web-DOM only), shake/screenshot-gesture activation (no desktop
concept), APNs push (realtime covers live updates while open).

**Deferred (documented):** launcher button + in-app toasts + banner/modal chrome are native UI the glue
renders (fed by `notificationCount`/`outboundSent`), mirroring the C# WPF layer; the in-widget screenshot
annotation editor (lives in Gleap's `/appnew`); agent-tool result round-trip (a shared `Gleap.Core` gap —
`toolExecution` is surfaced but result submission isn't wired in Core yet).

## Packaging

Ship the **net48** build: `Gleap.Omnis.dll` + `Gleap.Core.dll` + `System.Text.Json.dll` (+ deps) + the bridge
HTML + the glue reference. net48 is on every modern Windows ⇒ no runtime install; COM-visible ⇒ regasm or
reg-free COM; OBrowser is CEF ⇒ no WebView2. The customer never touches the C# SDK — `Gleap.Core` ships as a
private binary (same protocol as the public JS/iOS SDKs; nothing secret).

## Verification

- `dotnet build Gleap.Omnis.sln -warnaserror` — clean on **net48 and net8.0**, 0 warnings (inherits the repo
  `Directory.Build.props` analyzer/nullable/codestyle gates).
- `Gleap.Omnis.Tests` — 19 unit tests green (channel unwrap/queue/FIFO/inbound; facade pre-init guards and
  malformed-JSON tolerance).
- Reused `Gleap.Core` baseline — 209 tests green.
- **Not executable on the build machine (macOS, no Omnis):** COM activation, Win32 screenshot, the OBrowser
  round-trip, and the Omnis 4GL glue. Covered by the on-Omnis smoke test in `README.md` (customer runs it).

## Follow-ups

1. Ready-made native Omnis launcher + unread badge (currently glue-rendered).
2. Port the screenshot annotation editor from `/appnew` (currently attaches the shot without in-widget draw).
3. Agent-tool result round-trip — needs a `Gleap.Core` addition (shared with the C# SDK).
4. Optionally point `Gleap.WebView2` at a shared `Gleap.Windows.Common` to de-duplicate the metadata/store
   providers (kept per-package here to avoid touching the WPF project, which can't be built on macOS).
