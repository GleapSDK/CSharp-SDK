# Gleap C# SDK — AI tools (`setAiTools`)

> **For agentic workers:** Implement task-by-task with TDD. Steps use `- [ ]`.

**Goal:** `SetAiTools(AITool[])` — declare custom AI tools to the widget by including them in the `config-update` message (matching the JS SDK, which sends `aiTools`).

**Scope:** declaring tools (the primary API). The `frontend-tool-execute` → `frontend-tool-result` execution round-trip is a flagged follow-up (its exact contract is uncertain; the widget already surfaces `tool-execution` via the existing `toolExecution` callback).

**Tech Stack:** builds on the completed Core (112 tests). `Gleap.sln` (always pass it).

**Reference:** parent spec §3 (`setAiTools`; AITool = name/description/response/executionType/parameters; param = name/description/type/required/enums; JS renames `enums`→`enum`).

**Convention:** `CSharp-SDK/`, .NET at `~/.dotnet` (prefix `export PATH="$HOME/.dotnet:$PATH" && export DOTNET_CLI_TELEMETRY_OPTOUT=1 && `). Commits local only. One public type per file; `sealed`; file-scoped ns; XML docs.

---

## Task 1: AI tool models

**Files:** Create `src/Gleap.Core/Models/AIParamType.cs`, `src/Gleap.Core/Models/AIToolParameter.cs`, `src/Gleap.Core/Models/AITool.cs`; Test `tests/Gleap.Core.Tests/AIToolModelTests.cs`.

- [ ] **Step 1 — failing test.** `AIToolModelTests.cs`: build an `AITool { Name="getOrder", Description="d", Response="r", ExecutionType="auto", Parameters = new List<AIToolParameter>{ new AIToolParameter{ Name="id", Description="pd", Type=AIParamType.String, Required=true, Enums=new List<string>{"a","b"} } } }`; serialize with `SystemTextJsonSerializer`; assert JSON contains `"name":"getOrder"`, `"parameters":`, and the param's enum list is emitted under the key `"enum"` (not `"enums"`). Run `dotnet test Gleap.sln --filter AIToolModelTests` → FAIL.

- [ ] **Step 2 — implement.** `src/Gleap.Core/Models/AIParamType.cs`:
```csharp
namespace GleapSDK.Models;

/// <summary>Parameter type for an AI tool parameter.</summary>
public enum AIParamType { String, Number, Boolean }
```
`src/Gleap.Core/Models/AIToolParameter.cs`:
```csharp
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace GleapSDK.Models;

/// <summary>A single parameter of an <see cref="AITool"/>.</summary>
public sealed class AIToolParameter
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";

    // Serialized as a lowercase string ("string"/"number"/"boolean") via the global
    // JsonStringEnumConverter(CamelCase) registered in SystemTextJsonSerializer (see below).
    public AIParamType Type { get; set; }

    public bool Required { get; set; }

    /// <summary>Allowed values. Serialized as "enum" to match the web widget.</summary>
    [JsonPropertyName("enum")]
    public List<string>? Enums { get; set; }
}
```
> **Serializer note (corrected):** `JsonNamingPolicy.CamelCase` governs property *names* only, NOT enum *values* — a bare `[JsonConverter(typeof(JsonStringEnumConverter))]` emits `"String"`. To get `"string"`/`"number"`/`"boolean"`, add a **global** converter to `SystemTextJsonSerializer.Options`: `Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }`. Safe: no other enum is serialized directly (Severity/LogLevel/SurveyFormat/ActivationMethod are mapped manually in code). Do this as part of Task 1.

`src/Gleap.Core/Models/AITool.cs`:
```csharp
using System.Collections.Generic;

namespace GleapSDK.Models;

/// <summary>A custom AI tool declared to the Gleap agent.</summary>
public sealed class AITool
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Response { get; set; } = "";
    public string ExecutionType { get; set; } = "";
    public List<AIToolParameter> Parameters { get; set; } = new();
}
```
> `AIParamType` serializes lowercase via `JsonStringEnumConverter` + camelCase policy → `"string"`/`"number"`/`"boolean"`. Verify the test asserts a lowercase type value too.

- [ ] **Step 3 — pass + commit.** `feat: add AITool / AIToolParameter / AIParamType models`.

---

## Task 2: `SetAiTools` → config-update

**Files:** Modify `src/Gleap.Core/Session/SessionSnapshot.cs`, `src/Gleap.Core/Session/WidgetBootstrapper.cs`, `src/Gleap.Core/ManagedBackend.cs`, `src/Gleap.Core/IGleapBackend.cs`, `src/Gleap.Core/Gleap.cs`, `tests/Gleap.Core.Tests/GleapFacadeTests.cs`; Test `tests/Gleap.Core.Tests/ManagedBackendAiToolsTests.cs`.

- [ ] **Step 1 — failing test.** `ManagedBackendAiToolsTests.cs`:
  - `SetAiTools_BeforePing_IncludedInConfigUpdate`: init backend; `backend.SetAiTools(new[]{ new AITool{ Name="getOrder" } })`; `ch.SimulateIncoming(ping)`; assert an executed script contains `"config-update"` and `"getOrder"` (inside an `aiTools` array).
  - `SetAiTools_AfterConnected_ResendsConfigUpdate`: init + ping; count config-update scripts; `backend.SetAiTools(new[]{ new AITool{ Name="later" }})`; assert a NEW executed script contains `"config-update"` and `"later"`.
  Run → FAIL.

- [ ] **Step 2 — implement.**
  - `SessionSnapshot.cs`: add `public System.Collections.Generic.IReadOnlyList<GleapSDK.Models.AITool> AiTools { get; set; } = System.Array.Empty<GleapSDK.Models.AITool>();`
  - `WidgetBootstrapper.SendConfigUpdate`: add `["aiTools"] = s.AiTools` to the `config-update` `Data` dictionary (after `["isApp"] = true`). Also make `SendConfigUpdate` **public** (so ManagedBackend can re-send on `SetAiTools`).
  - `ManagedBackend`: add `private System.Collections.Generic.IReadOnlyList<GleapSDK.Models.AITool> _aiTools = System.Array.Empty<GleapSDK.Models.AITool>();`; in `BuildSnapshot` set `AiTools = _aiTools`; add:
```csharp
    public void SetAiTools(GleapSDK.Models.AITool[] tools)
    {
        _aiTools = tools;
        _bootstrapper.SendConfigUpdate();
    }
```
  - `IGleapBackend`: add `void SetAiTools(GleapSDK.Models.AITool[] tools);`
  - `Gleap`: add `public static void SetAiTools(Models.AITool[] tools) => Backend.SetAiTools(tools);`
  - `GleapFacadeTests` fake: add no-op `SetAiTools`.

- [ ] **Step 3 — pass + full suite + commit.** New filter + `--filter GleapFacadeTests` + `WidgetBootstrapperTests` (no regression from the extra config-update key) → PASS; `dotnet test Gleap.sln` green. Commit: `feat: SetAiTools declares AI tools via config-update`.

---

## Task 3: Quality gate

- [ ] **Step 1:** `dotnet format Gleap.sln` + `--verify-no-changes` → exit 0.
- [ ] **Step 2:** `dotnet build Gleap.sln -warnaserror --no-incremental` → 0/0.
- [ ] **Step 3:** `dotnet test Gleap.sln` → green.
- [ ] **Step 4:** Commit (skip if nothing to commit): `chore: pass quality gate for AI tools`.

---

## Definition of Done
- `Gleap.SetAiTools(tools)` includes the tools in `config-update` (re-sent if already connected); param enums serialize as `"enum"`, types lowercase.

## Follow-up
- `frontend-tool-execute` → app tool handler → `frontend-tool-result` reply (execution round-trip; contract to confirm live).
