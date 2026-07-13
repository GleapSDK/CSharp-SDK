# Gleap C# SDK — Realtime / Outbound (`/sessions/ping`)

> **For agentic workers:** Implement task-by-task with TDD. Steps use `- [ ]`.

**Goal:** Poll `/sessions/ping`, parse the returned outbound actions + unread count, and surface them: emit `notificationCountUpdated`, emit `outboundSent` per action, and auto-trigger `survey`/`feedbackflow` actions through the existing widget navigation. Banner/modal *rendering* is platform (Core just surfaces the action).

**Architecture:** `ApiClient.PingAsync` returns a typed `PingResponse { Actions, UnreadCount }`. `ManagedBackend.PollOutboundOnceAsync(ct)` runs one poll cycle (flush buffered events, ping, dispatch). The **timer loop is the platform host's job** (WPF `DispatcherTimer`, Unity coroutine, …) — Core stays headless/threadless and fully unit-testable via `PollOutboundOnceAsync`.

**Tech Stack:** builds on the completed Core (98 tests). `Gleap.sln` (macOS). Always pass `Gleap.sln`.

**Reference:** parent spec §6 (`POST /sessions/ping` body `{time, events, opened, ws, type, sdkVersion}` → response `{ a: actions[], u: unread }`; action `actionType` ∈ notification/banner/modal/else→survey/feedback-flow). **Contract caveat:** inferred from native SDKs; headless verifies request/response shape only.

**Convention:** `CSharp-SDK/`, .NET at `~/.dotnet` (prefix `export PATH="$HOME/.dotnet:$PATH" && export DOTNET_CLI_TELEMETRY_OPTOUT=1 && `). Commits local only. One public type per file; `sealed`; file-scoped ns; XML docs; `ConfigureAwait(false)`.

---

## Task 1: `OutboundAction` + `PingResponse` models

**Files:** Create `src/Gleap.Core/Outbound/OutboundAction.cs`, `src/Gleap.Core/Outbound/PingResponse.cs`; Test `tests/Gleap.Core.Tests/OutboundModelTests.cs`.

- [ ] **Step 1 — failing test.** `OutboundModelTests.cs`: construct `new OutboundAction("survey", "ob1", default)` → assert `ActionType=="survey"`, `OutboundId=="ob1"`; `new PingResponse(new[]{action}, 3)` → `UnreadCount==3`, `Actions.Count==1`. Run `dotnet test Gleap.sln --filter OutboundModelTests` → FAIL.

- [ ] **Step 2 — implement.** `src/Gleap.Core/Outbound/OutboundAction.cs`:
```csharp
using System.Text.Json;

namespace GleapSDK.Outbound;

/// <summary>One server-pushed outbound action from a ping response.</summary>
public sealed class OutboundAction
{
    public string ActionType { get; }
    public string? OutboundId { get; }
    public JsonElement Data { get; }

    public OutboundAction(string actionType, string? outboundId, JsonElement data)
    {
        ActionType = actionType;
        OutboundId = outboundId;
        Data = data;
    }
}
```
`src/Gleap.Core/Outbound/PingResponse.cs`:
```csharp
using System.Collections.Generic;

namespace GleapSDK.Outbound;

/// <summary>Parsed <c>POST /sessions/ping</c> response: outbound actions + unread count.</summary>
public sealed class PingResponse
{
    public IReadOnlyList<OutboundAction> Actions { get; }
    public int UnreadCount { get; }

    public PingResponse(IReadOnlyList<OutboundAction> actions, int unreadCount)
    {
        Actions = actions;
        UnreadCount = unreadCount;
    }
}
```

- [ ] **Step 3 — pass + commit.** `feat: add OutboundAction and PingResponse models`.

---

## Task 2: `ApiClient.PingAsync`

**Files:** Modify `src/Gleap.Core/Http/ApiClient.cs`; Test `tests/Gleap.Core.Tests/ApiClientPingTests.cs`.

- [ ] **Step 1 — failing test.** `ApiClientPingTests.cs`:
  - `Ping_PostsToPing_WithHeaders`: enqueue `HttpResult(200, "{\"a\":[{\"actionType\":\"survey\",\"outbound\":\"ob1\"}],\"u\":2}")`; `var r = await client.PingAsync(123, new object?[] { }, true, "g1", "h1", ct)`; assert URL `https://api.gleap.io/sessions/ping`, POST, header `Gleap-Id`=="g1", body contains `"opened":true`; `r.UnreadCount==2`, `r.Actions[0].ActionType=="survey"`, `r.Actions[0].OutboundId=="ob1"`.
  - `Ping_EmptyResponse_YieldsNoActions`: enqueue `HttpResult(200,"{}")`; assert `r.Actions.Count==0`, `r.UnreadCount==0`.
  Run → FAIL.

- [ ] **Step 2 — implement.** In `ApiClient.cs` add `using GleapSDK.Outbound;` and:
```csharp
    /// <summary>POST /sessions/ping. Flushes buffered events and returns outbound actions + unread count.</summary>
    public async Task<PingResponse> PingAsync(
        long time, IReadOnlyList<object?> events, bool opened,
        string? gleapId, string? gleapHash, CancellationToken ct)
    {
        var body = _json.Serialize(new Dictionary<string, object?>
        {
            ["time"] = time,
            ["events"] = events,
            ["opened"] = opened,
            ["ws"] = false,
            ["type"] = "windows",
            ["sdkVersion"] = "0.1.0"
        });
        var res = await _http.SendAsync("POST", _endpoints.ApiUrl + "/sessions/ping",
            body, BaseHeaders(gleapId, gleapHash), ct).ConfigureAwait(false);
        if (!res.IsSuccess)
        {
            throw new GleapApiException(res.StatusCode, $"Ping failed with status {res.StatusCode}");
        }

        using var doc = JsonDocument.Parse(res.Body);
        var root = doc.RootElement;
        var actions = new List<OutboundAction>();
        if (root.TryGetProperty("a", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in arr.EnumerateArray())
            {
                var actionType = item.TryGetProperty("actionType", out var at) && at.ValueKind == JsonValueKind.String
                    ? at.GetString()! : "";
                var outboundId = item.TryGetProperty("outbound", out var ob) && ob.ValueKind == JsonValueKind.String
                    ? ob.GetString() : null;
                actions.Add(new OutboundAction(actionType, outboundId, item.Clone()));
            }
        }
        var unread = root.TryGetProperty("u", out var u) && u.ValueKind == JsonValueKind.Number ? u.GetInt32() : 0;
        return new PingResponse(actions, unread);
    }
```

- [ ] **Step 3 — pass + commit.** `feat: ApiClient.PingAsync (POST /sessions/ping)`.

---

## Task 3: `ManagedBackend.PollOutboundOnceAsync` + wiring

**Files:** Modify `src/Gleap.Core/ManagedBackend.cs`, `src/Gleap.Core/IGleapBackend.cs`, `src/Gleap.Core/Gleap.cs`, `tests/Gleap.Core.Tests/GleapFacadeTests.cs`; Test `tests/Gleap.Core.Tests/ManagedBackendOutboundTests.cs`.

- [ ] **Step 1 — failing test.** `ManagedBackendOutboundTests.cs` (reuse the init helper):
  - `Poll_EmitsNotificationCount`: init + ping; register `"notificationCountUpdated"`; enqueue ping response `{"a":[],"u":5}`; `await backend.PollOutboundOnceAsync(CancellationToken.None)`; assert the handler received `5` (as the emitted data).
  - `Poll_EmitsOutboundSent_PerAction`: enqueue `{"a":[{"actionType":"survey","outbound":"ob1"}],"u":0}`; register `"outboundSent"`; poll; assert handler fired once with data whose serialization contains `"ob1"`.
  - `Poll_AutoStartsSurvey`: enqueue `{"a":[{"actionType":"survey","outbound":"ob1","flow":"s1"}],"u":0}`; poll; assert an executed script contains `"start-survey"` (Core auto-triggers survey/feedback-flow via the widget).
  Run → FAIL.

- [ ] **Step 2 — implement in `ManagedBackend`.**
  - Add `using GleapSDK.Outbound;` and `using System.Collections.Generic;` (present).
  - Add a monotonically-increasing ping time source that does not use `DateTime.Now` in a way that breaks tests — use `System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()` (allowed in normal C#, only Workflow scripts forbid it). Inline it.
  - Add:
```csharp
    /// <summary>Runs one outbound poll cycle: flush events, ping, emit notificationCountUpdated +
    /// outboundSent per action, and auto-open survey/feedback-flow actions. The platform host calls
    /// this on a timer (Core stays threadless).</summary>
    public async Task PollOutboundOnceAsync(CancellationToken ct)
    {
        var events = new List<object?>();
        foreach (var e in _eventLog.Snapshot())
        {
            events.Add(new Dictionary<string, object?> { ["name"] = e.Name, ["data"] = e.Data, ["date"] = e.Date });
        }

        var time = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var response = await _api.PingAsync(time, events, _widgetOpen, _session.GleapId, _session.GleapHash, ct)
            .ConfigureAwait(false);

        _events.Emit("notificationCountUpdated", response.UnreadCount);

        foreach (var action in response.Actions)
        {
            _events.Emit("outboundSent", new Dictionary<string, object?>
            {
                ["actionType"] = action.ActionType,
                ["outboundId"] = action.OutboundId
            });

            if (action.ActionType == "survey")
            {
                var flow = action.Data.TryGetProperty("flow", out var f) && f.ValueKind == JsonValueKind.String
                    ? f.GetString()! : action.OutboundId ?? "";
                Bridge.Send(WidgetCommands.StartSurvey(flow, SurveyFormat.Survey));
            }
            else if (action.ActionType == "feedbackflow")
            {
                var flow = action.Data.TryGetProperty("flow", out var f) && f.ValueKind == JsonValueKind.String
                    ? f.GetString()! : action.OutboundId ?? "";
                Bridge.Send(WidgetCommands.StartClassicForm(flow, showBackButton: true));
            }
            // notification / banner / modal: surfaced via outboundSent; rendering is the platform host's job.
        }
    }
```
  - Add a `private bool _widgetOpen;` field; set it `true` in `Open()` and `false` in `Close()` (alongside the existing sends). Needs a `using System.Text.Json;` for `JsonValueKind` (present).

- [ ] **Step 3 — `IGleapBackend`:** add `Task PollOutboundOnceAsync(CancellationToken ct);`

- [ ] **Step 4 — `Gleap` facade:** add `public static Task CheckOutboundAsync(CancellationToken ct = default) => Backend.PollOutboundOnceAsync(ct);`

- [ ] **Step 5 — `GleapFacadeTests` fake:** add no-op `PollOutboundOnceAsync` → `Task.CompletedTask`.

- [ ] **Step 6 — pass + full suite + commit.** Run new filter + `--filter GleapFacadeTests` → PASS; `dotnet test Gleap.sln` green. Commit: `feat: outbound polling (/sessions/ping) with notification + outbound dispatch`.

---

## Task 4: Quality gate

- [ ] **Step 1:** `dotnet format Gleap.sln` + `--verify-no-changes` → exit 0.
- [ ] **Step 2:** `dotnet build Gleap.sln -warnaserror --no-incremental` → 0/0.
- [ ] **Step 3:** `dotnet test Gleap.sln` → green.
- [ ] **Step 4:** Commit: `chore: pass quality gate for outbound polling`.

---

## Definition of Done
- `PollOutboundOnceAsync` posts `/sessions/ping` (with buffered events + open state), emits `notificationCountUpdated` + `outboundSent`, and auto-opens survey/feedback-flow outbounds. `Gleap.CheckOutboundAsync()` drives it; platform hosts loop it on a timer.

## Follow-up
- Platform: timer loop (WPF DispatcherTimer / Unity coroutine) calling `CheckOutboundAsync`; banner/modal rendering via `outboundmedia` WebViews on the `outboundSent` action; WebSocket transport as a poll alternative.
