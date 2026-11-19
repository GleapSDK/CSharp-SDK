---
paths:
  - "**/*.cs"
  - "**/*.csproj"
---

# Code conventions

For new files and the code you change; do not rewrite other code to match. Copy the examples below, not your neighbours: `src/Gleap.Core/ManagedBackend.cs` (1,296 lines) and `src/Gleap.WebView2/GleapMessenger.cs` (858) are far over every limit. `sh scripts/lint-new.sh` checks the mechanical part on the lines your branch adds: the compiler's nullable warnings, the analyzers `Directory.Build.props` turns on (`latest-recommended`), the style rules `.editorconfig` raises to warnings and its whitespace. The rest is on you and the review.

Sources: [.NET coding conventions](https://learn.microsoft.com/dotnet/csharp/fundamentals/coding-style/coding-conventions), [Framework design guidelines](https://learn.microsoft.com/dotnet/standard/design-guidelines/) (for everything public), [ConfigureAwait FAQ](https://devblogs.microsoft.com/dotnet/configureawait-faq/).

- **Size:** aim for files under 400 lines and never write a new one over 700; methods up to 60 lines. A file or method already too big must not grow: new behaviour goes into a new class, or into a new partial file beside it, as `WebViewBridge.Incoming.cs` splits `WebViewBridge`.
- **Layout:** one type per file, named after it; file-scoped namespaces (`GleapSDK`, `GleapSDK.<Folder>`); braces on every block; explicit accessibility; `_camelCase` private fields, PascalCase constants and static readonly fields; `readonly` wherever a field is never reassigned.
- **Nullable is on:** annotate honestly, and no `!` unless a comment says why the value cannot be null. Public parameters that accept null say so with `?`.
- **Async:** a method that awaits is `…Async` and returns `Task` or `Task<T>`; public ones take `CancellationToken ct = default` last. In `Gleap.Core` every `await` has `.ConfigureAwait(false)`: it runs under WPF, MAUI and Unity, and must not capture their UI thread. The platform packages get back to the UI thread on purpose (WebView2's `Dispatcher`, MAUI's `MainThread`, Unity's `GleapUnityMainThread`). Never `.Result` or `.Wait()`: on a UI thread that deadlocks the customer's app. `async void` only for event handlers, and those catch everything. A task started without an await (`_ = …`) catches its own exceptions.
- **Errors:** a failure of the network, the widget or the platform never reaches the customer's app as an exception: catch it where it happens, log one `Debug.WriteLine("Gleap: …")`, and degrade. A wrong argument to a public method is the caller's bug: `ArgumentException` at the entry. Catch the specific exception type where there is one; `catch (Exception)` only at such a boundary.
- **`Gleap.Core` targets netstandard2.0:** it is loaded by .NET Framework-era Unity runtimes too. Use only what netstandard2.0 offers (the compiler tells you); no new package for a newer API.
- **Unity runtime code (`unity/`)** is C# 9 for Unity 2021.3: block-scoped namespaces, no global usings. It compiles against the committed `Plugins/Gleap.Core.dll`, so it may use only what that DLL has. IL2CPP strips what only reflection reaches (`link.xml`, `.claude/rules/host-app.md`).
- **Dependencies:** no new package in a shipped project without asking first (AGENTS.md, Stop and ask). Test-only packages are asked for too.
- **Comments:** an XML doc comment on every new public member (CS1591 is switched off, so nothing reminds you). Other comments say why, in English, in one or two lines. No plan or task numbers ("SP-0", "Task 7"), people's names, dates or ticket ids: those belong in the PR. Old comments have them; no precedent.
- **Constants and wire names:** message names, event names and JSON property names are shared with the widget, the Gleap API and customers' listeners. Add new ones; never rename one.

## One job, one system

| Job | Use | Not in new code |
|---|---|---|
| JSON | `IJsonSerializer` (`SystemTextJsonSerializer`; Unity: `NewtonsoftJsonSerializer`) | `JsonSerializer` or Newtonsoft called directly in Core logic |
| HTTP to Gleap | `ApiClient` over `IHttpTransport`, URLs from `GleapEndpoints` | `new HttpClient()` in a feature, a hard-coded host |
| Messages to and from the widget | `WebViewBridge` over `IWebViewChannel`, commands in `WidgetCommands` | script strings built anywhere else |
| Time | `IClock` | `DateTime.UtcNow` or `DateTimeOffset.UtcNow` in logic |
| Storage | `IKeyValueStore` | files, MAUI `Preferences` or `PlayerPrefs` in Core |
| Device data | `IMetadataProvider` | platform calls in Core |
| The SDK's own diagnostics | `Debug.WriteLine("Gleap: …")` | `Console`, `ILogger`, a logging package in Core |
| Customers' logs | `Gleap.Extensions.Logging` (`GleapLoggerProvider`) | a second logging integration |
| Tests | xUnit.net v3 `Assert`, the fakes in `tests/Gleap.Core.Tests/Fakes` | Moq, NSubstitute, FluentAssertions |

## Examples to copy

- A small, bounded type with one job: `src/Gleap.Core/Collection/RingBuffer.cs`.
- Privacy before storage: `src/Gleap.Core/Collection/NetworkLogBuffer.cs` redacts before it buffers.
- A seam and its production implementation: `src/Gleap.Core/Time/IClock.cs` and `SystemClock.cs`, with `tests/Gleap.Core.Tests/Fakes/FakeClock.cs`.
- A class split by concern: `src/Gleap.Core/Bridge/WebViewBridge.cs` and `WebViewBridge.Incoming.cs`.
