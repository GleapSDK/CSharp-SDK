# Gleap Omnis SDK

Gleap for **native Omnis Studio (Windows) desktop applications**, hosted in Omnis' built-in CEF
`OBrowser` control. It gives an Omnis app the same Gleap capabilities as the iOS / C# SDKs — messenger,
bug reporting with a native screenshot, surveys, feature requests, help center, news, checklists, user
identity, custom data, realtime chat — without reimplementing the widget UI.

## How it works

The Gleap widget UI is a web app (the "frame"). The native SDKs are a thin host around it. This SDK is
that host for Omnis:

```
Omnis 11/12 app
 └─ OBrowser (CEF, built in)  ── loads ──►  gleap-omnis-bridge.html  ── iframe ──►  Gleap frame (messenger-app.gleap.io)
        │  $callmethod("gleapDeliver", …)  ▲  evControlEvent (sendMessageToFatClient)
   Omnis 4GL glue  (Gleap object class)
        │  COM interop
   Gleap.Omnis.dll  (this SDK, COM-visible)
        ├─ GleapOmnisClient      → drives ManagedBackend (the whole protocol engine, reused verbatim)
        ├─ OmnisWebViewChannel   → bridges $callmethod ⇆ evControlEvent
        ├─ Win32ScreenshotProvider → PrintWindow on the Omnis window
        └─ GleapWebSocket, metadata, session store (reused / Windows-native)
```

Every protocol detail (bridge handshake, ticket assembly, session, config, realtime) lives in the reused
`Gleap.Core` and stays in sync with the C# SDK. Only the thin Omnis seam is new.

## What the customer receives

A self-contained bundle — **no C# SDK, no .NET runtime install, no WebView2**:

| File | Purpose |
|------|---------|
| `Gleap.Omnis.dll` | The COM-visible SDK (net48). |
| `Gleap.Core.dll` | The reused protocol engine (private dependency). |
| `System.Text.Json.dll` (+ its small dependency set) | JSON, used by Gleap.Core. |
| `web/gleap-omnis-bridge.html` | The OBrowser html-control bridge page. |
| `omnis/gleap-omnis-glue.md` | The Omnis 4GL reference to import/adapt. |

Target framework is **.NET Framework 4.8**, which ships with every modern Windows — so nothing extra to
install. (A `net8.0` build also exists, used for cross-platform unit-testing; ship the **net48** one.)

## Build

```bash
dotnet build Gleap.Omnis.sln -c Release
# net48 output: src/Gleap.Omnis/bin/Release/net48/  (Gleap.Omnis.dll, Gleap.Core.dll, System.Text.Json.dll, web/)
```

## Deploy on the Windows / Omnis machine

1. Copy the net48 output (all DLLs + the `web/` folder) next to the Omnis library, e.g.
   `…\YourApp\gleap\`.
2. **Register the COM object** (one of):
   - Classic: `regasm Gleap.Omnis.dll /codebase` (elevated). Registers ProgId `Gleap.OmnisClient`.
   - Registration-free COM via a side-by-side manifest (no admin) — preferred for locked-down installs.
3. Import the Omnis 4GL glue (see `omnis/gleap-omnis-glue.md`) and point its bridge-page path at the
   copied `web/gleap-omnis-bridge.html` (or call `BridgePagePath()` to get it at runtime).
4. Set your Gleap **SDK key** in the glue.

## Smoke test (the go-live check)

Run these on the Omnis box; they exercise the parts that only run on Windows/Omnis:

1. Launch the app → open the Gleap launcher → the messenger renders in the OBrowser field.
2. In the DevTools of OBrowser (CEF remote-debugging port, default `5989`) confirm the frame received a
   `config-update` with `isApp:true` (native mode) and a `session-update`.
3. Send a chat message → it reaches your Gleap inbox; a reply appears in realtime (WebSocket).
4. Start a bug report → the attached screenshot shows the **Omnis app window** (call `CaptureScreenshot()`
   just before showing the widget so the shot is the app, not the widget).
5. Identify a user (`IdentifyContact`) → the conversation is attributed to that contact.

## Capability parity

**Included (parity with iOS / C#):** messenger & live chat, bug/feedback flows, surveys, feature requests,
help center, news, **checklists + live checklist-progress events**, identify + user data, custom data /
ticket attributes / tags, prefill, attachments, silent reports, language & endpoints, events, **native
window screenshot**, device metadata, console log (`Log`), network log (opt-in), **WebSocket realtime**,
notification/unread count, **feedback-button visibility (config + `ShowFeedbackButton`)**.

**Provided as a native 4GL reference (`omnis/gleap-omnis-launcher.md`):** the **launcher button**, the
**unread badge**, the **in-app notification preview toasts**, and the animated open/close. The SDK surfaces
the data/events (`notificationCount`, `feedbackButtonVisibility`, `outbound`, `checklist*`); the glue draws
the native chrome — exactly the division the C# SDK uses (its WPF layer draws the same chrome).

**Not included — by design, matching the native SDKs / desktop reality:**
- **Product tours** — web/DOM-only overlays; absent in the iOS SDK too.
- **Shake / screenshot-gesture activation** — no desktop equivalent (`SetActivationMethods` is not exposed).
- **APNs push** — desktop N/A; realtime runs over the WebSocket while the app is open.

**Deferred (not yet exposed — shared gaps or larger surfaces):**
- **Outbound banner / modal surfaces** — surfaced via the `outbound` app event, but rendering them needs a
  second OBrowser field loading `outboundmedia.gleap.io`; the reference launcher renders notification toasts
  but not banners/modals yet.
- **Session replay** — the C# SDK captures app frames on a timer (`CaptureReplayFrameAsync`); the Omnis COM
  facade does not expose replay-frame capture yet, so replays are off. Follow-up.
- **In-widget screenshot annotation editor** — lives in Gleap's `/appnew` wrapper; this SDK attaches the
  native screenshot but does not port that editor.
- **Agent-tool result round-trip** — `toolExecution` is surfaced, but returning a tool result is not yet
  wired in `Gleap.Core` (a shared limitation, not Omnis-specific).

## Status

The .NET layer is complete and verified (build clean with `-warnaserror` on net48 + net8.0; 19 unit tests
green). The Omnis 4GL glue, COM registration, and the live OBrowser round-trip run only on Windows/Omnis and
are covered by the smoke test above — they could not be executed on the build machine (macOS, no Omnis).
