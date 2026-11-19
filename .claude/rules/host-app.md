---
paths:
  - "src/**/*.cs"
  - "maui/**/*.cs"
  - "unity/**/*.cs"
---

# Code inside the customer's app

This code runs in apps we do not own: their UI thread, their HTTP stack, their users' data, on runtimes from Unity's Mono and IL2CPP to .NET 10. For new code and the code you change; do not rewrite old code to match.

## Leave the app as it was

- **Never throw into the app.** A failure of the network, the widget or the platform is caught where it happens, logged once with `Debug.WriteLine("Gleap: …")`, and the app goes on. A callback the customer gave us (`RegisterListener`, `RegisterAgentTool`) runs inside `try`; `GleapEventDispatcher.Emit` does not do that yet and is no precedent.
- **Never block the UI thread.** No `.Result`, `.Wait()` or synchronous I/O on a path a UI thread can reach. Core awaits with `ConfigureAwait(false)`; the platform package marshals back (WebView2 `Dispatcher`, MAUI `MainThread`, `GleapUnityMainThread`), and Unity APIs (`PlayerPrefs`, `SystemInfo`, `Screen`) are touched only on Unity's main thread.
- **Everything you start, you stop:** a timer, a polling loop, an event subscription or a WebSocket has an owner that stops it (`Dispose`, a `CancellationToken`) and unsubscribes, as `GleapMessenger.Dispose` detaches its listeners.
- **Capture only what the app hands us.** Network logging goes through the handler the app adds (`CreateNetworkLoggingHandler`); no global hooks into the app's HTTP, logging or exception handling beyond what the customer turned on.
- **Static state is the one `Gleap` facade.** No other process-wide singletons; state lives in `ManagedBackend` and its dependencies.

## The widget

- The widget (`messenger-app.gleap.io`) is the same page the iOS, Android and JavaScript SDKs host. The message names, the envelope `{ name, data, shareToken }` and the handshake are a shared contract (section 5 of `docs/superpowers/specs/2026-07-13-csharp-sdk-design.md`): nothing goes to the widget before its `ping`; on `ping` the SDK sends `config-update`, `session-update`, `prefill-form-data`, `screenshot-update`, then the queued commands.
- New behaviour matches the native SDKs. Find how they do it before you design; a difference is a decision for the PR, not an accident.
- A message from the widget is untrusted input: parse it defensively, ignore unknown names and shapes, never execute what it contains. `open-url` opens only `http` and `https`.
- Old SDK versions stay in apps for years while the widget changes, and the other way round: never rename or drop a message or a field; add, and accept both shapes while both exist.

## Privacy

- Redact before you store: whatever a customer excluded (`SetNetworkLogPropsToIgnore`, blacklisted URLs, `excludeData`) never enters a buffer, a log line or a payload.
- Screenshots and replay frames leave out what `GleapCensor` marks; a new capture path honours it before it adds anything.
- The user hash, a token, a request body, an email address never go into `Debug.WriteLine` or an exception message.

## Runtimes

- `Gleap.Core` is netstandard2.0 and also runs under Unity's Mono and IL2CPP, where Unity serializes with `NewtonsoftJsonSerializer`. Its output must stay identical to `SystemTextJsonSerializer`'s: camelCase names, nulls left out, a wire name that is not the camelCase of its property set with `[JsonPropertyName]`, which both honour. IL2CPP strips what only reflection reaches: `link.xml` keeps all of `Gleap.Core` and `Gleap.Unity`, and a new assembly the Unity package reflects over needs its own entry there.
- Platform code lives in the platform package behind a seam, never `#if` in Core. In `maui/`, per-platform code sits in the `#if ANDROID`, `#elif IOS || MACCATALYST`, `#elif WINDOWS` blocks of one file, as `MauiWebViewChannel` does.
- A capability a platform lacks (shake or screenshot activation on the desktop) is a documented no-op there, with the same public call.
