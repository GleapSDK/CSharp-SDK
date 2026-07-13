# Gleap C# SDK — SP-0 Part 2b: Feedback submission (/bugs/v2)

> **For agentic workers:** Implement task-by-task with TDD. Steps use `- [ ]`.

**Goal:** Handle the widget's `send-feedback` message end-to-end — assemble the `/bugs/v2` payload from the collected session data + the submitted form, POST it, and reply `feedback-sent` / `feedback-sending-failed`. Add `SendSilentCrashReportAsync`.

**Architecture:** Pure C# `netstandard2.0`, headless. A `FeedbackAssembler` builds the `/bugs/v2` body from `SessionDataCollector.BuildTicketData()` + the widget's `formData` + `type`/`priority`/`isSilent`, minus any `excludeData` keys. `ApiClient.SubmitBugAsync` POSTs it. `ManagedBackend` subscribes to the bridge's existing `SendFeedbackRequested` event, runs the assemble→post→reply round-trip.

**Tech Stack:** builds on SP-0 Part 1 + 2a + Core-API-completion. `Gleap.sln` (macOS). 81 tests currently green.

**Reference:** parent spec §5 (`send-feedback` → `feedback-sent`/`feedback-sending-failed`), §6 (`POST /bugs/v2`), §7 (crash `excludeData` defaults). Native assembly (from reverse-engineering): `/bugs/v2` body = `{ type, priority?, isSilent?, formData, metaData, customData, consoleLog, networkLogs, customEventLog, tags }` minus `excludeData` keys.

**⚠️ Contract caveat:** the exact `send-feedback` and `/bugs/v2` shapes are inferred from the native SDKs and confirmed only by the Windows live test. Headless tests verify WE emit the right-shaped request and the send-feedback→reply round-trip — not server acceptance.

**Scope (2b):** send-feedback handling + `/bugs/v2` assembly/post + silent crash + reply. **Out of scope:** screenshot/replay capture + image upload (screenshot/replays are simply excluded from the body for now — capture arrives with 2c/platform); outbound polling (2c).

**Convention:** `CSharp-SDK/`, .NET at `~/.dotnet` (prefix `export PATH="$HOME/.dotnet:$PATH" && export DOTNET_CLI_TELEMETRY_OPTOUT=1 && `). Always pass `Gleap.sln` to build/test/format (a `Gleap.Windows.sln` also exists). Commits **local only**. One public type per file; `sealed`; file-scoped namespaces; XML docs; `ConfigureAwait(false)`.

---

## Task 1: `ApiClient.SubmitBugAsync`

**Files:** Modify `src/Gleap.Core/Http/ApiClient.cs`; Test `tests/Gleap.Core.Tests/ApiClientBugTests.cs`.

- [ ] **Step 1 — failing test.** `ApiClientBugTests.cs`:
  - `SubmitBug_PostsToBugsV2_WithHeaders`: enqueue `HttpResult(200, "{\"shareToken\":\"st1\"}")`; call `await client.SubmitBugAsync(new Dictionary<string, object?> { ["type"] = "BUG" }, "g1", "h1", ct)`; assert call URL `https://api.gleap.io/bugs/v2`, method POST, header `Gleap-Id`=="g1", body contains `"type":"BUG"`; returned raw body contains `shareToken`.
  - `SubmitBug_NonSuccess_Throws`: enqueue `HttpResult(500,"err")`; assert `await Assert.ThrowsAsync<GleapApiException>(...)`.
  Run `dotnet test Gleap.sln --filter ApiClientBugTests` → FAIL.

- [ ] **Step 2 — implement.** In `ApiClient.cs`:
```csharp
    /// <summary>POST /bugs/v2 with the assembled report body. Returns the raw response JSON.</summary>
    public async Task<string> SubmitBugAsync(
        IReadOnlyDictionary<string, object?> body, string? gleapId, string? gleapHash, CancellationToken ct)
    {
        var res = await _http.SendAsync("POST", _endpoints.ApiUrl + "/bugs/v2",
            _json.Serialize(body), BaseHeaders(gleapId, gleapHash), ct).ConfigureAwait(false);
        if (!res.IsSuccess)
        {
            throw new GleapApiException(res.StatusCode, $"Bug submission failed with status {res.StatusCode}");
        }
        return res.Body;
    }
```

- [ ] **Step 3 — pass + commit.** Run filter → PASS. Commit: `feat: ApiClient.SubmitBugAsync (POST /bugs/v2)`.

---

## Task 2: `FeedbackAssembler`

**Files:** Create `src/Gleap.Core/Feedback/FeedbackAssembler.cs`; Test `tests/Gleap.Core.Tests/FeedbackAssemblerTests.cs`.

- [ ] **Step 1 — failing test.** `FeedbackAssemblerTests.cs`:
  - `Build_MergesTypeFormDataAndTicketData`: given `ticketData = { ["metaData"] = new Dictionary<string,object?>{["sdkType"]="NET"}, ["formData"] = new Dictionary<string,object?>{["priority"]="low"}, ["tags"] = new[]{"vip"} }` and `formData = { ["description"] = "it broke" }`, call `FeedbackAssembler.Build(ticketData, formData, type: "BUG", priority: null, isSilent: false, excludeKeys: new HashSet<string>())`; assert result `["type"]=="BUG"`, `result["formData"]` contains both `description`=="it broke" and `priority`=="low", `result["metaData"]` present, `result` does NOT contain `isSilent` (false omitted) or `priority` (null omitted).
  - `Build_OmitsExcludedKeys`: excludeKeys `{"metaData","tags"}` → result has neither `metaData` nor `tags`.
  - `Build_CrashSetsPriorityAndSilent`: `Build(empty, {description}, "CRASH", "HIGH", true, excludeKeys)` → `["type"]=="CRASH"`, `["priority"]=="HIGH"`, `["isSilent"]==true`.
  Run → FAIL.

- [ ] **Step 2 — implement.** Create `src/Gleap.Core/Feedback/FeedbackAssembler.cs`:
```csharp
using System.Collections.Generic;

namespace GleapSDK.Feedback;

/// <summary>
/// Builds the <c>POST /bugs/v2</c> body from the collected ticket data plus the submitted form,
/// applying feedback type/priority/silent flags and removing any excluded keys.
/// </summary>
public static class FeedbackAssembler
{
    public static Dictionary<string, object?> Build(
        IReadOnlyDictionary<string, object?> ticketData,
        IReadOnlyDictionary<string, object?> formData,
        string type,
        string? priority,
        bool isSilent,
        ISet<string> excludeKeys)
    {
        var body = new Dictionary<string, object?>();
        foreach (var kv in ticketData)
        {
            body[kv.Key] = kv.Value;
        }

        // The submitted form merges over any prefilled ticket formData.
        var mergedForm = new Dictionary<string, object?>();
        if (ticketData.TryGetValue("formData", out var existingForm)
            && existingForm is IReadOnlyDictionary<string, object?> existingFormDict)
        {
            foreach (var kv in existingFormDict)
            {
                mergedForm[kv.Key] = kv.Value;
            }
        }
        foreach (var kv in formData)
        {
            mergedForm[kv.Key] = kv.Value;
        }
        body["formData"] = mergedForm;

        body["type"] = type;
        if (!string.IsNullOrEmpty(priority))
        {
            body["priority"] = priority;
        }
        if (isSilent)
        {
            body["isSilent"] = true;
        }

        foreach (var key in excludeKeys)
        {
            body.Remove(key);
        }

        return body;
    }
}
```

- [ ] **Step 3 — pass + commit.** Run filter → PASS. Commit: `feat: FeedbackAssembler builds the /bugs/v2 body`.

---

## Task 3: Wire send-feedback + silent crash into the backend & facade

**Files:** Modify `src/Gleap.Core/ManagedBackend.cs`, `src/Gleap.Core/IGleapBackend.cs`, `src/Gleap.Core/Gleap.cs`, `tests/Gleap.Core.Tests/GleapFacadeTests.cs`; Test `tests/Gleap.Core.Tests/ManagedBackendFeedbackTests.cs`.

Background: the bridge already raises `SendFeedbackRequested(JsonElement data)` on an incoming `send-feedback`. The `data` shape (inferred): `{ formData: {...}, action: { feedbackType?: string, excludeData?: { key: bool } } }`.

- [ ] **Step 1 — failing test.** `ManagedBackendFeedbackTests.cs`:
  - `SendFeedback_AssemblesAndReplies_FeedbackSent`: init a backend (session + config enqueued), `ch.SimulateIncoming(ping)`; enqueue a `/bugs/v2` response `HttpResult(200,"{\"shareToken\":\"st1\"}")`; `ch.SimulateIncoming("{\"name\":\"send-feedback\",\"data\":{\"formData\":{\"description\":\"boom\"},\"action\":{\"feedbackType\":\"BUG\"}}}")`; then (since the handler is async) allow it to complete — the handler should be awaited internally via a completion you can observe; assert (a) an HTTP call went to `https://api.gleap.io/bugs/v2` with body containing `"type":"BUG"` and `"boom"`, and (b) an executed script contains `"feedback-sent"`.
  - `SendFeedback_OnApiError_RepliesFailed`: enqueue `/bugs/v2` `HttpResult(500,"err")`; simulate send-feedback; assert an executed script contains `"feedback-sending-failed"`.
  - `SilentCrashReport_PostsCrash`: init + ping; enqueue `/bugs/v2` 200; `await backend.SendSilentCrashReportAsync("crashed", Severity.High, null, CancellationToken.None)`; assert the `/bugs/v2` body contains `"type":"CRASH"`, `"priority":"HIGH"`, `"isSilent":true`, `"crashed"`.

  > Async note: `SendFeedbackRequested` is a synchronous event but the handler does async work. Implement the handler to run the async submit and, on completion, send the reply — and expose a way for tests to await it. Simplest: give `ManagedBackend` an `internal Task? LastFeedbackTask` set to the running submit task inside the handler; the test awaits `backend.LastFeedbackTask!` before asserting. Mark it `internal` (the test project already has InternalsVisibleTo).

  Run → FAIL.

- [ ] **Step 2 — implement in `ManagedBackend`.**
  - Add usings: `using System.Collections.Generic; using System.Text.Json; using GleapSDK.Feedback;` (others already present).
  - Add fields: `private ApiClient _api = null!;` — NOTE: `InitializeAsync` currently creates a local `api`; change it to assign the field `_api = new ApiClient(...)` (so the feedback handler can reuse it), and pass `_api` to `SessionManager`/`ConfigManager` as before.
  - Add `internal Task? LastFeedbackTask { get; private set; }`.
  - In `InitializeAsync`, after the bootstrapper wiring, subscribe:
```csharp
        _bridge.SendFeedbackRequested += data => { LastFeedbackTask = HandleSendFeedbackAsync(data); };
```
  - Add the handler + submit + crash:
```csharp
    private async Task HandleSendFeedbackAsync(JsonElement data)
    {
        var formData = ReadObject(data, "formData");
        var action = data.ValueKind == JsonValueKind.Object && data.TryGetProperty("action", out var a) ? a : default;
        var type = action.ValueKind == JsonValueKind.Object && action.TryGetProperty("feedbackType", out var ft)
            && ft.ValueKind == JsonValueKind.String ? ft.GetString()! : "BUG";
        var excludeKeys = ReadExcludeKeys(action);

        try
        {
            var body = FeedbackAssembler.Build(_collector.BuildTicketData(), formData, type, null, false, excludeKeys);
            var response = await _api.SubmitBugAsync(body, _session.GleapId, _session.GleapHash, default).ConfigureAwait(false);
            _bridge.Send(new GleapBridgeMessage { Name = "feedback-sent", Data = new Dictionary<string, object?> { ["response"] = response } });
        }
        catch (System.Exception ex)
        {
            _bridge.Send(new GleapBridgeMessage { Name = "feedback-sending-failed", Data = ex.Message });
        }
    }

    public async Task SendSilentCrashReportAsync(
        string description, Severity severity, IReadOnlyDictionary<string, object>? excludeData, CancellationToken ct)
    {
        var priority = severity switch
        {
            Severity.High => "HIGH",
            Severity.Medium => "MEDIUM",
            _ => "LOW"
        };
        var excludeKeys = excludeData != null
            ? new HashSet<string>(excludeData.Keys)
            : new HashSet<string> { "screenshot", "replays", "attachments" };
        var formData = new Dictionary<string, object?> { ["description"] = description };
        var body = FeedbackAssembler.Build(_collector.BuildTicketData(), formData, "CRASH", priority, true, excludeKeys);
        await _api.SubmitBugAsync(body, _session.GleapId, _session.GleapHash, ct).ConfigureAwait(false);
    }

    private static Dictionary<string, object?> ReadObject(JsonElement parent, string name)
    {
        var result = new Dictionary<string, object?>();
        if (parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out var obj)
            && obj.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in obj.EnumerateObject())
            {
                result[prop.Name] = prop.Value.Clone();
            }
        }
        return result;
    }

    private static HashSet<string> ReadExcludeKeys(JsonElement action)
    {
        var keys = new HashSet<string>();
        if (action.ValueKind == JsonValueKind.Object && action.TryGetProperty("excludeData", out var ex)
            && ex.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in ex.EnumerateObject())
            {
                if (prop.Value.ValueKind == JsonValueKind.True)
                {
                    keys.Add(prop.Name);
                }
            }
        }
        return keys;
    }
```
  > `ReadObject` values are cloned `JsonElement`s; `System.Text.Json` serializes them verbatim into the `/bugs/v2` body, so `description` etc. survive as sent.

- [ ] **Step 3 — `IGleapBackend`:** add `Task SendSilentCrashReportAsync(string description, Severity severity, System.Collections.Generic.IReadOnlyDictionary<string, object>? excludeData, CancellationToken ct);`

- [ ] **Step 4 — `Gleap` facade:** add
```csharp
    public static Task SendSilentCrashReportAsync(string description, Severity severity, System.Collections.Generic.IReadOnlyDictionary<string, object>? excludeData = null, CancellationToken ct = default) => Backend.SendSilentCrashReportAsync(description, severity, excludeData, ct);
```

- [ ] **Step 5 — `GleapFacadeTests` fake:** add a no-op `SendSilentCrashReportAsync` returning `Task.CompletedTask`.

- [ ] **Step 6 — pass + full suite + commit.** Run the new filter and `--filter GleapFacadeTests` → PASS; then `dotnet test Gleap.sln` green. Commit: `feat: send-feedback -> /bugs/v2 round-trip and silent crash report`.

---

## Task 4: Quality gate

- [ ] **Step 1:** `dotnet format Gleap.sln` then `dotnet format Gleap.sln --verify-no-changes` → exit 0.
- [ ] **Step 2:** `dotnet build Gleap.sln -warnaserror --no-incremental` → 0 warnings/0 errors (fix any; test-only CA false positives go in the existing `[tests/**/*.cs]` editorconfig block).
- [ ] **Step 3:** `dotnet test Gleap.sln` → all green.
- [ ] **Step 4:** Commit: `chore: pass quality gate for feedback submission`.

---

## Definition of Done
- `dotnet test Gleap.sln` green; `dotnet build Gleap.sln -warnaserror` clean; format clean.
- A widget `send-feedback` triggers an assembled `POST /bugs/v2` (type + merged formData + collected metaData/customData/logs/tags, minus excludeData) and a `feedback-sent` / `feedback-sending-failed` reply.
- `Gleap.SendSilentCrashReportAsync(...)` posts a `CRASH` report with severity + silent flag.

## Follow-up
- **Part 2c:** `/sessions/ping` polling + outbound (banner/modal/survey) dispatch + WebSocket + replay/screenshot capture seam (screenshot/replay then feed into the feedback body + image upload).
- Image/attachment upload to obtain URLs (native uploads first, then references) — currently attachments/screenshots are omitted from the body.
