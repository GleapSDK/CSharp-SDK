# Gleap C# SDK — Screenshot + Replay seam

> **For agentic workers:** Implement task-by-task with TDD. Steps use `- [ ]`.

**Goal:** Add the screenshot + session-replay capability to the Core as a platform seam: `IScreenshotProvider` (platform captures a base64 PNG), a `ReplayBuffer` (ring of frames the platform fills on a timer), and inclusion of `screenshot` + `replay` in the `/bugs/v2` body (honoring `excludeData`).

**Architecture:** Capture is platform (WPF window / Unity framebuffer) behind `IScreenshotProvider`; Core awaits it during feedback assembly and includes it. Replay frames are pushed via `ManagedBackend.AddReplayFrame(base64)` (platform timer) into a bounded `ReplayBuffer`; the feedback body carries `{ replay: { interval, frames } }`.

**Tech Stack:** completed Core (115 tests). `Gleap.sln` (always pass it).

**Reference:** parent spec §7 (screenshot before widget open; replay = periodic screenshots, ring ~60, `{ replay: { interval, frames } }`; `excludeData` keys `screenshot`/`replays`).

**Convention:** `CSharp-SDK/`, .NET at `~/.dotnet` (prefix `export PATH="$HOME/.dotnet:$PATH" && export DOTNET_CLI_TELEMETRY_OPTOUT=1 && `). Commits local only. One public type per file; `sealed`; file-scoped ns; XML docs; `ConfigureAwait(false)`.

---

## Task 1: `IScreenshotProvider` + `ReplayBuffer`

**Files:** Create `src/Gleap.Core/Capture/IScreenshotProvider.cs`, `src/Gleap.Core/Capture/ReplayBuffer.cs`; Test `tests/Gleap.Core.Tests/ReplayBufferTests.cs`.

- [ ] **Step 1 — failing test.** `ReplayBufferTests.cs`:
  - `AddFrame_KeepsRingBounded`: `var b = new ReplayBuffer(intervalMs: 1000, capacity: 3); for (var i=0;i<4;i++) b.AddFrame("f"+i);` → `b.Snapshot()` == `["f1","f2","f3"]`; `b.IntervalMs == 1000`.
  - `BuildReplay_ReturnsIntervalAndFrames`: after adding "a","b", `b.BuildReplay()` is a dictionary with `["interval"]==1000` and `["frames"]` a list of 2. (Return type `IReadOnlyDictionary<string, object?>`.)
  Run `dotnet test Gleap.sln --filter ReplayBufferTests` → FAIL.

- [ ] **Step 2 — implement.** `src/Gleap.Core/Capture/IScreenshotProvider.cs`:
```csharp
using System.Threading;
using System.Threading.Tasks;

namespace GleapSDK.Capture;

/// <summary>Platform hook that captures the current screen as a base64 PNG data URI
/// (e.g. "data:image/png;base64,....") or returns null if capture is unavailable.</summary>
public interface IScreenshotProvider
{
    Task<string?> CaptureScreenshotAsync(CancellationToken ct);
}
```
`src/Gleap.Core/Capture/ReplayBuffer.cs`:
```csharp
using System.Collections.Generic;
using GleapSDK.Collection;

namespace GleapSDK.Capture;

/// <summary>Bounded ring of replay frames (base64 PNGs) the platform pushes on a timer.</summary>
public sealed class ReplayBuffer
{
    private readonly RingBuffer<string> _frames;

    public ReplayBuffer(int intervalMs, int capacity)
    {
        IntervalMs = intervalMs;
        _frames = new RingBuffer<string>(capacity);
    }

    public int IntervalMs { get; }

    public void AddFrame(string base64) => _frames.Add(base64);

    public IReadOnlyList<string> Snapshot() => _frames.Snapshot();

    public IReadOnlyDictionary<string, object?> BuildReplay() => new Dictionary<string, object?>
    {
        ["interval"] = IntervalMs,
        ["frames"] = _frames.Snapshot()
    };
}
```

- [ ] **Step 3 — pass + commit.** `feat: add IScreenshotProvider and ReplayBuffer`.

---

## Task 2: FeedbackAssembler — screenshot + replay

**Files:** Modify `src/Gleap.Core/Feedback/FeedbackAssembler.cs`; Test `tests/Gleap.Core.Tests/FeedbackAssemblerCaptureTests.cs`.

- [ ] **Step 1 — failing test.** `FeedbackAssemblerCaptureTests.cs`:
  - `Build_IncludesScreenshotAndReplay_WhenNotExcluded`: call `FeedbackAssembler.Build(ticketData, formData, "BUG", null, false, new HashSet<string>(), attachments: null, screenshot: "data:image/png;base64,AAA", replay: new Dictionary<string,object?>{["interval"]=1000})`; assert `result["screenshot"]=="data:image/png;base64,AAA"` and `result["replay"]` present.
  - `Build_OmitsScreenshot_WhenExcluded`: excludeKeys `{"screenshot","replays"}` → result has neither `screenshot` nor `replay`.
  Run → FAIL.

- [ ] **Step 2 — implement.** Extend `FeedbackAssembler.Build` with two more optional trailing params: `string? screenshot = null, IReadOnlyDictionary<string, object?>? replay = null`. Before the `excludeKeys` removal loop: if `screenshot != null` set `body["screenshot"] = screenshot;` and if `replay != null` set `body["replay"] = replay;`. **Important:** the exclude key for replay is `"replays"` (per native), but the body key is `"replay"` — so after building, also honor exclusion: change the removal loop to additionally remove `"replay"` when `excludeKeys` contains `"replays"`. Concretely, after the existing `foreach (var key in excludeKeys) body.Remove(key);` add: `if (excludeKeys.Contains("replays")) { body.Remove("replay"); }`.

- [ ] **Step 3 — pass + commit.** `feat: include screenshot and replay in the /bugs/v2 body`.

---

## Task 3: Wire capture into ManagedBackend

**Files:** Modify `src/Gleap.Core/ManagedBackend.cs`, `src/Gleap.Core/IGleapBackend.cs`, `src/Gleap.Core/Gleap.cs`, `tests/Gleap.Core.Tests/GleapFacadeTests.cs`; Test `tests/Gleap.Core.Tests/ManagedBackendCaptureTests.cs`.

- [ ] **Step 1 — failing test.** `ManagedBackendCaptureTests.cs`:
  - Use a fake `IScreenshotProvider` returning `"data:image/png;base64,SHOT"`. Build deps with `Screenshot = fakeProvider`. Init + ping; enqueue `/bugs/v2` 200; simulate `send-feedback`; await `LastFeedbackTask`; assert the `/bugs/v2` request body contains `"data:image/png;base64,SHOT"`.
  - `AddReplayFrame_IncludesReplay`: build backend, init + ping, `backend.AddReplayFrame("FRAME1")`; enqueue `/bugs/v2` 200; simulate send-feedback; await; assert body contains `"replay"` and `"FRAME1"`.
  Run → FAIL.

- [ ] **Step 2 — implement in `ManagedBackend`.**
  - Add `using GleapSDK.Capture;`.
  - Add to `Dependencies`: `public IScreenshotProvider? Screenshot { get; set; }` (default null).
  - Add fields: `private ReplayBuffer _replay = new ReplayBuffer(intervalMs: 1000, capacity: 60);` and `public void AddReplayFrame(string base64) => _replay.AddFrame(base64);`.
  - In `HandleSendFeedbackAsync`, before building the body: capture the screenshot and replay honoring exclusion:
```csharp
        string? screenshot = null;
        if (_d.Screenshot != null && !excludeKeys.Contains("screenshot"))
        {
            try { screenshot = await _d.Screenshot.CaptureScreenshotAsync(default).ConfigureAwait(false); }
            catch { screenshot = null; }
        }
        var replay = _replay.Snapshot().Count > 0 && !excludeKeys.Contains("replays") ? _replay.BuildReplay() : null;
```
    then pass `screenshot: screenshot, replay: replay` to `FeedbackAssembler.Build(...)` (in addition to the existing `attachments:` argument).
  - In `SendSilentCrashReportAsync`, likewise capture screenshot/replay honoring its `excludeKeys` (crash defaults exclude screenshot+replays, so typically null) and pass them through.

- [ ] **Step 3 — `IGleapBackend`:** add `void AddReplayFrame(string base64);`

- [ ] **Step 4 — `Gleap` facade:** add `public static void AddReplayFrame(string base64) => Backend.AddReplayFrame(base64);`

- [ ] **Step 5 — `GleapFacadeTests` fake:** add no-op `AddReplayFrame`.

- [ ] **Step 6 — pass + full suite + commit.** New filter + `--filter GleapFacadeTests` → PASS; `dotnet test Gleap.sln` green. Commit: `feat: capture screenshot + replay into feedback via IScreenshotProvider`.

---

## Task 4: Quality gate

- [ ] **Step 1:** `dotnet format Gleap.sln` + `--verify-no-changes` → exit 0.
- [ ] **Step 2:** `dotnet build Gleap.sln -warnaserror --no-incremental` → 0/0.
- [ ] **Step 3:** `dotnet test Gleap.sln` → green.
- [ ] **Step 4:** Commit (skip if nothing to commit): `chore: pass quality gate for screenshot+replay`.

---

## Definition of Done
- `Dependencies.Screenshot` (platform) is captured into the `/bugs/v2` `screenshot` field on feedback (unless excluded); `AddReplayFrame` feeds a bounded replay ring included as `{ replay: { interval, frames } }` (unless `replays` excluded).

## Follow-up (platform)
- WPF/Unity/MAUI `IScreenshotProvider` implementations + a timer pushing `AddReplayFrame`.
