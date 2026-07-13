# Gleap C# SDK — SP-0 Part 2c: Callbacks & events

> **For agentic workers:** Implement task-by-task with TDD. Steps use `- [ ]`.

**Goal:** Add the public callback API (`RegisterListener`) and wire the SDK's lifecycle/interaction events to it: `initialized`, `widgetOpened`, `widgetClosed`, `feedbackSent`, `feedbackFlowStarted`, `customActionTriggered`, `toolExecution`.

**Architecture:** A small `GleapEventDispatcher` (name → handlers) owned by `ManagedBackend`. The backend emits at the right moments (init complete, open/close, feedback posted) and by subscribing to the bridge's incoming events. Exposed via `Gleap.RegisterListener`.

**Scope (2c):** callbacks only. **Explicitly deferred (platform/timer, not headless-testable well):** `/sessions/ping` polling loop, WebSocket, outbound banner/modal rendering, replay/screenshot capture + `notificationCountUpdated`/`outboundSent` (these depend on the poll loop / platform WebViews — a later "realtime" sub-project with the real runtime).

**Tech Stack:** builds on SP-0 Part 1 + 2a + 2b + Core-API-completion. `Gleap.sln` (macOS), 89 tests green.

**Reference:** parent spec §3 (event catalog).

**Convention:** `CSharp-SDK/`, .NET at `~/.dotnet` (prefix `export PATH="$HOME/.dotnet:$PATH" && export DOTNET_CLI_TELEMETRY_OPTOUT=1 && `). Always pass `Gleap.sln`. Commits **local only**. One public type per file; `sealed`; file-scoped namespaces; XML docs.

---

## Task 1: Bridge — tool-execution incoming event

**Files:** Modify `src/Gleap.Core/Bridge/WebViewBridge.Incoming.cs`; Test `tests/Gleap.Core.Tests/WebViewBridgeToolExecutionTests.cs`.

- [ ] **Step 1 — failing test.** `WebViewBridgeToolExecutionTests.cs`: build a bridge, subscribe `bridge.ToolExecutionRequested += _ => raised = true;`, `ch.SimulateIncoming("{\"name\":\"tool-execution\",\"data\":{\"tool\":\"x\"}}")`, assert raised. Run `dotnet test Gleap.sln --filter WebViewBridgeToolExecutionTests` → FAIL.

- [ ] **Step 2 — implement.** In `WebViewBridge.Incoming.cs`: add event alongside the others: `public event System.Action<System.Text.Json.JsonElement>? ToolExecutionRequested;` and add a case before `case "send-feedback":`:
```csharp
            case "tool-execution":
                ToolExecutionRequested?.Invoke(msg.Data);
                break;
```

- [ ] **Step 3 — pass + commit.** Run filter → PASS; also `--filter WebViewBridgeIncomingTests` (no regression). Commit: `feat: bridge raises ToolExecutionRequested on tool-execution`.

---

## Task 2: `GleapEventDispatcher`

**Files:** Create `src/Gleap.Core/Events/GleapEventDispatcher.cs`; Test `tests/Gleap.Core.Tests/GleapEventDispatcherTests.cs`.

- [ ] **Step 1 — failing test.** `GleapEventDispatcherTests.cs`:
  - `Emit_InvokesRegisteredHandlers`: register two handlers for `"widgetOpened"`, `Emit("widgetOpened", null)`, assert both ran.
  - `Emit_PassesData`: register `"x"` capturing data, `Emit("x", "payload")`, assert captured == "payload".
  - `Emit_UnknownEvent_NoOp`: `Emit("nope", null)` does not throw.
  Run → FAIL.

- [ ] **Step 2 — implement.** Create `src/Gleap.Core/Events/GleapEventDispatcher.cs`:
```csharp
using System;
using System.Collections.Generic;
using System.Linq;

namespace GleapSDK.Events;

/// <summary>Registry of app-supplied event listeners, keyed by Gleap event name.</summary>
public sealed class GleapEventDispatcher
{
    private readonly Dictionary<string, List<Action<object?>>> _listeners = new();

    public void Register(string eventName, Action<object?> handler)
    {
        if (!_listeners.TryGetValue(eventName, out var handlers))
        {
            handlers = new List<Action<object?>>();
            _listeners[eventName] = handlers;
        }
        handlers.Add(handler);
    }

    public void Emit(string eventName, object? data = null)
    {
        if (_listeners.TryGetValue(eventName, out var handlers))
        {
            foreach (var handler in handlers.ToArray())
            {
                handler(data);
            }
        }
    }
}
```

- [ ] **Step 3 — pass + commit.** Run filter → PASS. Commit: `feat: add GleapEventDispatcher`.

---

## Task 3: Wire events into the backend & facade

**Files:** Modify `src/Gleap.Core/ManagedBackend.cs`, `src/Gleap.Core/IGleapBackend.cs`, `src/Gleap.Core/Gleap.cs`, `tests/Gleap.Core.Tests/GleapFacadeTests.cs`; Test `tests/Gleap.Core.Tests/ManagedBackendEventsTests.cs`.

- [ ] **Step 1 — failing test.** `ManagedBackendEventsTests.cs` (reuse the init helper pattern):
  - `Initialized_FiresAfterInit`: construct backend, `backend.RegisterListener("initialized", _ => fired = true)`, then `await InitializeAsync(...)` (enqueue session+config), assert fired.
  - `WidgetOpened_FiresOnOpen`: after init + ping, register `"widgetOpened"`, call `backend.Open()`, assert fired. Similarly `widgetClosed` on `Close()`.
  - `CustomAction_FiresWithName`: register `"customActionTriggered"`, `ch.SimulateIncoming("{\"name\":\"run-custom-action\",\"data\":\"myAction\",\"shareToken\":\"t\"}")`, assert the handler received data whose serialized form contains `"myAction"`.
  - `FeedbackSent_FiresOnSuccess`: after init + ping, register `"feedbackSent"`, enqueue `/bugs/v2` 200, simulate `send-feedback`, await `backend.LastFeedbackTask!`, assert fired.
  Run → FAIL.

- [ ] **Step 2 — implement in `ManagedBackend`.**
  - Add `using GleapSDK.Events;`.
  - Add field `private readonly GleapEventDispatcher _events = new();`.
  - Add `public void RegisterListener(string eventName, System.Action<object?> handler) => _events.Register(eventName, handler);`
  - In `InitializeAsync`, after `_config.LoadAsync(...)`, subscribe bridge events and emit `initialized` LAST:
```csharp
        _bridge.FeedbackFlowStarted += _ => _events.Emit("feedbackFlowStarted");
        _bridge.CustomActionTriggered += (name, token) =>
            _events.Emit("customActionTriggered", new Dictionary<string, object?> { ["name"] = name, ["shareToken"] = token });
        _bridge.ToolExecutionRequested += _ => _events.Emit("toolExecution");
        _events.Emit("initialized");
```
    (These subscriptions are added once; `InitializeAsync` runs once per backend.)
  - In `Open()`, after the existing `Bridge.Send(...)`, add `_events.Emit("widgetOpened");`. In `Close()`, after its send, add `_events.Emit("widgetClosed");`.
  - In `HandleSendFeedbackAsync`, on the success path (right after sending `feedback-sent`), add `_events.Emit("feedbackSent", response);`.

- [ ] **Step 3 — `IGleapBackend`:** add `void RegisterListener(string eventName, System.Action<object?> handler);`

- [ ] **Step 4 — `Gleap` facade:** add `public static void RegisterListener(string eventName, System.Action<object?> handler) => Backend.RegisterListener(eventName, handler);`

- [ ] **Step 5 — `GleapFacadeTests` fake:** add a no-op `RegisterListener`.

- [ ] **Step 6 — pass + full suite + commit.** Run the new filter and `--filter GleapFacadeTests` → PASS; then `dotnet test Gleap.sln` green. Commit: `feat: RegisterListener callbacks (initialized/widgetOpened/closed/feedbackSent/customAction/flowStarted/toolExecution)`.

---

## Task 4: Quality gate

- [ ] **Step 1:** `dotnet format Gleap.sln` then `dotnet format Gleap.sln --verify-no-changes` → exit 0.
- [ ] **Step 2:** `dotnet build Gleap.sln -warnaserror --no-incremental` → 0 warnings/0 errors (test-only CA false positives → existing `[tests/**/*.cs]` editorconfig block).
- [ ] **Step 3:** `dotnet test Gleap.sln` → green.
- [ ] **Step 4:** Commit: `chore: pass quality gate for callbacks`.

---

## Definition of Done
- `dotnet test Gleap.sln` green; `dotnet build Gleap.sln -warnaserror` clean; format clean.
- `Gleap.RegisterListener(name, handler)` works; `initialized`, `widgetOpened`, `widgetClosed`, `feedbackSent`, `feedbackFlowStarted`, `customActionTriggered`, `toolExecution` fire at the right moments.

## Follow-up (realtime sub-project, later, with the real runtime)
- `/sessions/ping` polling loop + `PingResponse {actions, unreadCount}` + `notificationCountUpdated`/`outboundSent` emits.
- Outbound banner/modal rendering (separate `outboundmedia` WebViews) — platform.
- WebSocket transport as a polling alternative.
- Replay/screenshot capture seam (`IScreenshotProvider` + timer) feeding the feedback body + image upload.
