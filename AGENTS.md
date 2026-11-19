# Gleap C# SDK

Gleap for .NET apps: Windows desktop (WPF with WebView2), .NET MAUI (Android, iOS, Mac Catalyst) and Unity. Pure C#: the messenger, help center, surveys and forms are Gleap's web widget (`messenger-app.gleap.io`) in a WebView. `Gleap.Core` holds the engine (session, config, the widget's message protocol, data collection, feedback, outbound polling) behind the static `Gleap` class; each platform package supplies the WebView and the other seams (`IWebViewChannel`, `IHttpTransport`, `IKeyValueStore`, `IMetadataProvider`, `IScreenshotProvider`). No native iOS or Android SDK is wrapped: the binding projects the design in `docs/superpowers/` describes were never built.

What ships:

| Package | Project | Targets | Packed by `release.yml` on |
|---|---|---|---|
| `Gleap.Core` | `src/Gleap.Core` | netstandard2.0 | Linux |
| `Gleap.Extensions.Logging` | `src/Gleap.Extensions.Logging` | netstandard2.0 | Linux |
| `Gleap.WebView2` | `src/Gleap.WebView2` | net8.0-windows10.0.19041.0 (WPF) | Windows |
| `Gleap.Maui` | `maui/Gleap.Maui` | net10.0-android, -ios, -maccatalyst | macOS |
| Unity package `com.gleap.sdk` | `unity/com.gleap.sdk` | Unity 2021.3+, installed from the git tag | (no build) |

Pushing a `v*` tag publishes the four NuGet packages and is the Unity install ref. Apps pin versions, so every version stays out there for years; a NuGet version cannot be republished, only unlisted.

## Commands

```bash
sh .githooks/install.sh                    # once per clone: git push runs scripts/lint-new.sh
dotnet build Gleap.sln                     # Gleap.Core, Gleap.Extensions.Logging and the tests, on any OS
dotnet build src/Gleap.WebView2/Gleap.WebView2.csproj -p:EnableWindowsTargeting=true   # the Windows package on macOS or Linux
dotnet build maui/Gleap.Maui/Gleap.Maui.csproj                                         # needs the maui workload; macOS builds all three targets
dotnet test --project tests/Gleap.Core.Tests                                           # 237 tests, about 10 s with the build
dotnet test --project tests/Gleap.Core.Tests --filter-class '*ManagedBackendOutboundTests'
dotnet test --project tests/Gleap.Core.Tests --filter-method '*Poll_*'
sh scripts/coverage.sh                     # what CI runs: the tests, line coverage and its floor
sh scripts/lint-new.sh                     # warnings and whitespace on the lines this branch adds compared with origin/main
sh scripts/lint-new.sh --all               # every finding, old code included (about 100): for reading, nothing gates on it
sh scripts/api-new.sh                      # the public API of the changed shipped projects against a build of the fork point
dotnet format whitespace --folder --include <file>   # fix one file's whitespace
```

Needs the .NET 10 SDK (any 10.0.x), the maui workload only for `maui/` (`dotnet workload install maui`, plus Xcode on macOS), and `jq` for the Claude hooks. `global.json` only selects Microsoft.Testing.Platform for `dotnet test`; it pins no SDK.

CI (`.github/workflows/ci.yml`) runs on every PR and push to `main`: the shipped projects with `-warnaserror` (Core, Logging and WebView2 on Linux, Gleap.Maui on macOS), the WPF sample, and `sh scripts/coverage.sh`. `.github/workflows/quality.yml` runs `lint-new.sh` and `api-new.sh` on PRs, on macOS, with the base branch's copies of the scripts, `.editorconfig` and `dotnet-tools.json`. Both install the .NET 10 SDK into a folder of their own.

Done means `sh scripts/lint-new.sh` and the tests pass, `sh scripts/api-new.sh` passes when you changed a shipped project, and CI is green. Say which commands you ran and what they printed. Several sessions share each dev machine: the tests take seconds, so run them all.

New worktree: the .NET 10 SDK and `sh .githooks/install.sh` are all it needs; the SDK reads no `.env`.

`git push` runs `sh scripts/lint-new.sh` (`.githooks/pre-push`). Agents fix what it reports and push again; they never use `--no-verify`.

## Code

New files and the code you change follow `.claude/rules/code-conventions.md`, code that runs inside customers' apps (`src/`, `maui/`, `unity/`) follows `.claude/rules/host-app.md`, and tests follow `.claude/rules/testing.md`. Read them before your first edit: old code here is no precedent.

## Public API

Everything public in the four assemblies and in `unity/com.gleap.sdk/Runtime` is public API: the `Gleap` class, `ManagedBackend` and its `Dependencies`, the models and enums, the platform entry points (`GleapMessenger`, `GleapWebView2Host`, `GleapMaui`, `GleapUnity`), the event names passed to `RegisterListener`, and the interfaces customers implement (`IWebViewChannel` is implemented by Unity customers' WebView plugins).

- **Never remove, rename or change the meaning of any of it,** beta versions included. Add instead: an optional parameter at the end, an overload, a new member. Parameter names are API too (named arguments).
- **Deprecate, don't delete:** keep the behaviour, mark it `[Obsolete("Use … instead.")]` as `GleapMessenger.SdkKey` does, and say so in `CHANGELOG.md`.
- **A new member on a public interface breaks every outside implementation** (`IGleapBackend`, the seams). `scripts/api-new.sh` reports it as CP0006 like any other break.
- **The same call on every platform:** a call a platform cannot serve is a documented no-op there, not a missing method.
- A breaking change needs a person's approval. Once approved, a commit on the branch carries the trailer `API-Break-Approved: <what and why>`, and `api-new.sh` lists the break instead of failing.
- A change to the API updates `README.md` and `CHANGELOG.md` in the same PR.

## Security and privacy

- The SDK runs inside customers' apps, next to their users' data. Network logs are redacted before they are buffered (`NetworkLogBuffer`); screenshots and replay frames leave out what `GleapCensor` marks; `excludeData` is honoured. New capture keeps both: nothing a customer excluded reaches Gleap.
- The widget is a web page: every message from it is untrusted input, parsed defensively (`WebViewBridge.Incoming.cs`). `open-url` opens only `http` and `https` URLs (`OpenExternalUrl` in `GleapWebView2Host` and `GleapMaui`).
- Script for the widget is never built by string concatenation: messages go through `WebViewBridge` and `IJsonSerializer` into `sendMessage(<json>)`.
- Never log a user's data, a user hash, a token or a request body; the SDK's own diagnostics are `Debug.WriteLine("Gleap: …")` lines.
- This repository is public. The sample's Project ID is a public demo project; never commit another Project ID, a key or a certificate. NuGet publishing uses OIDC; no key exists here.

## Traps

- **No SDK pin, and analyzer rules follow the SDK.** Without an `sdk` entry in `global.json`, `dotnet` takes the newest SDK installed, and `AnalysisLevel` is `latest-recommended`, so a new SDK brings new rules: when the runner images added SDK 11.0.100, CA2026 failed a PR that changed only docs. CI installs SDK 10 in a folder of its own; `release.yml` does not, so a tag builds with whatever SDK the runner image has, with `LangVersion=latest`. An `sdk` entry in `global.json` changes what the release builds with: stop and ask.
- **An up-to-date project prints no warnings.** To see them, `dotnet build --no-incremental`; `lint-new.sh` does that.
- **`README.md` and `LICENSE.md` are packed into all four NuGet packages.** A change to either changes what the next release ships.
- **The version lives in three places, kept in step:** `<Version>` in `Directory.Build.props`, `Gleap.Version` in `src/Gleap.Core/Gleap.cs`, `version` in `unity/com.gleap.sdk/package.json`, plus the install line in `README.md`. Releases are made by people.
- **Unity compiles `unity/com.gleap.sdk/Runtime/*.cs`, nothing here does.** Unity 2021.3 means C# 9: block-scoped namespaces, no global usings. That code compiles against the committed `Runtime/Plugins/Gleap.Core.dll`, not against `src/`, and that DLL is older than the source: it reports `0.1.0-beta.1`. A Core change reaches Unity only when someone rebuilds and commits the DLLs, which is a release step.
- **`Microsoft.Web.WebView2` floats at `1.0.*`:** each restore takes the newest, and the WebView2 package's dependency moves with it.
- **Gleap.Maui's Windows target** builds only on Windows and is not shipped; the release packs on macOS. Its Android build warns MSB3243 (the explicit `Mono.Android.Export` reference); CI keeps that one a warning.
- **The Android build is not deterministic:** `_Microsoft.Android.Resource.Designer.dll` is generated anew, so two builds of the same commit give a `Gleap.Maui.dll` for Android with a different timestamp, MVID and debug directory, but the same IL.
- **`.editorconfig` asks for UTF-8 with a BOM;** 52 of the 167 C# files have none, and agents write files without one. The edit hook fixes new files; never re-encode an old one.
- **`docs/superpowers/`** holds the design and implementation plans from the first build: background, not current truth. Section 5 of the design spec still describes the widget's message protocol.
- **.NET 8 is out of support since 2026-11-10,** and `Gleap.WebView2` targets it. Changing a shipped target framework is a product decision.
- **The samples talk to the live widget** with the public demo project: a submitted form creates a real ticket there.

## Tests

New behaviour and bug fixes start with a test that fails; refactors leave tests unchanged. Full rules: `.claude/rules/testing.md`.

- xUnit.net v3 on Microsoft.Testing.Platform, net10.0, in `tests/Gleap.Core.Tests`; a hand-written fake for every seam in `tests/Gleap.Core.Tests/Fakes`. No mocking or assertion library.
- `Gleap.Core` and `Gleap.Extensions.Logging` are tested without a device: drive `ManagedBackend` through `FakeWebViewChannel` and `FakeHttpTransport`. The WebView2 control, the MAUI channels and the Unity runtime have no automated tests: say what you verified by hand and how (`WINDOWS-RUNBOOK.md` for the WPF sample).
- Never edit, skip or delete an existing test to make a change pass. If one looks wrong, stop and report `BLOCKED` with the reason.

## Customer requests

A ticket is one customer's view. A feature belongs in the SDK only if most customers would want it, or behind an option every customer can use. A bug report is first checked against what the code is meant to do (tests, comments, docs, history); if it works as designed, change nothing and say what the customer can do instead. Never special-case one customer in code: no Project IDs, user ids, names or domains in conditions. If the only fix that works is specific to one customer, stop and ask.

## Stop and ask

Keep going while a step needs no input. Stop and ask before: releasing (a version bump, a `v*` tag), adding or upgrading a package in a shipped project (it lands in every customer's dependency graph, and Unity bundles DLLs by hand), changing a target framework, `Directory.Build.props`, `global.json` or `.github/workflows/release.yml`, a breaking change to the public API, changing a message name or payload the widget exchanges (old SDKs and the current widget, and the other way round, must keep working), rebuilding the Unity DLLs, running a sample against the live widget, and merging.

## Release

Only people release. A `v<Version>` tag runs `.github/workflows/release.yml`: Core and Logging packed on Linux, WebView2 on Windows, Gleap.Maui on macOS, all pushed to nuget.org with trusted publishing; Unity installs from the same tag. A broken release is fixed by a new version.
