# Gleap for Unity (`com.gleap.sdk`)

Unity binding that reuses the platform-agnostic **`Gleap.Core`** engine. Unity supplies persistence
(`PlayerPrefs`), device metadata (`SystemInfo`), and console-log capture; you supply a WebView channel
that renders the widget.

> **Status: authored, NOT yet compiled/verified in a Unity project.** Verify + adjust on first import.

## Install
1. Build `Gleap.Core` for `netstandard2.0`: `dotnet build src/Gleap.Core -c Release`.
2. Copy `src/Gleap.Core/bin/Release/netstandard2.0/Gleap.Core.dll` **and its dependency**
   `System.Text.Json.dll` (+ its transitive deps) into `unity/com.gleap.sdk/Runtime/Plugins/`.
   (The `.asmdef` already lists `Gleap.Core.dll` as a precompiled reference.)
3. Add the package to a Unity project (Package Manager → Add package from disk → `com.gleap.sdk/package.json`),
   or copy it under `Assets/`.

## Use
```csharp
using GleapSDK;
using GleapSDK.Unity;

// `channel` is your IWebViewChannel implementation (see "WebView" below).
await GleapUnity.AttachAsync(channel, "YOUR_SDK_KEY");
Gleap.Open();
// Optional: add GleapUnityLogHook to a persistent GameObject to capture Debug.Log output.
```

## WebView (the platform-specific piece you must provide)
Unity has no built-in WebView, so you implement `GleapSDK.Bridge.IWebViewChannel` against your chosen
WebView solution and load `GleapEndpoints.Default.FrameUrl` (`https://messenger-app.gleap.io/appnew`).
The channel must, exactly like the WPF `WebView2Channel`:
- inject, before page load, the shim `window.GleapJSBridge = { gleapCallback: function (s) { <post s to native> } }`;
- forward page→native messages (the `s` string) by raising `MessageReceived`;
- implement `ExecuteJavaScript` by running the script in the page (host→widget: `window.sendMessage({...})`).

Options: a commercial plugin (Vuplex / 3D WebView) for standalone + mobile; a `.jslib` bridge for WebGL
(where you may instead front the existing Gleap JS SDK). Native iOS/Android WebView wrapping is the
alternative "wrap-native" path from the design spec.

## Known risks / verify-first
- **System.Text.Json under IL2CPP**: reflection-based (de)serialization can fail under IL2CPP/AOT. The
  `IJsonSerializer` seam exists precisely so you can swap in a Newtonsoft-based implementation if needed —
  build a `NewtonsoftJsonSerializer : IJsonSerializer` and pass it via `ManagedBackend.Dependencies.Json`.
- **Nullable annotations**: this package assumes Unity's default (nullable disabled); `Gleap.Core.dll` is
  compiled with nullable enabled, which is compatible at the binary level.
- **WebGL/consoles**: no WebView → use the JS-SDK/jslib path (WebGL) or treat as unsupported (consoles).
- DLL dependency resolution in Unity (System.Text.Json + System.Runtime.CompilerServices.Unsafe etc.) may
  need the full transitive DLL set in `Plugins/`; the first import will surface any missing ones.
