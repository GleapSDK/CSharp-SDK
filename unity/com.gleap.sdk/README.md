# Gleap for Unity (`com.gleap.sdk`)

Unity binding that reuses the platform-agnostic **`Gleap.Core`** engine. Unity supplies persistence
(`PlayerPrefs`), device metadata (`SystemInfo`), and console-log capture; you supply a WebView channel
that renders the widget.

> **Status: authored; the JSON serializer is verified, the rest is verify-on-import** (needs a Unity
> project + a WebView plugin). `Gleap.Core.dll` and its dependencies are already bundled in
> `Runtime/Plugins/`, and `NewtonsoftJsonSerializer` is verified to produce output identical to the
> engine's default serializer.

## Install
1. `Gleap.Core.dll` **and its dependencies are already in `Runtime/Plugins/`** (built for
   `netstandard2.0`). If you change `Gleap.Core`, rebuild and recopy:
   `dotnet publish src/Gleap.Core -c Release -o <tmp>` then copy the `*.dll` into `Runtime/Plugins/`.
2. Add the package (Package Manager → *Add package from disk* → `com.gleap.sdk/package.json`), or copy
   it under `Assets/`. It depends on `com.unity.nuget.newtonsoft-json` (declared in `package.json`).
3. On first import, if Unity reports **duplicate assemblies** for framework facades it already ships
   (`System.Buffers`, `System.Memory`, `System.Runtime.CompilerServices.Unsafe`, `System.Numerics.Vectors`,
   `System.Threading.Tasks.Extensions`), delete those specific DLLs from `Runtime/Plugins/`. Keep
   `Gleap.Core.dll`, `System.Text.Json.dll`, `System.Text.Encodings.Web.dll`,
   `Microsoft.Bcl.AsyncInterfaces.dll`, `System.IO.Pipelines.dll`.

## Use
```csharp
using GleapSDK;
using GleapSDK.Unity;

// Bring your own WebView (see below); route its page->native messages into the channel.
var channel = new GleapUnityWebViewChannel(js => myWebView.ExecuteJavaScript(js));
await GleapUnity.AttachAsync(channel, "YOUR_SDK_KEY"); // IL2CPP-safe Newtonsoft serializer by default
Gleap.Open();
// Optional: add GleapUnityLogHook to a persistent GameObject to capture Debug.Log output.
```

## WebView (the platform-specific piece you must provide)
Unity has no built-in WebView. Use **`GleapUnityWebViewChannel`** (plugin-agnostic) and load
`GleapEndpoints.Default.FrameUrl` (`https://messenger-app.gleap.io/appnew`):
- construct it with a delegate that runs a script in your WebView (host→widget: `window.sendMessage({...})`);
- inject `GleapUnityWebViewChannel.BridgeShim` before page load, wired so the widget's
  `GleapJSBridge.gleapCallback(jsonString)` calls the channel's `ReceiveFromWidget(json)`.

Options:
- **Standalone / mobile:** a commercial plugin (Vuplex / 3D WebView) — wire its `ExecuteJavaScript` and its
  page→native message event to the channel.
- **WebGL:** the `/appnew` frame is a cross-origin iframe you can't inject into, and the wrapper doesn't
  `postMessage` to the parent — so on WebGL front the existing **Gleap JS SDK** (`window.Gleap.initialize`)
  from a `.jslib` instead of using this managed channel. (Separate path; not included here.)

## Notes / verify-first
- **JSON / IL2CPP:** `GleapUnity` defaults to `NewtonsoftJsonSerializer` (Newtonsoft's reflection is
  IL2CPP-friendly; System.Text.Json's serializer can be stripped/broken under AOT). It is verified to
  emit the same wire shape as `SystemTextJsonSerializer` (camelCase, null-omission on POCO props, enums as
  camelCase strings, raw pass-through of the config `JsonElement`). `Gleap.Core` still uses
  `System.Text.Json` internally for DOM parsing (`JsonDocument`, reflection-free) — keep that DLL. Pass a
  different `IJsonSerializer` to `AttachAsync` to override.
- **Nullable:** the package assumes Unity's default (nullable disabled); `Gleap.Core.dll` is compiled with
  nullable enabled, which is binary-compatible.
- The `.asmdef` lists `Gleap.Core.dll`, `System.Text.Json.dll`, `Newtonsoft.Json.dll` as precompiled
  references (with `overrideReferences`).
