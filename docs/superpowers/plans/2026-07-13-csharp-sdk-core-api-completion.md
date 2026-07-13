# Gleap C# SDK — Core API Completion (facade nav + metadata injection + identity)

> **For agentic workers:** Implement task-by-task with TDD. Steps use `- [ ]`.

**Goal:** Close the `Gleap.Core` API gaps flagged in SP-1 §7: inject `IMetadataProvider` via `Dependencies`, wire the remaining `WidgetCommands` to the facade, and build identity (`identifyContact`/`updateContact`/`clearIdentity`/`isUserIdentified`/`getIdentity`) against `/sessions/identify` + `/sessions/partialupdate`.

**Architecture:** Pure C# `netstandard2.0`, headless, unit-tested. Identity re-pushes `session-update` to the widget after the session changes, reusing the bootstrap message builder (small refactor to expose it).

**Tech Stack:** builds on SP-0 Part 1 + 2a. `Gleap.sln` (macOS). 74 tests currently green.

**Reference:** parent spec `docs/superpowers/specs/2026-07-13-csharp-sdk-design.md` §3 (API surface), §6 (endpoints). SP-1 spec §7 (the gaps this closes).

**Convention:** `CSharp-SDK/`, .NET at `~/.dotnet` (prefix `export PATH="$HOME/.dotnet:$PATH" && export DOTNET_CLI_TELEMETRY_OPTOUT=1 && `). Commits **local only**. One public type per file; `sealed`; file-scoped namespaces; `Async` suffix + `CancellationToken` + `ConfigureAwait(false)`; XML docs. Analyzer warnings handled at the final gate task — don't fix mid-task.

---

## Task 1: Inject `IMetadataProvider` via `Dependencies`

**Files:** Modify `src/Gleap.Core/ManagedBackend.cs`; Test `tests/Gleap.Core.Tests/ManagedBackendMetadataTests.cs`.

- [ ] **Step 1 — failing test.** Create `ManagedBackendMetadataTests.cs`: a fake `IMetadataProvider` returning `{["sdkType"]="TEST/X"}`. Build `ManagedBackend` with `Dependencies { …, Metadata = fake }`, `InitializeAsync`, `ch.SimulateIncoming("{\"name\":\"ping\"}")`, then `ch.SimulateIncoming("{\"name\":\"collect-ticket-data\"}")`. Assert an executed script contains `"sdkType":"TEST/X"`. (Reuse the `FakeHttpTransport`/`FakeWebViewChannel` setup pattern from `ManagedBackendDataTests`.) Run `dotnet test --filter ManagedBackendMetadataTests` → FAIL (`Metadata` not on `Dependencies`).

- [ ] **Step 2 — implement.** In `ManagedBackend.cs`:
  - Add `using GleapSDK.Metadata;` if not present.
  - Add to `Dependencies`: `public IMetadataProvider Metadata { get; set; } = new DefaultMetadataProvider("NET", "0.1.0");`
  - In the constructor, replace `new DefaultMetadataProvider("NET/Windows", "0.1.0")` in the `SessionDataCollector` construction with `_d.Metadata`.

- [ ] **Step 3 — pass.** Run the filter → PASS. Commit: `feat: inject IMetadataProvider via ManagedBackend.Dependencies`.

---

## Task 2: Wire remaining navigation methods to the facade

The `WidgetCommands` builders already exist; expose them through `IGleapBackend` → `ManagedBackend` → `Gleap`.

**Files:** Modify `src/Gleap.Core/IGleapBackend.cs`, `src/Gleap.Core/ManagedBackend.cs`, `src/Gleap.Core/Gleap.cs`, `tests/Gleap.Core.Tests/GleapFacadeTests.cs`; Test `tests/Gleap.Core.Tests/ManagedBackendNavTests.cs`.

- [ ] **Step 1 — failing test.** Create `ManagedBackendNavTests.cs`: init a backend (as in `ManagedBackendDataTests`), `ch.SimulateIncoming(ping)`, call `backend.SearchHelpCenter("reset", true)` and `backend.AskAI("why?", true)`, assert executed scripts contain `"open-helpcenter-search"` + `"reset"` and `"ask-ai"` + `"why?"`. Run → FAIL.

- [ ] **Step 2 — extend `IGleapBackend`.** Add (after `ShowSurvey`):
```csharp
    void OpenConversations(bool showBackButton);
    void StartClassicForm(string formId, bool showBackButton);
    void OpenHelpCenterArticle(string articleId, bool showBackButton);
    void OpenHelpCenterCollection(string collectionId, bool showBackButton);
    void SearchHelpCenter(string term, bool showBackButton);
    void OpenNewsArticle(string articleId, bool showBackButton);
    void OpenFeatureRequests(bool showBackButton);
    void OpenChecklists(bool showBackButton);
    void OpenChecklist(string checklistId, bool showBackButton);
    void StartChecklist(string outboundId, bool showBackButton);
    void AskAI(string question, bool showBackButton);
```

- [ ] **Step 3 — implement in `ManagedBackend`** (each delegates to the existing `WidgetCommands` builder via `Bridge.Send`):
```csharp
    public void OpenConversations(bool showBackButton) => Bridge.Send(WidgetCommands.OpenConversations(showBackButton));
    public void StartClassicForm(string formId, bool showBackButton) => Bridge.Send(WidgetCommands.StartClassicForm(formId, showBackButton));
    public void OpenHelpCenterArticle(string articleId, bool showBackButton) => Bridge.Send(WidgetCommands.OpenHelpCenterArticle(articleId, showBackButton));
    public void OpenHelpCenterCollection(string collectionId, bool showBackButton) => Bridge.Send(WidgetCommands.OpenHelpCenterCollection(collectionId, showBackButton));
    public void SearchHelpCenter(string term, bool showBackButton) => Bridge.Send(WidgetCommands.SearchHelpCenter(term, showBackButton));
    public void OpenNewsArticle(string articleId, bool showBackButton) => Bridge.Send(WidgetCommands.OpenNewsArticle(articleId, showBackButton));
    public void OpenFeatureRequests(bool showBackButton) => Bridge.Send(WidgetCommands.OpenFeatureRequests(showBackButton));
    public void OpenChecklists(bool showBackButton) => Bridge.Send(WidgetCommands.OpenChecklists(showBackButton));
    public void OpenChecklist(string checklistId, bool showBackButton) => Bridge.Send(WidgetCommands.OpenChecklist(checklistId, showBackButton));
    public void StartChecklist(string outboundId, bool showBackButton) => Bridge.Send(WidgetCommands.StartChecklist(outboundId, showBackButton));
    public void AskAI(string question, bool showBackButton) => Bridge.Send(WidgetCommands.AskAI(question, showBackButton));
```

- [ ] **Step 4 — extend the `Gleap` facade** (defaults `showBackButton = true`), before `ResetForTest`:
```csharp
    public static void OpenConversations(bool showBackButton = true) => Backend.OpenConversations(showBackButton);
    public static void StartClassicForm(string formId, bool showBackButton = true) => Backend.StartClassicForm(formId, showBackButton);
    public static void OpenHelpCenterArticle(string articleId, bool showBackButton = true) => Backend.OpenHelpCenterArticle(articleId, showBackButton);
    public static void OpenHelpCenterCollection(string collectionId, bool showBackButton = true) => Backend.OpenHelpCenterCollection(collectionId, showBackButton);
    public static void SearchHelpCenter(string term, bool showBackButton = true) => Backend.SearchHelpCenter(term, showBackButton);
    public static void OpenNewsArticle(string articleId, bool showBackButton = true) => Backend.OpenNewsArticle(articleId, showBackButton);
    public static void OpenFeatureRequests(bool showBackButton = true) => Backend.OpenFeatureRequests(showBackButton);
    public static void OpenChecklists(bool showBackButton = true) => Backend.OpenChecklists(showBackButton);
    public static void OpenChecklist(string checklistId, bool showBackButton = true) => Backend.OpenChecklist(checklistId, showBackButton);
    public static void StartChecklist(string outboundId, bool showBackButton = true) => Backend.StartChecklist(outboundId, showBackButton);
    public static void AskAI(string question, bool showBackButton = true) => Backend.AskAI(question, showBackButton);
```

- [ ] **Step 5 — update the `GleapFacadeTests` fake** with no-op implementations of all 11 new interface members.

- [ ] **Step 6 — pass + commit.** `dotnet test --filter ManagedBackendNavTests` and `--filter GleapFacadeTests` → PASS. Commit: `feat: wire remaining navigation methods to the facade`.

---

## Task 3: `ApiClient` identify + updateContact

**Files:** Modify `src/Gleap.Core/Http/ApiClient.cs`; Test `tests/Gleap.Core.Tests/ApiClientIdentityTests.cs`.

- [ ] **Step 1 — failing test.** Create `ApiClientIdentityTests.cs`:
  - `Identify_PostsToIdentify_WithUserAndHeaders`: enqueue `HttpResult(200, "{\"gleapId\":\"g2\",\"gleapHash\":\"h2\"}")`; call `IdentifyAsync("u1", new GleapUserProperty{ Email="a@b.c" }, "hash1", "g1", "h1", ct)`; assert call URL `https://api.gleap.io/sessions/identify`, method POST, header `Gleap-Id`=="g1", body contains `"userId":"u1"`, `"email":"a@b.c"`, `"userHash":"hash1"`; result GleapId "g2".
  - `UpdateContact_PostsToPartialUpdate`: enqueue `HttpResult(200,"{}")`; call `UpdateContactAsync(new GleapUserProperty{ Name="Ada" }, "g1", "h1", ct)`; assert URL `.../sessions/partialupdate`, body contains `"data"` and `"Ada"`.
  Run → FAIL.

- [ ] **Step 2 — implement.** In `ApiClient.cs` add `using GleapSDK.Models;` and:
```csharp
    private static Dictionary<string, object?> ToDict(GleapUserProperty p) => new()
    {
        ["userId"] = p.UserId,
        ["name"] = p.Name,
        ["email"] = p.Email,
        ["phone"] = p.Phone,
        ["plan"] = p.Plan,
        ["companyName"] = p.CompanyName,
        ["companyId"] = p.CompanyId,
        ["avatar"] = p.Avatar,
        ["lang"] = p.Lang,
        ["value"] = p.Value,
        ["sla"] = p.Sla,
        ["customData"] = p.CustomData
    };

    /// <summary>POST /sessions/identify. Returns the (possibly upgraded) session ids.</summary>
    public async Task<SessionResult> IdentifyAsync(
        string userId, GleapUserProperty data, string? userHash,
        string? gleapId, string? gleapHash, CancellationToken ct)
    {
        var payload = ToDict(data);
        payload["userId"] = userId;
        if (!string.IsNullOrEmpty(userHash))
        {
            payload["userHash"] = userHash;
        }

        var res = await _http.SendAsync("POST", _endpoints.ApiUrl + "/sessions/identify",
            _json.Serialize(payload), BaseHeaders(gleapId, gleapHash), ct).ConfigureAwait(false);

        if (!res.IsSuccess)
        {
            throw new GleapApiException(res.StatusCode, $"Identify failed with status {res.StatusCode}");
        }

        using var doc = JsonDocument.Parse(res.Body);
        var root = doc.RootElement;
        return new SessionResult
        {
            GleapId = root.TryGetProperty("gleapId", out var i) ? i.GetString() ?? "" : "",
            GleapHash = root.TryGetProperty("gleapHash", out var h) ? h.GetString() ?? "" : ""
        };
    }

    /// <summary>POST /sessions/partialupdate.</summary>
    public async Task UpdateContactAsync(
        GleapUserProperty data, string? gleapId, string? gleapHash, CancellationToken ct)
    {
        var body = _json.Serialize(new Dictionary<string, object?>
        {
            ["data"] = ToDict(data),
            ["type"] = "windows",
            ["sdkVersion"] = "0.1.0"
        });
        var res = await _http.SendAsync("POST", _endpoints.ApiUrl + "/sessions/partialupdate",
            body, BaseHeaders(gleapId, gleapHash), ct).ConfigureAwait(false);
        if (!res.IsSuccess)
        {
            throw new GleapApiException(res.StatusCode, $"Update contact failed with status {res.StatusCode}");
        }
    }
```
> `ToDict` values that are `null` are dropped by the serializer's `WhenWritingNull` policy, so only supplied fields are sent. The `platform`/`type` literal `"windows"` mirrors the SP-0 §8 open item (per-runtime later).

- [ ] **Step 3 — pass + commit.** `dotnet test --filter ApiClientIdentityTests` → PASS. Commit: `feat: ApiClient identify and updateContact`.

---

## Task 4: `SessionManager` identity

**Files:** Modify `src/Gleap.Core/Session/SessionManager.cs`; Test `tests/Gleap.Core.Tests/SessionManagerIdentityTests.cs`.

- [ ] **Step 1 — failing test.** Create `SessionManagerIdentityTests.cs` (reuse the `New()` helper style from `SessionManagerTests`):
  - `Identify_UpdatesSessionAndCachesIdentity`: start guest (enqueue session `{gleapId:g1,hash:h1}`), then enqueue identify `{gleapId:g2,hash:h2}`; call `IdentifyAsync("u1", new GleapUserProperty{Email="a@b.c"}, null, ct)`; assert `GleapId=="g2"`, `IsIdentified` true, `Identity!.Email=="a@b.c"`, `Identity!.UserId=="u1"`, store `gleapId`=="g2".
  - `ClearIdentity_ResetsIdentityFlags`: after identify, `ClearIdentity()`; assert `IsIdentified` false, `Identity` null, `GleapId==""`.
  Run → FAIL.

- [ ] **Step 2 — implement.** In `SessionManager.cs`:
  - Add `using System.Threading; using System.Threading.Tasks; using GleapSDK.Models;` (Task/CancellationToken already imported).
  - Add properties: `public bool IsIdentified { get; private set; }` and `public GleapUserProperty? Identity { get; private set; }`.
  - Add:
```csharp
    public async Task IdentifyAsync(string userId, GleapUserProperty data, string? userHash, CancellationToken ct)
    {
        data.UserId = userId;
        var res = await _api.IdentifyAsync(userId, data, userHash, GleapId, GleapHash, ct).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(res.GleapId))
        {
            GleapId = res.GleapId;
            GleapHash = res.GleapHash;
            _store.Set(KeyId, GleapId);
            _store.Set(KeyHash, GleapHash);
        }
        Identity = data;
        IsIdentified = true;
    }

    public async Task UpdateContactAsync(GleapUserProperty data, CancellationToken ct)
    {
        await _api.UpdateContactAsync(data, GleapId, GleapHash, ct).ConfigureAwait(false);
        Identity = data;
    }
```
  - Extend the existing `ClearIdentity()` body to also reset identity: add `Identity = null; IsIdentified = false;` (keep the existing gleapId/hash + store clearing).

- [ ] **Step 3 — pass + commit.** `dotnet test --filter SessionManagerIdentityTests` → PASS. Commit: `feat: SessionManager identify/updateContact/clearIdentity`.

---

## Task 5: Backend + facade identity, with session-update re-push

**Files:** Modify `src/Gleap.Core/Session/WidgetBootstrapper.cs` (expose the session-update send), `src/Gleap.Core/ManagedBackend.cs`, `src/Gleap.Core/IGleapBackend.cs`, `src/Gleap.Core/Gleap.cs`, `tests/Gleap.Core.Tests/GleapFacadeTests.cs`; Test `tests/Gleap.Core.Tests/ManagedBackendIdentityTests.cs`.

- [ ] **Step 1 — failing test.** Create `ManagedBackendIdentityTests.cs`:
  - `IdentifyContact_ReSendsSessionUpdate_WithUser`: build backend (enqueue session + config for init), `InitializeAsync`, `ch.SimulateIncoming(ping)`; enqueue identify `{gleapId:g2,hash:h2}`; `await backend.IdentifyContactAsync("u1", new GleapUserProperty{Email="a@b.c"}, null, CancellationToken.None)`; assert a LATER executed script contains `"session-update"` and `"userId":"u1"` and `"a@b.c"`; assert `backend.IsUserIdentified()` true and `backend.GetIdentity()!.Email=="a@b.c"`.
  Run → FAIL.

- [ ] **Step 2 — refactor `WidgetBootstrapper`** to expose the session-update send. Split `OnPing` into private senders and make the session-update one public:
```csharp
    private void OnPing()
    {
        SendConfigUpdate();
        SendSessionUpdate();
        SendPrefill();
    }

    private void SendConfigUpdate()
    {
        var s = _snapshot();
        _bridge.Send(new GleapBridgeMessage
        {
            Name = "config-update",
            Data = new Dictionary<string, object>
            {
                ["config"] = RawJson(s.FlowConfigJson),
                ["actions"] = RawJson(s.ProjectActionsJson),
                ["overrideLanguage"] = s.Language,
                ["isApp"] = true
            }
        });
    }

    /// <summary>Sends session-update from the current snapshot. Called on ping and again after identify.</summary>
    public void SendSessionUpdate()
    {
        var s = _snapshot();
        _bridge.Send(new GleapBridgeMessage
        {
            Name = "session-update",
            Data = new Dictionary<string, object?>
            {
                ["sessionData"] = new Dictionary<string, object?>
                {
                    ["gleapId"] = s.GleapId,
                    ["gleapHash"] = s.GleapHash,
                    ["userId"] = s.UserId,
                    ["name"] = s.Name,
                    ["email"] = s.Email
                },
                ["apiUrl"] = s.ApiUrl,
                ["sdkKey"] = s.SdkKey
            }
        });
    }

    private void SendPrefill()
    {
        var s = _snapshot();
        if (s.PreFillFormData != null)
        {
            _bridge.Send(new GleapBridgeMessage { Name = "prefill-form-data", Data = s.PreFillFormData });
        }
    }
```
(Keep the private `RawJson` helper unchanged.)

- [ ] **Step 3 — `ManagedBackend`:** keep a reference to the bootstrapper and add identity methods.
  - Change `_ = new WidgetBootstrapper(_bridge, BuildSnapshot);` to assign a field: add `private WidgetBootstrapper _bootstrapper = null!;` and set `_bootstrapper = new WidgetBootstrapper(_bridge, BuildSnapshot);`.
  - Update `BuildSnapshot()` to surface identity in the snapshot:
```csharp
        UserId = _session.Identity?.UserId,
        Name = _session.Identity?.Name,
        Email = _session.Identity?.Email,
```
    (add these three lines to the existing `new SessionSnapshot { … }`).
  - Add identity methods:
```csharp
    public async Task IdentifyContactAsync(string userId, GleapUserProperty? properties, string? userHash, CancellationToken ct)
    {
        await _session.IdentifyAsync(userId, properties ?? new GleapUserProperty(), userHash, ct).ConfigureAwait(false);
        _bootstrapper.SendSessionUpdate();
    }

    public async Task UpdateContactAsync(GleapUserProperty properties, CancellationToken ct)
    {
        await _session.UpdateContactAsync(properties, ct).ConfigureAwait(false);
        _bootstrapper.SendSessionUpdate();
    }

    public async Task ClearIdentityAsync(CancellationToken ct)
    {
        _session.ClearIdentity();
        await _session.StartAsync("en", "desktop", ct).ConfigureAwait(false);
        _bootstrapper.SendSessionUpdate();
    }

    public bool IsUserIdentified() => _session.IsIdentified;
    public GleapUserProperty? GetIdentity() => _session.Identity;
```

- [ ] **Step 4 — `IGleapBackend`:** add
```csharp
    Task IdentifyContactAsync(string userId, GleapUserProperty? properties, string? userHash, CancellationToken ct);
    Task UpdateContactAsync(GleapUserProperty properties, CancellationToken ct);
    Task ClearIdentityAsync(CancellationToken ct);
    bool IsUserIdentified();
    GleapUserProperty? GetIdentity();
```
Add `using GleapSDK.Models;` and `using System.Threading; using System.Threading.Tasks;` (Task/CT already there).

- [ ] **Step 5 — `Gleap` facade:** add
```csharp
    public static Task IdentifyContactAsync(string userId, Models.GleapUserProperty? properties = null, string? userHash = null, CancellationToken ct = default) => Backend.IdentifyContactAsync(userId, properties, userHash, ct);
    public static Task UpdateContactAsync(Models.GleapUserProperty properties, CancellationToken ct = default) => Backend.UpdateContactAsync(properties, ct);
    public static Task ClearIdentityAsync(CancellationToken ct = default) => Backend.ClearIdentityAsync(ct);
    public static bool IsUserIdentified() => Backend.IsUserIdentified();
    public static Models.GleapUserProperty? GetIdentity() => Backend.GetIdentity();
```
(Use `Models.GleapUserProperty` or add `using GleapSDK.Models;` — either is fine; keep consistent with the file.)

- [ ] **Step 6 — update `GleapFacadeTests` fake** with the 5 new members (`IdentifyContactAsync`/`UpdateContactAsync`/`ClearIdentityAsync` → `Task.CompletedTask`; `IsUserIdentified` → `false`; `GetIdentity` → `null`).

- [ ] **Step 7 — pass + full suite + commit.** `dotnet test --filter ManagedBackendIdentityTests` and `--filter GleapFacadeTests` → PASS; then `dotnet test` (whole suite) green. Commit: `feat: identity (identifyContact/updateContact/clearIdentity) with session-update re-push`.

---

## Task 6: Quality gate

- [ ] **Step 1:** `dotnet format Gleap.sln` then `dotnet format Gleap.sln --verify-no-changes` → exit 0.
- [ ] **Step 2:** `dotnet build Gleap.sln -warnaserror --no-incremental` → 0 warnings/0 errors. Fix any surfaced warnings (extend the `[tests/**/*.cs]` editorconfig block only for genuine test-convention/DI false positives, consistent with the existing CA1707/CA1859/CA1861 entries; otherwise fix the code).
- [ ] **Step 3:** `dotnet test Gleap.sln` → all green.
- [ ] **Step 4:** Commit: `chore: pass quality gate for core API completion`.

> Note: use `Gleap.sln` explicitly in all `dotnet format`/`build`/`test` commands — the repo now also has `Gleap.Windows.sln`, so an unqualified `dotnet format` is ambiguous.

---

## Definition of Done

- `dotnet test Gleap.sln` green; `dotnet build Gleap.sln -warnaserror` clean; `dotnet format Gleap.sln --verify-no-changes` clean.
- `Dependencies.Metadata` is honored; the full nav surface (`SearchHelpCenter`, articles, collections, checklists, feature requests, `AskAI`, `StartClassicForm`, `OpenConversations`) is on the facade; `IdentifyContactAsync`/`UpdateContactAsync`/`ClearIdentityAsync`/`IsUserIdentified`/`GetIdentity` work and re-push `session-update` so the widget reflects the identified user.

## Follow-up
- Wire `Dependencies.Metadata = new WindowsMetadataProvider(...)` in `GleapWebView2Host` (Gleap.WebView2 — Windows-only edit).
- Part 2b (feedback `/bugs/v2`), Part 2c (realtime/replay).
