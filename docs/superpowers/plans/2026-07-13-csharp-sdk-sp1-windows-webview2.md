# Gleap C# SDK — SP-1: Windows Desktop (WebView2 + WPF) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Ship `Gleap.WebView2` (a Windows binding that hosts the live Gleap widget in a WebView2 control via the existing `Gleap.Core` `ManagedBackend`) plus a minimal WPF sample app that provides the first live end-to-end validation of the managed bridge.

**Architecture:** `Gleap.WebView2` (`net8.0-windows`) implements `IWebViewChannel` over `CoreWebView2`, injecting a `GleapJSBridge` shim so `messenger-app.gleap.io/appnew` treats the host as native (no Messenger-App changes). A one-call `GleapWebView2Host.AttachAsync` wires `ManagedBackend` to the control. The `Gleap.Sample.Wpf` app hosts it with buttons that call the `Gleap` facade.

**Tech Stack:** `net8.0-windows`, WPF (`UseWPF`), `Microsoft.Web.WebView2` NuGet, `Gleap.Core` (project reference). `System.Text.Json` for the file store.

**Reference spec:** `docs/superpowers/specs/2026-07-13-csharp-sdk-sp1-windows-webview2-design.md`.

---

## ⚠️ Build/verification model (read first)

**This code cannot be built or run on the macOS dev box** (`net8.0-windows` + WPF + WebView2 are Windows-only). Therefore:
- Tasks 1–7 **author files** (no `dotnet build`/`dotnet test` on macOS). Where possible, run `dotnet restore` on macOS as a light sanity check (restore is cross-platform; build is not).
- The existing macOS solution `Gleap.sln` (Core + tests) is **left untouched** — its 74-test gate keeps working. The Windows projects live in a **separate `Gleap.Windows.sln`**.
- **Verification is manual, on the user's Windows machine**, per Task 8's runbook + acceptance checklist. That is SP-1's gate.
- Author carefully: WebView2/WPF APIs used here are the stable, documented core. Do not invent APIs.

**Convention:** Work in `CSharp-SDK/`. .NET at `~/.dotnet` — prefix macOS shell commands with `export PATH="$HOME/.dotnet:$PATH" && export DOTNET_CLI_TELEMETRY_OPTOUT=1 && `. Commits **local only, never push**. One public type per file; `sealed`; file-scoped namespaces; XML docs on public members (matches the Core quality bar via the shared `Directory.Build.props`).

---

## File Structure

```
CSharp-SDK/
  Gleap.Windows.sln                         # NEW: Core + WebView2 + Sample (Windows-only)
  src/Gleap.WebView2/
    Gleap.WebView2.csproj                   # net8.0-windows, UseWPF, WebView2 pkg, ref Gleap.Core
    WebView2Channel.cs                      # IWebViewChannel over CoreWebView2 (+shim, +Navigate/InitializeAsync)
    FileKeyValueStore.cs                    # IKeyValueStore -> %LOCALAPPDATA%\Gleap\session.json
    WindowsMetadataProvider.cs              # IMetadataProvider (Windows fields over DefaultMetadataProvider)
    GleapWebView2Host.cs                    # AttachAsync(webView, sdkKey) one-call bootstrap
  examples/Gleap.Sample.Wpf/
    Gleap.Sample.Wpf.csproj                 # net8.0-windows WPF exe
    App.xaml
    App.xaml.cs
    MainWindow.xaml
    MainWindow.xaml.cs
  WINDOWS-RUNBOOK.md                        # build + run + acceptance checklist
```

---

## Task 1: `Gleap.WebView2` project + `Gleap.Windows.sln`

**Files:**
- Create: `CSharp-SDK/src/Gleap.WebView2/Gleap.WebView2.csproj`
- Create: `CSharp-SDK/Gleap.Windows.sln`

- [ ] **Step 1: Create the project and Windows solution**

Run (macOS ok — create/restore only, no build):

```bash
cd /Users/tobiasduelli/development/projects/gleap/CSharp-SDK
mkdir -p src/Gleap.WebView2 examples
dotnet new classlib -n Gleap.WebView2 -o src/Gleap.WebView2 -f net8.0-windows
rm -f src/Gleap.WebView2/Class1.cs
dotnet new sln -n Gleap.Windows
dotnet sln Gleap.Windows.sln add src/Gleap.Core/Gleap.Core.csproj
dotnet sln Gleap.Windows.sln add src/Gleap.WebView2/Gleap.WebView2.csproj
dotnet add src/Gleap.WebView2/Gleap.WebView2.csproj reference src/Gleap.Core/Gleap.Core.csproj
dotnet add src/Gleap.WebView2/Gleap.WebView2.csproj package Microsoft.Web.WebView2
```

- [ ] **Step 2: Set the csproj contents**

Replace `src/Gleap.WebView2/Gleap.WebView2.csproj` `<PropertyGroup>` so it reads (keep the `<ItemGroup>` package/reference entries `dotnet` added):

```xml
<PropertyGroup>
  <TargetFramework>net8.0-windows</TargetFramework>
  <UseWPF>true</UseWPF>
  <RootNamespace>GleapSDK.WebView2</RootNamespace>
  <AssemblyName>Gleap.WebView2</AssemblyName>
</PropertyGroup>
```

- [ ] **Step 3: Restore (macOS sanity check) and commit**

Run: `dotnet restore Gleap.Windows.sln` — expect restore to succeed (it may print a NETSDK warning about building Windows targets on macOS; that is fine — do NOT run `dotnet build`). If restore fails on package resolution, report it.

```bash
git add -A && git commit -m "chore: add Gleap.WebView2 project and Windows solution"
```

---

## Task 2: `WebView2Channel` — the transport

**Files:**
- Create: `CSharp-SDK/src/Gleap.WebView2/WebView2Channel.cs`

- [ ] **Step 1: Write the channel**

Create `src/Gleap.WebView2/WebView2Channel.cs`:

```csharp
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using GleapSDK.Bridge;
using Microsoft.Web.WebView2.Core;

namespace GleapSDK.WebView2;

/// <summary>
/// <see cref="IWebViewChannel"/> over a WPF <see cref="Microsoft.Web.WebView2.Wpf.WebView2"/> control.
/// Injects the <c>GleapJSBridge</c> shim so the Gleap widget's <c>/appnew</c> wrapper recognizes
/// this host as native, forwards page-&gt;host messages from <c>chrome.webview.postMessage</c>, and
/// runs host-&gt;page scripts on the UI thread.
/// </summary>
public sealed class WebView2Channel : IWebViewChannel
{
    // Installed before the page loads (AddScriptToExecuteOnDocumentCreatedAsync). appnew.html calls
    // GleapJSBridge.gleapCallback(jsonString); we forward that to the C# WebMessageReceived handler.
    private const string BridgeShim =
        "window.GleapJSBridge = { gleapCallback: function (s) { window.chrome.webview.postMessage(s); } };";

    private readonly Microsoft.Web.WebView2.Wpf.WebView2 _webView;
    private CoreWebView2? _core;

    public WebView2Channel(Microsoft.Web.WebView2.Wpf.WebView2 webView) => _webView = webView;

    /// <inheritdoc />
    public event Action<string>? MessageReceived;

    /// <summary>Ensures the CoreWebView2 exists, installs the bridge shim, and wires the receive event.
    /// Call once before <see cref="Navigate"/>.</summary>
    public async Task InitializeAsync()
    {
        await _webView.EnsureCoreWebView2Async().ConfigureAwait(true);
        _core = _webView.CoreWebView2;
        await _core.AddScriptToExecuteOnDocumentCreatedAsync(BridgeShim).ConfigureAwait(true);
        _core.WebMessageReceived += OnWebMessageReceived;
    }

    /// <summary>Navigates the control to the widget frame URL.</summary>
    public void Navigate(string url) => _core!.Navigate(url);

    /// <inheritdoc />
    public void ExecuteJavaScript(string script)
    {
        if (_webView.Dispatcher.CheckAccess())
        {
            RunScript(script);
        }
        else
        {
            _webView.Dispatcher.BeginInvoke(new Action(() => RunScript(script)));
        }
    }

    private void RunScript(string script)
    {
        try
        {
            _ = _core!.ExecuteScriptAsync(script);
        }
        catch (Exception ex)
        {
            Debug.WriteLine("Gleap: ExecuteScriptAsync failed: " + ex.Message);
        }
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        string json;
        try
        {
            json = e.TryGetWebMessageAsString();
        }
        catch (Exception ex)
        {
            Debug.WriteLine("Gleap: could not read web message: " + ex.Message);
            return;
        }
        MessageReceived?.Invoke(json);
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add -A && git commit -m "feat: WebView2Channel bridging IWebViewChannel over CoreWebView2"
```

---

## Task 3: `FileKeyValueStore`

**Files:**
- Create: `CSharp-SDK/src/Gleap.WebView2/FileKeyValueStore.cs`

- [ ] **Step 1: Write the store**

Create `src/Gleap.WebView2/FileKeyValueStore.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using GleapSDK.Session;

namespace GleapSDK.WebView2;

/// <summary>
/// <see cref="IKeyValueStore"/> persisting session ids to a JSON file under
/// <c>%LOCALAPPDATA%\Gleap\session.json</c>. Tolerates a missing or corrupt file (treated as empty).
/// </summary>
public sealed class FileKeyValueStore : IKeyValueStore
{
    private readonly string _path;
    private readonly Dictionary<string, string> _data;

    public FileKeyValueStore(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Gleap", "session.json");
        _data = Load();
    }

    public string? Get(string key) => _data.TryGetValue(key, out var value) ? value : null;

    public void Set(string key, string value)
    {
        _data[key] = value;
        Save();
    }

    public void Remove(string key)
    {
        if (_data.Remove(key))
        {
            Save();
        }
    }

    private Dictionary<string, string> Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                var json = File.ReadAllText(_path);
                return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine("Gleap: could not read session store: " + ex.Message);
        }
        return new Dictionary<string, string>();
    }

    private void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }
            File.WriteAllText(_path, JsonSerializer.Serialize(_data));
        }
        catch (Exception ex)
        {
            Debug.WriteLine("Gleap: could not write session store: " + ex.Message);
        }
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add -A && git commit -m "feat: FileKeyValueStore for persisting gleapId/gleapHash"
```

---

## Task 4: `WindowsMetadataProvider`

**Files:**
- Create: `CSharp-SDK/src/Gleap.WebView2/WindowsMetadataProvider.cs`

- [ ] **Step 1: Write the provider**

Create `src/Gleap.WebView2/WindowsMetadataProvider.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using GleapSDK.Metadata;

namespace GleapSDK.WebView2;

/// <summary>
/// Windows device metadata layered over <see cref="DefaultMetadataProvider"/>'s cross-platform subset.
/// </summary>
public sealed class WindowsMetadataProvider : IMetadataProvider
{
    private readonly DefaultMetadataProvider _baseProvider;

    public WindowsMetadataProvider(string sdkVersion) =>
        _baseProvider = new DefaultMetadataProvider("NET/Windows", sdkVersion);

    public IReadOnlyDictionary<string, object?> Collect()
    {
        var meta = new Dictionary<string, object?>();
        foreach (var kv in _baseProvider.Collect())
        {
            meta[kv.Key] = kv.Value;
        }

        meta["systemName"] = "Windows";
        meta["systemVersion"] = RuntimeInformation.OSDescription;
        meta["releaseVersionNumber"] = Environment.OSVersion.Version.ToString();
        meta["deviceName"] = Environment.MachineName;
        meta["deviceModel"] = Environment.MachineName;
        meta["buildVersionNumber"] = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "1.0.0";

        return meta;
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add -A && git commit -m "feat: WindowsMetadataProvider"
```

---

## Task 5: `GleapWebView2Host` — one-call bootstrap

**Files:**
- Create: `CSharp-SDK/src/Gleap.WebView2/GleapWebView2Host.cs`

- [ ] **Step 1: Write the host**

Create `src/Gleap.WebView2/GleapWebView2Host.cs`:

```csharp
using System.Threading.Tasks;
using GleapSDK.Http;
using GleapSDK.Serialization;

namespace GleapSDK.WebView2;

/// <summary>
/// One-call setup: builds a <see cref="WebView2Channel"/> for the given control, wires a
/// <see cref="ManagedBackend"/> with Windows persistence, attaches it to the <see cref="Gleap"/>
/// facade, initializes the session/config, and navigates to the widget.
/// </summary>
public static class GleapWebView2Host
{
    /// <summary>SDK version reported in metadata (bump with releases).</summary>
    public const string SdkVersion = "0.1.0";

    public static async Task<ManagedBackend> AttachAsync(
        Microsoft.Web.WebView2.Wpf.WebView2 webView, string sdkKey, GleapEndpoints? endpoints = null)
    {
        var resolvedEndpoints = endpoints ?? GleapEndpoints.Default;

        var channel = new WebView2Channel(webView);
        await channel.InitializeAsync().ConfigureAwait(true);

        var backend = new ManagedBackend(new ManagedBackend.Dependencies
        {
            Http = new HttpTransport(),
            Json = new SystemTextJsonSerializer(),
            Store = new FileKeyValueStore(),
            Channel = channel,
            Endpoints = resolvedEndpoints
        });

        Gleap.UseBackend(backend);
        await backend.InitializeAsync(sdkKey).ConfigureAwait(true);

        // Navigate only after the backend (and its WidgetBootstrapper) is listening, so the
        // widget's ping is handled and answered with config-update/session-update.
        channel.Navigate(resolvedEndpoints.FrameUrl);
        return backend;
    }
}
```

> Note: `ManagedBackend` builds its own `WindowsMetadataProvider`? No — in SP-0 Part 2a `ManagedBackend` hard-codes `new DefaultMetadataProvider("NET/Windows", "0.1.0")`. Using the richer `WindowsMetadataProvider` requires a Core seam to inject `IMetadataProvider` via `Dependencies`. That injection is a small Core follow-up (see spec §7 / Task 7 note); for SP-1 the `DefaultMetadataProvider` subset is sufficient to render and validate the widget. `WindowsMetadataProvider` is delivered now so the follow-up only has to wire it.

- [ ] **Step 2: Commit**

```bash
git add -A && git commit -m "feat: GleapWebView2Host one-call bootstrap"
```

---

## Task 6: `Gleap.Sample.Wpf` app

**Files:**
- Create: `CSharp-SDK/examples/Gleap.Sample.Wpf/Gleap.Sample.Wpf.csproj`
- Create: `CSharp-SDK/examples/Gleap.Sample.Wpf/App.xaml`
- Create: `CSharp-SDK/examples/Gleap.Sample.Wpf/App.xaml.cs`
- Create: `CSharp-SDK/examples/Gleap.Sample.Wpf/MainWindow.xaml`
- Create: `CSharp-SDK/examples/Gleap.Sample.Wpf/MainWindow.xaml.cs`

- [ ] **Step 1: Create the WPF project and register it**

```bash
cd /Users/tobiasduelli/development/projects/gleap/CSharp-SDK
dotnet new wpf -n Gleap.Sample.Wpf -o examples/Gleap.Sample.Wpf -f net8.0-windows
dotnet sln Gleap.Windows.sln add examples/Gleap.Sample.Wpf/Gleap.Sample.Wpf.csproj
dotnet add examples/Gleap.Sample.Wpf/Gleap.Sample.Wpf.csproj reference src/Gleap.WebView2/Gleap.WebView2.csproj
```

- [ ] **Step 2: Overwrite the csproj**

Set `examples/Gleap.Sample.Wpf/Gleap.Sample.Wpf.csproj` to:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net8.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <RootNamespace>GleapSDK.Sample.Wpf</RootNamespace>
    <AssemblyName>Gleap.Sample.Wpf</AssemblyName>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\Gleap.WebView2\Gleap.WebView2.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 3: App.xaml + App.xaml.cs**

Overwrite `examples/Gleap.Sample.Wpf/App.xaml`:

```xml
<Application x:Class="GleapSDK.Sample.Wpf.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             StartupUri="MainWindow.xaml">
    <Application.Resources />
</Application>
```

Overwrite `examples/Gleap.Sample.Wpf/App.xaml.cs`:

```csharp
using System.Windows;

namespace GleapSDK.Sample.Wpf;

public partial class App : Application
{
}
```

- [ ] **Step 4: MainWindow.xaml**

Overwrite `examples/Gleap.Sample.Wpf/MainWindow.xaml`:

```xml
<Window x:Class="GleapSDK.Sample.Wpf.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:wv2="clr-namespace:Microsoft.Web.WebView2.Wpf;assembly=Microsoft.Web.WebView2.Wpf"
        Title="Gleap WebView2 Sample" Height="720" Width="1080">
    <Grid>
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="*" />
            <ColumnDefinition Width="260" />
        </Grid.ColumnDefinitions>

        <wv2:WebView2 x:Name="WebView" Grid.Column="0" />

        <StackPanel Grid.Column="1" Margin="10">
            <TextBlock Text="SDK Key" FontWeight="Bold" />
            <TextBox x:Name="SdkKeyBox" Margin="0,4,0,8" />
            <Button Content="Attach" Click="OnAttach" Margin="0,0,0,12" />

            <Button Content="Open" Click="OnOpen" Margin="0,2" />
            <Button Content="Close" Click="OnClose" Margin="0,2" />
            <Button Content="Start Conversation" Click="OnStartConversation" Margin="0,2" />
            <Button Content="Start Bot" Click="OnStartBot" Margin="0,2" />
            <Button Content="Open Help Center" Click="OnOpenHelpCenter" Margin="0,2" />
            <Button Content="Open News" Click="OnOpenNews" Margin="0,2" />
            <Button Content="Track Event" Click="OnTrackEvent" Margin="0,2" />
            <Button Content="Set Custom Data" Click="OnSetCustomData" Margin="0,2" />

            <TextBlock x:Name="StatusText" TextWrapping="Wrap" Margin="0,12,0,0" Foreground="Gray" />
        </StackPanel>
    </Grid>
</Window>
```

- [ ] **Step 5: MainWindow.xaml.cs**

Overwrite `examples/Gleap.Sample.Wpf/MainWindow.xaml.cs`:

```csharp
using System;
using System.Windows;
using GleapSDK;
using GleapSDK.WebView2;

namespace GleapSDK.Sample.Wpf;

public partial class MainWindow : Window
{
    private bool _attached;

    public MainWindow()
    {
        InitializeComponent();
        SdkKeyBox.Text = Environment.GetEnvironmentVariable("gleap.sdkkey") ?? "";
    }

    private async void OnAttach(object sender, RoutedEventArgs e)
    {
        var sdkKey = SdkKeyBox.Text.Trim();
        if (string.IsNullOrEmpty(sdkKey))
        {
            StatusText.Text = "Enter an SDK key first.";
            return;
        }

        try
        {
            StatusText.Text = "Attaching…";
            await GleapWebView2Host.AttachAsync(WebView, sdkKey);
            _attached = true;
            StatusText.Text = "Attached. Click Open to show the widget.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Attach failed: " + ex.Message;
            MessageBox.Show(ex.ToString(), "Gleap attach failed");
        }
    }

    private void OnOpen(object sender, RoutedEventArgs e) => Guarded(() => Gleap.Open());
    private void OnClose(object sender, RoutedEventArgs e) => Guarded(() => Gleap.Close());
    private void OnStartConversation(object sender, RoutedEventArgs e) => Guarded(() => Gleap.StartConversation());
    private void OnStartBot(object sender, RoutedEventArgs e) => Guarded(() => Gleap.StartBot(""));
    private void OnOpenHelpCenter(object sender, RoutedEventArgs e) => Guarded(() => Gleap.OpenHelpCenter());
    private void OnOpenNews(object sender, RoutedEventArgs e) => Guarded(() => Gleap.OpenNews());
    private void OnTrackEvent(object sender, RoutedEventArgs e) => Guarded(() => Gleap.TrackEvent("sample-button-clicked", null));
    private void OnSetCustomData(object sender, RoutedEventArgs e) => Guarded(() => Gleap.SetCustomData("plan", "pro"));

    private void Guarded(Action action)
    {
        if (!_attached)
        {
            StatusText.Text = "Attach first.";
            return;
        }
        try
        {
            action();
        }
        catch (Exception ex)
        {
            StatusText.Text = "Error: " + ex.Message;
        }
    }
}
```

> `Gleap.StartBot("")` with an empty botId equals "start default conversation" (same as `StartConversation`), per the Core `WidgetCommands.StartConversation` mapping — kept as a distinct button to exercise the `start-bot` path explicitly.

- [ ] **Step 6: Restore + commit**

Run: `dotnet restore Gleap.Windows.sln` (macOS: restore only; expect success, no build). Report failures.

```bash
git add -A && git commit -m "feat: WPF sample app hosting the Gleap widget"
```

---

## Task 7: Windows runbook

**Files:**
- Create: `CSharp-SDK/WINDOWS-RUNBOOK.md`

- [ ] **Step 1: Write the runbook**

Create `CSharp-SDK/WINDOWS-RUNBOOK.md`:

```markdown
# Gleap C# SDK — Windows (SP-1) Runbook

Runs the WPF sample that hosts the live Gleap widget via WebView2. **Windows only.**

## Prerequisites
- Windows 10/11.
- .NET 8 SDK (`dotnet --version` ≥ 8).
- WebView2 Runtime (preinstalled on current Windows; else install the Evergreen Runtime from Microsoft).
- A valid Gleap **project SDK key** for a project with the messenger enabled.

## Build & run
```
cd CSharp-SDK
set gleap.sdkkey=YOUR_SDK_KEY        &: optional; or paste into the app's SDK Key box
dotnet run --project examples\Gleap.Sample.Wpf
```
(Optional strict build: `dotnet build Gleap.Windows.sln -warnaserror`.)

## Steps
1. The window opens with a blank web area + a button panel.
2. Paste the SDK key (if not set via env) and click **Attach**. Status shows "Attached".
3. Click **Open** → the Gleap widget home should render, themed with the project's config.
4. Click **Start Conversation** / **Start Bot** → a conversation/bot screen opens; type to the AI/bot and confirm replies stream.
5. Click **Open Help Center** / **Open News** → those screens render and articles open.
6. Click **Track Event** / **Set Custom Data**, then start a classic feedback form in the widget → confirm the form is prefilled with collected data (the `collect-ticket-data` round-trip).

## Acceptance (SP-1 done when all pass)
- [ ] Widget renders inside the window (ping handshake completed, themed by config).
- [ ] Open/Close works.
- [ ] Start Conversation / Start Bot / Open Help Center / Open News each navigate correctly.
- [ ] AI/bot chat and help-center/news content are interactive.
- [ ] Track Event / Set Custom Data raise no errors; collect-ticket-data prefills a started form.

## Known limitation
Submitting a classic feedback form does not complete yet — that needs SP-0 Part 2b (`send-feedback` → `POST /bugs/v2`). Conversational/AI/help-center/news flows are unaffected.

## If it fails
- Blank window / "Attach failed": check the SDK key and that the WebView2 Runtime is installed (the exception dialog will say if CoreWebView2 could not start).
- Widget area loads but stays unstyled/empty: open DevTools (right-click → Inspect, if enabled) and check the console; capture any errors and report them — likely a bootstrap-message shape mismatch to fix in Core.
```

- [ ] **Step 2: Commit**

```bash
git add -A && git commit -m "docs: Windows build/run/acceptance runbook for SP-1"
```

---

## Task 8: Live validation on Windows (user-run)

**This task is executed by the user on their Windows machine — it is SP-1's gate.**

- [ ] **Step 1:** Copy/clone `CSharp-SDK/` to the Windows machine.
- [ ] **Step 2:** Follow `WINDOWS-RUNBOOK.md` build & run.
- [ ] **Step 3:** Work the acceptance checklist. Record which items pass/fail.
- [ ] **Step 4:** Report results. Any failure → capture the exact symptom (exception text / browser console error) so the fix can be made in `Gleap.Core` (most likely a bootstrap-message shape) or `Gleap.WebView2`.

---

## Definition of Done (SP-1)

- `Gleap.WebView2` + `Gleap.Sample.Wpf` authored, in `Gleap.Windows.sln`, committed locally; `Gleap.sln` (macOS Core+tests) untouched and still green.
- On Windows: `dotnet run --project examples/Gleap.Sample.Wpf` builds and launches.
- The live widget renders in WebView2 and the acceptance checklist passes (modulo the known classic-form-submit limitation).
- Any shape mismatches found during live validation are fixed in Core and re-verified.

## Follow-ups
- **Core facade completion** (macOS-buildable): wire remaining `WidgetCommands` to the facade (SearchHelpCenter, articles, checklists, feature requests, AskAI, StartClassicForm) + build identify/updateContact/clearIdentity (`/sessions/identify`, `/sessions/partialupdate`); inject `IMetadataProvider` via `ManagedBackend.Dependencies` so `WindowsMetadataProvider` is used.
- **SP-0 Part 2b:** `send-feedback` → `/bugs/v2` (unblocks classic-form submit) + silent crash + upload.
- **NuGet packaging** of `Gleap.WebView2` once validation passes.
