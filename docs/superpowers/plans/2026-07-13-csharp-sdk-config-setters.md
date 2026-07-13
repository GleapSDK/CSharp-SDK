# Gleap C# SDK — Config/misc setters + attachments in feedback

> **For agentic workers:** Implement task-by-task with TDD. Steps use `- [ ]`.

**Goal:** Close the remaining facade parity: `SetLanguage`, `IsOpened`, `ShowFeedbackButton`, `SetDisableInAppNotifications`, `PreFillForm`, `StartNetworkLogging`/`StopNetworkLogging`, `EnableDebugConsoleLog`/`DisableConsoleLog`, `SetActivationMethods` — plus include pending attachments in the `/bugs/v2` body.

**Architecture:** Mostly small state on `ManagedBackend` + toggle flags on the existing buffers. `PreFillForm` re-sends `prefill-form-data` (reusing the bootstrapper). Attachments flow from `AttachmentStore` into `FeedbackAssembler`, honoring `excludeData`.

**Tech Stack:** builds on the completed Core (105 tests). `Gleap.sln` (always pass it).

**Convention:** `CSharp-SDK/`, .NET at `~/.dotnet` (prefix `export PATH="$HOME/.dotnet:$PATH" && export DOTNET_CLI_TELEMETRY_OPTOUT=1 && `). Commits local only. One public type per file; `sealed`; file-scoped ns; XML docs; `ConfigureAwait(false)`.

---

## Task 1: Enable/disable flags on the log buffers

**Files:** Modify `src/Gleap.Core/Collection/ConsoleLogBuffer.cs`, `src/Gleap.Core/Collection/NetworkLogBuffer.cs`; Test `tests/Gleap.Core.Tests/BufferEnabledTests.cs`.

- [ ] **Step 1 — failing test.** `BufferEnabledTests.cs`:
  - `ConsoleLog_WhenDisabled_DoesNotRecord`: `var b = new ConsoleLogBuffer(new FakeClock(), 100); b.Enabled = false; b.Add("x", LogLevel.Info);` → `b.Snapshot()` empty; set `Enabled = true`, add → count 1.
  - `NetworkLog_WhenDisabled_DoesNotRecord`: `var b = new NetworkLogBuffer(100); b.Enabled = false; b.Add(new GleapNetworkLog{ Url="u" });` → empty; enable → records.
  Run `dotnet test Gleap.sln --filter BufferEnabledTests` → FAIL.

- [ ] **Step 2 — implement.** In `ConsoleLogBuffer.cs`: add `public bool Enabled { get; set; } = true;` and make `Add` return early when disabled: at the top of `Add`, `if (!Enabled) { return; }`. Same in `NetworkLogBuffer.cs` (`public bool Enabled { get; set; } = true;` + early-return at top of `Add`, before the blacklist loop).

- [ ] **Step 3 — pass + commit.** `feat: add Enabled toggle to console/network log buffers`.

---

## Task 2: Attachments in the feedback body

**Files:** Modify `src/Gleap.Core/Feedback/FeedbackAssembler.cs`, `src/Gleap.Core/ManagedBackend.cs`; Test `tests/Gleap.Core.Tests/FeedbackAssemblerAttachmentsTests.cs`.

- [ ] **Step 1 — failing test.** `FeedbackAssemblerAttachmentsTests.cs`:
  - `Build_IncludesAttachments_WhenNotExcluded`: call the new overload `FeedbackAssembler.Build(ticketData, formData, "BUG", null, false, excludeKeys: new HashSet<string>(), attachments: new[] { new GleapAttachment { Base64File = "Zm9v", FileName = "a.txt" } })`; assert `result["attachments"]` is a list with one item whose serialization contains `"a.txt"`.
  - `Build_OmitsAttachments_WhenExcluded`: excludeKeys `{"attachments"}` → result has no `attachments` key.
  Run → FAIL.

- [ ] **Step 2 — implement.** In `FeedbackAssembler.Build`, add a parameter `IReadOnlyList<GleapSDK.Models.GleapAttachment>? attachments = null` (at the end, optional to keep existing callers working). Before applying `excludeKeys`, if `attachments != null && attachments.Count > 0`, set `body["attachments"] = attachments;`. (The `excludeKeys` loop then removes it if `"attachments"` is excluded.) Add `using GleapSDK.Models;` if needed.
  In `ManagedBackend.HandleSendFeedbackAsync` and `SendSilentCrashReportAsync`, pass the attachments: change the `FeedbackAssembler.Build(...)` calls to include `_attachments.Snapshot()` as the `attachments` argument.

- [ ] **Step 3 — pass + commit.** `feat: include pending attachments in the /bugs/v2 body`.

---

## Task 3: Config/misc facade setters

**Files:** Modify `src/Gleap.Core/Session/WidgetBootstrapper.cs` (make `SendPrefill` public), `src/Gleap.Core/ManagedBackend.cs`, `src/Gleap.Core/IGleapBackend.cs`, `src/Gleap.Core/Gleap.cs`, `tests/Gleap.Core.Tests/GleapFacadeTests.cs`; Test `tests/Gleap.Core.Tests/ManagedBackendConfigTests.cs`.

- [ ] **Step 1 — failing test.** `ManagedBackendConfigTests.cs`:
  - `IsOpened_ReflectsOpenClose`: after init + ping, `backend.Open()` → `backend.IsOpened()` true; `backend.Close()` → false.
  - `StopNetworkLogging_DisablesCapture` / `DisableConsoleLog_DisablesCapture`: call the setter, then verify via a follow-on behavior isn't easily observable — instead assert no throw and that `IsOpened()`-style state is consistent. (Keep this test minimal: call each setter after init and assert none throw: `SetLanguage("de")`, `ShowFeedbackButton(true)`, `SetDisableInAppNotifications(true)`, `PreFillForm(new Dictionary<string,object?>{["email"]="a@b.c"})`, `StartNetworkLogging()`, `StopNetworkLogging()`, `EnableDebugConsoleLog()`, `DisableConsoleLog()`, `SetActivationMethods(new[]{ActivationMethod.Shake})`.)
  - `PreFillForm_AfterConnected_SendsPrefillData`: after init + ping, `backend.PreFillForm(new Dictionary<string,object?>{["email"]="a@b.c"})` → assert an executed script contains `"prefill-form-data"` and `"a@b.c"`.
  Run → FAIL.

- [ ] **Step 2 — `WidgetBootstrapper`:** change `private void SendPrefill()` to `public void SendPrefill()` (no other change; it already reads the snapshot's `PreFillFormData`).

- [ ] **Step 3 — implement in `ManagedBackend`.** Add fields:
```csharp
    private string _language = "en";
    private bool _feedbackButtonVisible;
    private bool _inAppNotificationsDisabled;
    private System.Collections.Generic.IReadOnlyDictionary<string, object?>? _prefill;
    private System.Collections.Generic.IReadOnlyList<ActivationMethod> _activationMethods = System.Array.Empty<ActivationMethod>();
```
Use `_language` where `"en"` is currently hardcoded in `InitializeAsync` (`_session.StartAsync(_language, "desktop", ct)`, `_config.LoadAsync(_language, ct)`) and in `BuildSnapshot` (`Language = _language`). Also in `BuildSnapshot`, set `PreFillFormData = _prefill`. Add the setters:
```csharp
    public void SetLanguage(string language) => _language = language;
    public bool IsOpened() => _widgetOpen;
    public void ShowFeedbackButton(bool visible) => _feedbackButtonVisible = visible;
    public void SetDisableInAppNotifications(bool disable) => _inAppNotificationsDisabled = disable;
    public void PreFillForm(System.Collections.Generic.IReadOnlyDictionary<string, object?> formData)
    {
        _prefill = formData;
        _bootstrapper.SendPrefill();
    }
    public void StartNetworkLogging() => _networkLog.Enabled = true;
    public void StopNetworkLogging() => _networkLog.Enabled = false;
    public void EnableDebugConsoleLog() => _consoleLog.Enabled = true;
    public void DisableConsoleLog() => _consoleLog.Enabled = false;
    public void SetActivationMethods(ActivationMethod[] activationMethods) => _activationMethods = activationMethods;
```
> `_feedbackButtonVisible`, `_inAppNotificationsDisabled`, `_activationMethods` are stored for the platform layer to read (feedback-button rendering, notification suppression, and shake/screenshot activation are platform concerns). Core records intent.

- [ ] **Step 4 — `IGleapBackend`:** add the 9 members:
```csharp
    void SetLanguage(string language);
    bool IsOpened();
    void ShowFeedbackButton(bool visible);
    void SetDisableInAppNotifications(bool disable);
    void PreFillForm(System.Collections.Generic.IReadOnlyDictionary<string, object?> formData);
    void StartNetworkLogging();
    void StopNetworkLogging();
    void EnableDebugConsoleLog();
    void DisableConsoleLog();
    void SetActivationMethods(ActivationMethod[] activationMethods);
```
(That's 10 — include all.)

- [ ] **Step 5 — `Gleap` facade:** add delegating statics for each (e.g. `public static void SetLanguage(string language) => Backend.SetLanguage(language);`, `public static bool IsOpened() => Backend.IsOpened();`, `public static void PreFillForm(System.Collections.Generic.IReadOnlyDictionary<string, object?> formData) => Backend.PreFillForm(formData);`, etc.).

- [ ] **Step 6 — `GleapFacadeTests` fake:** add the 10 no-op members (`IsOpened` → `false`).

- [ ] **Step 7 — pass + full suite + commit.** New filter + `--filter GleapFacadeTests` → PASS; `dotnet test Gleap.sln` green. Commit: `feat: language/prefill/feedback-button/notification/network+console-logging/activation setters`.

---

## Task 4: Quality gate

- [ ] **Step 1:** `dotnet format Gleap.sln` + `--verify-no-changes` → exit 0.
- [ ] **Step 2:** `dotnet build Gleap.sln -warnaserror --no-incremental` → 0/0.
- [ ] **Step 3:** `dotnet test Gleap.sln` → green.
- [ ] **Step 4:** Commit: `chore: pass quality gate for config setters + attachments`.

---

## Definition of Done
- The listed setters exist on the facade and take effect (`SetLanguage` drives session/config/bootstrap language; `PreFillForm` re-sends prefill; network/console logging can be toggled; feedback-button/notification/activation intent is stored for the platform layer). Attachments are included in `/bugs/v2` unless excluded.

## Follow-up
- AI tools (`SetAiTools` + `frontend-tool-execute` → `frontend-tool-result`).
- Platform: feedback-button rendering, in-app-notification suppression, shake/screenshot activation read the stored intent.
