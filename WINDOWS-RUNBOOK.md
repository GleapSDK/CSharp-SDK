# Gleap C# SDK — Windows (SP-1) Runbook

Runs the WPF sample that hosts the live Gleap widget via WebView2. **Windows only** (the
`net8.0-windows` / WPF / WebView2 projects cannot build on macOS/Linux).

## Prerequisites
- Windows 10/11.
- .NET 8 SDK (`dotnet --version` >= 8).
- WebView2 Runtime (preinstalled on current Windows; else install the Evergreen Runtime from Microsoft).
- A valid Gleap **project SDK key** for a project with the messenger enabled.

## Build & run
```
cd CSharp-SDK
set gleap.sdkkey=YOUR_SDK_KEY
dotnet run --project examples\Gleap.Sample.Wpf
```
(You can also skip the env var and paste the key into the app's "SDK Key" box.)

Optional strict build (quality gate): `dotnet build Gleap.Windows.sln -warnaserror`.

First build note: `Microsoft.Web.WebView2` is referenced as `1.0.*` (floats to latest). After the
first successful restore, pin it to the concrete version restore selected (`dotnet list
src\Gleap.WebView2 package`) for reproducible builds.

## Steps
1. The window opens with a blank web area + a button panel on the right.
2. Paste the SDK key (if not set via env) and click **Attach**. Status shows "Attached".
3. Click **Open** -> the Gleap widget home should render, themed with the project's config.
4. Click **Start Conversation** / **Start Bot** -> a conversation/bot screen opens; type to the
   AI/bot and confirm replies stream.
5. Click **Open Help Center** / **Open News** -> those screens render and articles open.
6. Click **Track Event** / **Set Custom Data**, then start a classic feedback form in the widget ->
   confirm the form is prefilled with collected data (the `collect-ticket-data` round-trip).

## Acceptance (SP-1 done when all pass)
- [ ] Widget renders inside the window (ping handshake completed, themed by config).
- [ ] Open/Close works.
- [ ] Start Conversation / Start Bot / Open Help Center / Open News each navigate correctly.
- [ ] AI/bot chat and help-center/news content are interactive.
- [ ] Track Event / Set Custom Data raise no errors; collect-ticket-data prefills a started form.

## Known limitation
Submitting a classic feedback form does not complete yet — that needs SP-0 Part 2b
(`send-feedback` -> `POST /bugs/v2`). Conversational / AI / help-center / news flows are unaffected.

## If it fails
- Blank window / "Attach failed": check the SDK key and that the WebView2 Runtime is installed
  (the exception dialog states if CoreWebView2 could not start).
- Widget area loads but stays unstyled/empty: right-click -> Inspect (if DevTools enabled), check
  the console, and capture any errors — likely a bootstrap-message shape mismatch to fix in
  `Gleap.Core`.
