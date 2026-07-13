# Gleap C# SDK — SP-0 (Part 1) Core Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the headless heart of `Gleap.Core` — the contracts, models, JSON seam, the WebView bridge protocol, and the session/config bootstrap — fully unit-tested against a fake WebView channel and fake HTTP, with no real WebView or platform code.

**Architecture:** A pure C# `.netstandard2.0` library. The public `Gleap` facade delegates to an internal `IGleapBackend`; SP-0 provides the `ManagedBackend`. The `WebViewBridge` speaks the exact `{name,data,shareToken}` protocol from the design spec (frame `messenger-app.gleap.io/appnew`), gated on the `ping` handshake, with an outgoing message queue. `ApiClient` + `SessionManager` + `ConfigManager` bootstrap a session and feed the bridge. Everything platform-specific (real WebView host, screenshot, native metadata) is behind an interface and stubbed here.

**Tech Stack:** .NET SDK (8.x), `netstandard2.0` library, `net8.0` xUnit test project, `System.Text.Json` (behind an `IJsonSerializer` seam so it can be swapped for Newtonsoft under Unity IL2CPP later), injectable `HttpMessageHandler` for HTTP tests.

**Reference spec:** `docs/superpowers/specs/2026-07-13-csharp-sdk-design.md` (esp. §5 bridge contract, §6 endpoints, §3 API surface).

**Scope of this plan (SP-0 Part 1):** contracts + models + JSON + `WebViewBridge` + navigation command builders + `ApiClient` (sessions/config) + `SessionManager` + `ConfigManager` + `WidgetBootstrapper` + `Gleap` facade wiring for `Initialize`/`Open`/`Close` and the messenger-open methods. **Out of scope (SP-0 Part 2, later plan):** console/network/event log buffers + truncation, `/bugs/v2` feedback & silent-crash assembly, `/sessions/ping` polling + outbound (banner/modal/survey) dispatch, replay scheduling, custom-data/ticket-attribute/tag/attachment buffers, AI-tools execution.

**Convention:** New SDK lives in its own local git repo at `CSharp-SDK/` (mirrors `Android-SDK/`, `Flutter-SDK/`). Commits are **local only — never push, never publish** (project policy). Root namespace is `GleapSDK`; public facade type is `Gleap`.

---

## File Structure

**Code-quality bar (hard requirement):** the code must be genuinely clean and idiomatic, not just functional. This is enforced mechanically, not by taste: shared build config in `Directory.Build.props`, C# conventions in `.editorconfig` with `EnforceCodeStyleInBuild`, .NET analyzers on, **one public type per file**, folder == namespace, `sealed` by default, immutability where natural, `Async` suffix + `CancellationToken` + `ConfigureAwait(false)` in library code, XML docs on public members. A final quality gate (`dotnet format --verify-no-changes` + a `-warnaserror` build) is part of Definition of Done.

```
CSharp-SDK/
  Gleap.sln
  Directory.Build.props                   # shared props: Nullable, LangVersion, analyzers, code-style, deterministic
  .editorconfig                           # enforced C# conventions
  .gitignore
  README.md
  src/Gleap.Core/
    Gleap.Core.csproj                     # netstandard2.0 (minimal: TFM + namespace + InternalsVisibleTo)
    Gleap.cs                              # public static facade (GleapSDK.Gleap)
    IGleapBackend.cs                      # backend contract
    ManagedBackend.cs                     # pure-C# backend used on desktop
    GleapEnums.cs                         # Severity, ActivationMethod, LogLevel, SurveyFormat
    Models/
      GleapUserProperty.cs
    Serialization/
      IJsonSerializer.cs
      SystemTextJsonSerializer.cs
    Bridge/
      GleapBridgeMessage.cs               # outgoing envelope {name,data,shareToken}
      IncomingBridgeMessage.cs            # parsed incoming message
      IWebViewChannel.cs                  # transport abstraction (send JS / receive msg)
      WebViewBridge.cs                    # protocol: queue + outgoing (partial)
      WebViewBridge.Incoming.cs           # protocol: ping handshake + incoming dispatch (partial)
      WidgetCommands.cs                   # navigation command builders
    Http/
      GleapEndpoints.cs                   # base URLs
      HttpResult.cs                       # HTTP result value type
      IHttpTransport.cs                   # thin wrapper over HttpClient (testable)
      HttpTransport.cs
      SessionResult.cs                    # /sessions response DTO
      ApiClient.cs                        # /sessions, /config/{token}
    Session/
      IKeyValueStore.cs                   # persistence abstraction
      InMemoryKeyValueStore.cs
      SessionManager.cs                   # gleapId/gleapHash lifecycle
      ConfigManager.cs                    # load + split flowConfig/projectActions
      SessionSnapshot.cs                  # immutable view fed to the bootstrapper
      WidgetBootstrapper.cs               # on ping -> push bootstrap sequence, flush
  tests/Gleap.Core.Tests/
    Gleap.Core.Tests.csproj               # net8.0, xUnit
    Fakes/
      FakeWebViewChannel.cs
      FakeHttpTransport.cs
    (one test file per component)
```

---

## Task 1: Repository, solution & project scaffold

**Files:**
- Create: `CSharp-SDK/Gleap.sln`
- Create: `CSharp-SDK/src/Gleap.Core/Gleap.Core.csproj`
- Create: `CSharp-SDK/tests/Gleap.Core.Tests/Gleap.Core.Tests.csproj`
- Create: `CSharp-SDK/src/Gleap.Core/Serialization/IJsonSerializer.cs` (placeholder type so the smoke test has something to reference)
- Test: `CSharp-SDK/tests/Gleap.Core.Tests/SmokeTest.cs`

- [ ] **Step 1: Verify / install the .NET SDK**

Run: `dotnet --version`
If "command not found", install on macOS: `brew install --cask dotnet-sdk` (or download the .NET 8 SDK installer). Re-run `dotnet --version` and confirm it prints `8.x` or newer.

- [ ] **Step 2: Create the directory, git repo and solution**

```bash
mkdir -p /Users/tobiasduelli/development/projects/gleap/CSharp-SDK
cd /Users/tobiasduelli/development/projects/gleap/CSharp-SDK
git init
dotnet new gitignore
dotnet new sln -n Gleap
dotnet new classlib -n Gleap.Core -o src/Gleap.Core -f netstandard2.0
dotnet new xunit -n Gleap.Core.Tests -o tests/Gleap.Core.Tests -f net8.0
rm src/Gleap.Core/Class1.cs tests/Gleap.Core.Tests/UnitTest1.cs
dotnet sln add src/Gleap.Core/Gleap.Core.csproj
dotnet sln add tests/Gleap.Core.Tests/Gleap.Core.Tests.csproj
dotnet add tests/Gleap.Core.Tests/Gleap.Core.Tests.csproj reference src/Gleap.Core/Gleap.Core.csproj
dotnet add src/Gleap.Core/Gleap.Core.csproj package System.Text.Json
```

- [ ] **Step 3: Add shared build config (`Directory.Build.props`) — this enforces the quality bar**

Create `Directory.Build.props` at `CSharp-SDK/Directory.Build.props`:

```xml
<Project>
  <PropertyGroup>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>disable</ImplicitUsings>
    <Deterministic>true</Deterministic>
    <EnableNETAnalyzers>true</EnableNETAnalyzers>
    <AnalysisLevel>latest-recommended</AnalysisLevel>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
    <!-- CS1591 (missing XML doc) stays a warning, not an error, so TDD tasks aren't brittle;
         the -warnaserror quality gate in the DoD is run explicitly at the end. -->
    <NoWarn>$(NoWarn);CS1591</NoWarn>
  </PropertyGroup>
</Project>
```

- [ ] **Step 4: Add `.editorconfig` (enforced C# conventions)**

Create `.editorconfig` at `CSharp-SDK/.editorconfig`:

```ini
root = true

[*.cs]
indent_style = space
indent_size = 4
insert_final_newline = true
charset = utf-8-bom
trim_trailing_whitespace = true

# Language conventions
csharp_style_namespace_declarations = file_scoped:warning
csharp_prefer_braces = true:warning
csharp_style_expression_bodied_methods = when_on_single_line:suggestion
dotnet_style_require_accessibility_modifiers = for_non_interface_members:warning
dotnet_style_readonly_field = true:warning
csharp_prefer_simple_using_statement = true:suggestion
dotnet_diagnostic.IDE0005.severity = warning   # unnecessary usings

# Naming: private instance fields => _camelCase
dotnet_naming_rule.private_fields_underscore.symbols = private_fields
dotnet_naming_rule.private_fields_underscore.style = underscore_prefix
dotnet_naming_rule.private_fields_underscore.severity = warning
dotnet_naming_symbols.private_fields.applicable_kinds = field
dotnet_naming_symbols.private_fields.applicable_accessibilities = private
dotnet_naming_style.underscore_prefix.capitalization = camel_case
dotnet_naming_style.underscore_prefix.required_prefix = _
```

- [ ] **Step 5: Slim the project files and add a README**

Edit `src/Gleap.Core/Gleap.Core.csproj` so the first `<PropertyGroup>` is minimal (shared props now come from `Directory.Build.props`) and add the `InternalsVisibleTo`:

```xml
<PropertyGroup>
  <TargetFramework>netstandard2.0</TargetFramework>
  <RootNamespace>GleapSDK</RootNamespace>
  <AssemblyName>Gleap.Core</AssemblyName>
</PropertyGroup>

<!-- Test-only internal members (e.g. MarkConnectedForTest). -->
<ItemGroup>
  <InternalsVisibleTo Include="Gleap.Core.Tests" />
</ItemGroup>
```

Create `CSharp-SDK/README.md`:

```markdown
# Gleap C# SDK

Cross-platform C# SDK for [Gleap](https://gleap.io) — Unity, .NET MAUI, and Windows Desktop.

`Gleap.Core` is the headless, platform-agnostic engine (session, config, and the
web-widget bridge protocol). Platform packages attach a backend to the `Gleap` facade.

See `docs/superpowers/specs/2026-07-13-csharp-sdk-design.md` for the architecture.
```

- [ ] **Step 6: Add a placeholder type and a smoke test**

Create `src/Gleap.Core/Serialization/IJsonSerializer.cs`:

```csharp
namespace GleapSDK.Serialization;

public interface IJsonSerializer
{
    string Serialize(object value);
    T Deserialize<T>(string json);
}
```

Create `tests/Gleap.Core.Tests/SmokeTest.cs`:

```csharp
using GleapSDK.Serialization;
using Xunit;

namespace Gleap.Core.Tests;

public class SmokeTest
{
    [Fact]
    public void CoreAssembly_ExposesJsonSerializerContract()
    {
        Assert.NotNull(typeof(IJsonSerializer));
    }
}
```

- [ ] **Step 7: Build & run the smoke test**

Run: `cd /Users/tobiasduelli/development/projects/gleap/CSharp-SDK && dotnet test`
Expected: build succeeds, 1 test passes.

- [ ] **Step 8: Commit (local only)**

```bash
cd /Users/tobiasduelli/development/projects/gleap/CSharp-SDK
git add -A
git commit -m "chore: scaffold Gleap.Core solution, shared build props and editorconfig"
```

---

## Task 2: Enums and GleapUserProperty model

**Files:**
- Create: `CSharp-SDK/src/Gleap.Core/GleapEnums.cs`
- Create: `CSharp-SDK/src/Gleap.Core/Models/GleapUserProperty.cs`
- Test: `CSharp-SDK/tests/Gleap.Core.Tests/GleapUserPropertyTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/Gleap.Core.Tests/GleapUserPropertyTests.cs`:

```csharp
using System.Collections.Generic;
using GleapSDK;
using GleapSDK.Models;
using Xunit;

namespace Gleap.Core.Tests;

public class GleapUserPropertyTests
{
    [Fact]
    public void UserProperty_HoldsAllParityFields()
    {
        var p = new GleapUserProperty
        {
            UserId = "u1",
            Name = "Ada",
            Email = "ada@example.com",
            Phone = "123",
            Plan = "pro",
            CompanyName = "Acme",
            CompanyId = "c1",
            Avatar = "https://a",
            Lang = "en",
            Value = 42.0,
            Sla = 3.0,
            CustomData = new Dictionary<string, object> { ["k"] = "v" }
        };

        Assert.Equal("u1", p.UserId);
        Assert.Equal(42.0, p.Value);
        Assert.Equal("v", p.CustomData!["k"]);
    }

    [Fact]
    public void Enums_HaveParityMembers()
    {
        Assert.Equal(3, System.Enum.GetValues(typeof(Severity)).Length);
        Assert.Equal(3, System.Enum.GetValues(typeof(LogLevel)).Length);
        Assert.Equal(2, System.Enum.GetValues(typeof(ActivationMethod)).Length);
        Assert.Equal(2, System.Enum.GetValues(typeof(SurveyFormat)).Length);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter GleapUserPropertyTests`
Expected: FAIL — `GleapUserProperty` / enums do not exist.

- [ ] **Step 3: Write the enums**

Create `src/Gleap.Core/GleapEnums.cs`:

```csharp
namespace GleapSDK;

public enum Severity { Low, Medium, High }

public enum ActivationMethod { Shake, Screenshot }

public enum LogLevel { Error, Warning, Info }

public enum SurveyFormat { Survey, SurveyFull }
```

- [ ] **Step 4: Write the model**

Create `src/Gleap.Core/Models/GleapUserProperty.cs`:

```csharp
using System.Collections.Generic;

namespace GleapSDK.Models;

public sealed class GleapUserProperty
{
    public string? UserId { get; set; }
    public string? Name { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Plan { get; set; }
    public string? CompanyName { get; set; }
    public string? CompanyId { get; set; }
    public string? Avatar { get; set; }
    public string? Lang { get; set; }
    public double? Value { get; set; }
    public double? Sla { get; set; }
    public Dictionary<string, object>? CustomData { get; set; }
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test --filter GleapUserPropertyTests`
Expected: PASS (2 tests).

- [ ] **Step 6: Commit**

```bash
git add -A && git commit -m "feat: add core enums and GleapUserProperty model"
```

---

## Task 3: JSON serializer seam

**Files:**
- Modify: `CSharp-SDK/src/Gleap.Core/Serialization/IJsonSerializer.cs` (already created in Task 1 — leave as-is)
- Create: `CSharp-SDK/src/Gleap.Core/Serialization/SystemTextJsonSerializer.cs`
- Test: `CSharp-SDK/tests/Gleap.Core.Tests/SystemTextJsonSerializerTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/Gleap.Core.Tests/SystemTextJsonSerializerTests.cs`:

```csharp
using GleapSDK.Models;
using GleapSDK.Serialization;
using Xunit;

namespace Gleap.Core.Tests;

public class SystemTextJsonSerializerTests
{
    private readonly IJsonSerializer _json = new SystemTextJsonSerializer();

    [Fact]
    public void Serialize_UsesCamelCase_AndOmitsNulls()
    {
        var p = new GleapUserProperty { UserId = "u1", Value = 5.0 };

        var s = _json.Serialize(p);

        Assert.Contains("\"userId\":\"u1\"", s);
        Assert.Contains("\"value\":5", s);
        Assert.DoesNotContain("email", s); // null omitted
    }

    [Fact]
    public void Deserialize_RoundTrips()
    {
        var s = _json.Serialize(new GleapUserProperty { Email = "a@b.c" });
        var back = _json.Deserialize<GleapUserProperty>(s);
        Assert.Equal("a@b.c", back.Email);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter SystemTextJsonSerializerTests`
Expected: FAIL — `SystemTextJsonSerializer` does not exist.

- [ ] **Step 3: Write the implementation**

Create `src/Gleap.Core/Serialization/SystemTextJsonSerializer.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GleapSDK.Serialization;

public sealed class SystemTextJsonSerializer : IJsonSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public string Serialize(object value) => JsonSerializer.Serialize(value, Options);

    public T Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Options)!;
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter SystemTextJsonSerializerTests`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "feat: add System.Text.Json serializer behind IJsonSerializer seam"
```

---

## Task 4: Outgoing bridge envelope

**Files:**
- Create: `CSharp-SDK/src/Gleap.Core/Bridge/GleapBridgeMessage.cs`
- Test: `CSharp-SDK/tests/Gleap.Core.Tests/GleapBridgeMessageTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/Gleap.Core.Tests/GleapBridgeMessageTests.cs`:

```csharp
using System.Collections.Generic;
using GleapSDK.Bridge;
using GleapSDK.Serialization;
using Xunit;

namespace Gleap.Core.Tests;

public class GleapBridgeMessageTests
{
    private readonly IJsonSerializer _json = new SystemTextJsonSerializer();

    [Fact]
    public void Serializes_ToNameDataShareToken()
    {
        var msg = new GleapBridgeMessage
        {
            Name = "open-conversation",
            Data = new Dictionary<string, object> { ["shareToken"] = "abc" }
        };

        var s = _json.Serialize(msg);

        Assert.Contains("\"name\":\"open-conversation\"", s);
        Assert.Contains("\"data\":", s);
    }

    [Fact]
    public void OmitsShareToken_WhenNull()
    {
        var s = _json.Serialize(new GleapBridgeMessage { Name = "ping" });
        Assert.DoesNotContain("shareToken", s);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter GleapBridgeMessageTests`
Expected: FAIL — `GleapBridgeMessage` does not exist.

- [ ] **Step 3: Write the envelope**

Create `src/Gleap.Core/Bridge/GleapBridgeMessage.cs`:

```csharp
namespace GleapSDK.Bridge;

/// <summary>Outgoing message envelope: { name, data, shareToken? }.</summary>
public sealed class GleapBridgeMessage
{
    public string Name { get; set; } = "";
    public object? Data { get; set; }
    public string? ShareToken { get; set; }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter GleapBridgeMessageTests`
Expected: PASS (2 tests). (`Data` null is also omitted by the serializer — acceptable.)

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "feat: add outgoing GleapBridgeMessage envelope"
```

---

## Task 5: WebView channel abstraction + fake

**Files:**
- Create: `CSharp-SDK/src/Gleap.Core/Bridge/IWebViewChannel.cs`
- Create: `CSharp-SDK/tests/Gleap.Core.Tests/Fakes/FakeWebViewChannel.cs`
- Test: `CSharp-SDK/tests/Gleap.Core.Tests/FakeWebViewChannelTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/Gleap.Core.Tests/FakeWebViewChannelTests.cs`:

```csharp
using System.Collections.Generic;
using Gleap.Core.Tests.Fakes;
using Xunit;

namespace Gleap.Core.Tests;

public class FakeWebViewChannelTests
{
    [Fact]
    public void RecordsExecutedScripts()
    {
        var ch = new FakeWebViewChannel();
        ch.ExecuteJavaScript("sendMessage({});");
        Assert.Single(ch.ExecutedScripts);
    }

    [Fact]
    public void RaisesMessageReceived_WhenPageSends()
    {
        var ch = new FakeWebViewChannel();
        var got = new List<string>();
        ch.MessageReceived += got.Add;

        ch.SimulateIncoming("{\"name\":\"ping\"}");

        Assert.Equal("{\"name\":\"ping\"}", got[0]);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter FakeWebViewChannelTests`
Expected: FAIL — `IWebViewChannel` / `FakeWebViewChannel` do not exist.

- [ ] **Step 3: Write the abstraction**

Create `src/Gleap.Core/Bridge/IWebViewChannel.cs`:

```csharp
using System;

namespace GleapSDK.Bridge;

/// <summary>
/// Transport between native/managed and the hosted web widget.
/// A platform WebView host (WebView2, Unity plugin) implements this;
/// tests use a fake. Native side runs "sendMessage(&lt;json&gt;)" and receives
/// raw JSON strings the page posts back.
/// </summary>
public interface IWebViewChannel
{
    void ExecuteJavaScript(string script);
    event Action<string> MessageReceived;
}
```

- [ ] **Step 4: Write the fake**

Create `tests/Gleap.Core.Tests/Fakes/FakeWebViewChannel.cs`:

```csharp
using System;
using System.Collections.Generic;
using GleapSDK.Bridge;

namespace Gleap.Core.Tests.Fakes;

public sealed class FakeWebViewChannel : IWebViewChannel
{
    public List<string> ExecutedScripts { get; } = new();

    public void ExecuteJavaScript(string script) => ExecutedScripts.Add(script);

    public event Action<string>? MessageReceived;

    /// <summary>Simulate the web page posting a message to native.</summary>
    public void SimulateIncoming(string json) => MessageReceived?.Invoke(json);
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test --filter FakeWebViewChannelTests`
Expected: PASS (2 tests).

- [ ] **Step 6: Commit**

```bash
git add -A && git commit -m "feat: add IWebViewChannel abstraction and test fake"
```

---

## Task 6: WebViewBridge — outgoing send + queue-until-connected

**Files:**
- Create: `CSharp-SDK/src/Gleap.Core/Bridge/WebViewBridge.cs`
- Test: `CSharp-SDK/tests/Gleap.Core.Tests/WebViewBridgeOutgoingTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/Gleap.Core.Tests/WebViewBridgeOutgoingTests.cs`:

```csharp
using Gleap.Core.Tests.Fakes;
using GleapSDK.Bridge;
using GleapSDK.Serialization;
using Xunit;

namespace Gleap.Core.Tests;

public class WebViewBridgeOutgoingTests
{
    private static WebViewBridge NewBridge(FakeWebViewChannel ch) =>
        new WebViewBridge(ch, new SystemTextJsonSerializer());

    [Fact]
    public void Send_BeforeConnected_IsQueued_NotExecuted()
    {
        var ch = new FakeWebViewChannel();
        var bridge = NewBridge(ch);

        bridge.Send(new GleapBridgeMessage { Name = "open-conversations" });

        Assert.Empty(ch.ExecutedScripts);
    }

    [Fact]
    public void Send_WrapsInSendMessageCall()
    {
        var ch = new FakeWebViewChannel();
        var bridge = NewBridge(ch);
        bridge.MarkConnectedForTest();

        bridge.Send(new GleapBridgeMessage { Name = "open-news" });

        Assert.Single(ch.ExecutedScripts);
        Assert.StartsWith("sendMessage(", ch.ExecutedScripts[0]);
        Assert.EndsWith(");", ch.ExecutedScripts[0]);
        Assert.Contains("\"name\":\"open-news\"", ch.ExecutedScripts[0]);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter WebViewBridgeOutgoingTests`
Expected: FAIL — `WebViewBridge` does not exist.

- [ ] **Step 3: Write the outgoing half of the bridge**

Create `src/Gleap.Core/Bridge/WebViewBridge.cs`:

```csharp
using System.Collections.Generic;
using GleapSDK.Serialization;

namespace GleapSDK.Bridge;

/// <summary>
/// Speaks the Gleap web-widget protocol over an <see cref="IWebViewChannel"/>.
/// Outgoing messages are queued until the widget sends "ping" (handshake),
/// then flushed. Incoming dispatch is added in a later task.
/// </summary>
public sealed partial class WebViewBridge
{
    private readonly IWebViewChannel _channel;
    private readonly IJsonSerializer _json;
    private readonly Queue<GleapBridgeMessage> _queue = new();
    private bool _connected;

    public WebViewBridge(IWebViewChannel channel, IJsonSerializer json)
    {
        _channel = channel;
        _json = json;
        // Wrapped in a lambda (not a method group): OnMessageReceived is a partial
        // method whose body is added in Task 7. A method group of an unimplemented
        // partial method does not compile; a call inside a lambda is simply elided.
        _channel.MessageReceived += json => OnMessageReceived(json);
    }

    public bool IsConnected => _connected;

    /// <summary>Send a message, queuing it until the handshake completes.</summary>
    public void Send(GleapBridgeMessage message)
    {
        if (!_connected)
        {
            _queue.Enqueue(message);
            return;
        }
        Execute(message);
    }

    private void Execute(GleapBridgeMessage message)
    {
        var json = _json.Serialize(message);
        _channel.ExecuteJavaScript("sendMessage(" + json + ");");
    }

    private void FlushQueue()
    {
        while (_queue.Count > 0)
            Execute(_queue.Dequeue());
    }

    // Incoming dispatch is implemented in the partial in WebViewBridge.Incoming.cs (Task 7).
    partial void OnMessageReceived(string json);

    // Test hook — real connection happens on "ping" (Task 7).
    internal void MarkConnectedForTest()
    {
        _connected = true;
        FlushQueue();
    }
}
```

> Note: `OnMessageReceived` is declared as a `partial` method here and implemented in Task 7. A partial method with no implementation compiles to a no-op, so this task builds and passes on its own.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter WebViewBridgeOutgoingTests`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "feat: WebViewBridge outgoing send with queue-until-connected"
```

---

## Task 7: WebViewBridge — incoming parse, ping handshake & dispatch

**Files:**
- Create: `CSharp-SDK/src/Gleap.Core/Bridge/IncomingBridgeMessage.cs`
- Create: `CSharp-SDK/src/Gleap.Core/Bridge/WebViewBridge.Incoming.cs`
- Test: `CSharp-SDK/tests/Gleap.Core.Tests/WebViewBridgeIncomingTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/Gleap.Core.Tests/WebViewBridgeIncomingTests.cs`:

```csharp
using System.Collections.Generic;
using Gleap.Core.Tests.Fakes;
using GleapSDK.Bridge;
using GleapSDK.Serialization;
using Xunit;

namespace Gleap.Core.Tests;

public class WebViewBridgeIncomingTests
{
    private static WebViewBridge NewBridge(FakeWebViewChannel ch) =>
        new WebViewBridge(ch, new SystemTextJsonSerializer());

    [Fact]
    public void Ping_ConnectsAndFlushesQueue()
    {
        var ch = new FakeWebViewChannel();
        var bridge = NewBridge(ch);
        bridge.Send(new GleapBridgeMessage { Name = "open-news" }); // queued

        ch.SimulateIncoming("{\"name\":\"ping\"}");

        Assert.True(bridge.IsConnected);
        Assert.Contains(ch.ExecutedScripts, s => s.Contains("\"name\":\"open-news\""));
    }

    [Fact]
    public void Ping_RaisesPingReceived_BeforeFlush()
    {
        var ch = new FakeWebViewChannel();
        var bridge = NewBridge(ch);
        var order = new List<string>();
        bridge.PingReceived += () => order.Add("ping");
        bridge.Send(new GleapBridgeMessage { Name = "open-news" });
        // record flush by observing script execution timing via event ordering
        bridge.PingReceived += () => order.Add("bootstrap-window");

        ch.SimulateIncoming("{\"name\":\"ping\"}");

        Assert.Equal("ping", order[0]);
    }

    [Fact]
    public void RunCustomAction_ExposesNameAndTopLevelShareToken()
    {
        var ch = new FakeWebViewChannel();
        var bridge = NewBridge(ch);
        string? name = null;
        string? token = null;
        bridge.CustomActionTriggered += (n, t) => { name = n; token = t; };

        ch.SimulateIncoming("{\"name\":\"run-custom-action\",\"data\":\"myAction\",\"shareToken\":\"tok1\"}");

        Assert.Equal("myAction", name);
        Assert.Equal("tok1", token);
    }

    [Fact]
    public void CloseWidget_RaisesEvent()
    {
        var ch = new FakeWebViewChannel();
        var bridge = NewBridge(ch);
        var closed = false;
        bridge.CloseWidgetRequested += () => closed = true;

        ch.SimulateIncoming("{\"name\":\"close-widget\"}");

        Assert.True(closed);
    }

    [Fact]
    public void NotifyEvent_FlowStarted_RaisesFeedbackFlowStarted()
    {
        var ch = new FakeWebViewChannel();
        var bridge = NewBridge(ch);
        var started = false;
        bridge.FeedbackFlowStarted += _ => started = true;

        ch.SimulateIncoming("{\"name\":\"notify-event\",\"data\":{\"type\":\"flow-started\",\"data\":{}}}");

        Assert.True(started);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter WebViewBridgeIncomingTests`
Expected: FAIL — events/`IncomingBridgeMessage` do not exist.

- [ ] **Step 3: Write the incoming message model**

Create `src/Gleap.Core/Bridge/IncomingBridgeMessage.cs`:

```csharp
using System.Text.Json;

namespace GleapSDK.Bridge;

/// <summary>Parsed inbound message from the web widget.</summary>
public readonly struct IncomingBridgeMessage
{
    public string Name { get; }
    public JsonElement Data { get; }        // may be Undefined
    public string? ShareToken { get; }      // top-level (used by run-custom-action)

    public IncomingBridgeMessage(string name, JsonElement data, string? shareToken)
    {
        Name = name;
        Data = data;
        ShareToken = shareToken;
    }

    public static IncomingBridgeMessage Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var name = root.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
        var data = root.TryGetProperty("data", out var d) ? d.Clone() : default;
        string? token = root.TryGetProperty("shareToken", out var t) ? t.GetString() : null;
        return new IncomingBridgeMessage(name, data, token);
    }
}
```

- [ ] **Step 4: Write the incoming dispatch partial**

Create `src/Gleap.Core/Bridge/WebViewBridge.Incoming.cs`:

```csharp
using System;
using System.Text.Json;

namespace GleapSDK.Bridge;

public sealed partial class WebViewBridge
{
    /// <summary>Raised on "ping" — orchestrator pushes the bootstrap sequence here.</summary>
    public event Action? PingReceived;
    public event Action? CloseWidgetRequested;
    /// <summary>(actionName, shareToken?)</summary>
    public event Action<string, string?>? CustomActionTriggered;
    public event Action<JsonElement>? FeedbackFlowStarted;
    public event Action<string>? OpenUrlRequested;
    public event Action<JsonElement>? SendFeedbackRequested;

    partial void OnMessageReceived(string json)
    {
        IncomingBridgeMessage msg;
        try { msg = IncomingBridgeMessage.Parse(json); }
        catch { return; } // ignore malformed frames

        switch (msg.Name)
        {
            case "ping":
                _connected = true;
                PingReceived?.Invoke();   // orchestrator sends bootstrap while connected
                FlushQueue();             // then queued nav commands
                break;

            case "close-widget":
                CloseWidgetRequested?.Invoke();
                break;

            case "run-custom-action":
                var actionName = msg.Data.ValueKind == JsonValueKind.String ? msg.Data.GetString() : null;
                if (!string.IsNullOrEmpty(actionName))
                    CustomActionTriggered?.Invoke(actionName!, msg.ShareToken);
                break;

            case "open-url":
                if (msg.Data.ValueKind == JsonValueKind.String)
                    OpenUrlRequested?.Invoke(msg.Data.GetString()!);
                break;

            case "notify-event":
                if (msg.Data.ValueKind == JsonValueKind.Object &&
                    msg.Data.TryGetProperty("type", out var type) &&
                    type.GetString() == "flow-started")
                {
                    var payload = msg.Data.TryGetProperty("data", out var d) ? d : default;
                    FeedbackFlowStarted?.Invoke(payload);
                }
                break;

            case "send-feedback":
                SendFeedbackRequested?.Invoke(msg.Data);
                break;

            // tool-execution, frontend-tool-execute, collect-ticket-data,
            // cleanup-drawings, screenshot-updated -> handled in SP-0 Part 2.
        }
    }
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test --filter WebViewBridgeIncomingTests`
Expected: PASS (5 tests).

- [ ] **Step 6: Commit**

```bash
git add -A && git commit -m "feat: WebViewBridge incoming parse, ping handshake and dispatch"
```

---

## Task 8: Navigation command builders

**Files:**
- Create: `CSharp-SDK/src/Gleap.Core/Bridge/WidgetCommands.cs`
- Test: `CSharp-SDK/tests/Gleap.Core.Tests/WidgetCommandsTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/Gleap.Core.Tests/WidgetCommandsTests.cs`:

```csharp
using GleapSDK;
using GleapSDK.Bridge;
using GleapSDK.Serialization;
using Xunit;

namespace Gleap.Core.Tests;

public class WidgetCommandsTests
{
    private readonly IJsonSerializer _json = new SystemTextJsonSerializer();

    [Fact]
    public void StartBot_MapsBotId_And_HideBackButtonIsNegation()
    {
        var msg = WidgetCommands.StartBot("bot1", showBackButton: false);
        Assert.Equal("start-bot", msg.Name);
        var s = _json.Serialize(msg);
        Assert.Contains("\"botId\":\"bot1\"", s);
        Assert.Contains("\"hideBackButton\":true", s); // !showBackButton
    }

    [Fact]
    public void StartConversation_IsStartBot_WithEmptyBotId()
    {
        var msg = WidgetCommands.StartConversation(showBackButton: true);
        Assert.Equal("start-bot", msg.Name);
        var s = _json.Serialize(msg);
        Assert.Contains("\"botId\":\"\"", s);
        Assert.Contains("\"hideBackButton\":false", s);
    }

    [Fact]
    public void OpenConversation_PutsShareTokenInData()
    {
        var msg = WidgetCommands.OpenConversation("tok");
        Assert.Equal("open-conversation", msg.Name);
        Assert.Contains("\"shareToken\":\"tok\"", _json.Serialize(msg));
    }

    [Fact]
    public void StartSurvey_SetsFlagsAndFormat()
    {
        var msg = WidgetCommands.StartSurvey("s1", SurveyFormat.SurveyFull);
        Assert.Equal("start-survey", msg.Name);
        var s = _json.Serialize(msg);
        Assert.Contains("\"flow\":\"s1\"", s);
        Assert.Contains("\"isSurvey\":true", s);
        Assert.Contains("\"format\":\"survey_full\"", s);
    }

    [Fact]
    public void HelpCenterSearch_MapsTerm()
    {
        var msg = WidgetCommands.SearchHelpCenter("reset password", showBackButton: true);
        Assert.Equal("open-helpcenter-search", msg.Name);
        Assert.Contains("\"term\":\"reset password\"", _json.Serialize(msg));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter WidgetCommandsTests`
Expected: FAIL — `WidgetCommands` does not exist.

- [ ] **Step 3: Write the command builders**

Create `src/Gleap.Core/Bridge/WidgetCommands.cs`:

```csharp
using System.Collections.Generic;

namespace GleapSDK.Bridge;

/// <summary>
/// Builds the navigation command messages the web widget understands.
/// Rule from the native SDKs: hideBackButton == !showBackButton.
/// </summary>
public static class WidgetCommands
{
    private static object Hide(bool showBackButton, params (string key, object value)[] extra)
    {
        var d = new Dictionary<string, object> { ["hideBackButton"] = !showBackButton };
        foreach (var (k, v) in extra) d[k] = v;
        return d;
    }

    public static GleapBridgeMessage StartBot(string botId, bool showBackButton) =>
        new() { Name = "start-bot", Data = Hide(showBackButton, ("botId", botId)) };

    public static GleapBridgeMessage StartConversation(bool showBackButton) =>
        StartBot("", showBackButton);

    public static GleapBridgeMessage OpenConversations(bool showBackButton) =>
        new() { Name = "open-conversations", Data = Hide(showBackButton) };

    public static GleapBridgeMessage OpenConversation(string shareToken) =>
        new() { Name = "open-conversation", Data = new Dictionary<string, object> { ["shareToken"] = shareToken } };

    public static GleapBridgeMessage StartClassicForm(string formId, bool showBackButton) =>
        new() { Name = "start-feedbackflow", Data = Hide(showBackButton, ("flow", formId)) };

    public static GleapBridgeMessage OpenHelpCenter(bool showBackButton) =>
        new() { Name = "open-helpcenter", Data = Hide(showBackButton) };

    public static GleapBridgeMessage OpenHelpCenterArticle(string articleId, bool showBackButton) =>
        new() { Name = "open-help-article", Data = Hide(showBackButton, ("articleId", articleId)) };

    public static GleapBridgeMessage OpenHelpCenterCollection(string collectionId, bool showBackButton) =>
        new() { Name = "open-help-collection", Data = Hide(showBackButton, ("collectionId", collectionId)) };

    public static GleapBridgeMessage SearchHelpCenter(string term, bool showBackButton) =>
        new() { Name = "open-helpcenter-search", Data = Hide(showBackButton, ("term", term)) };

    public static GleapBridgeMessage OpenNews(bool showBackButton) =>
        new() { Name = "open-news", Data = Hide(showBackButton) };

    public static GleapBridgeMessage OpenNewsArticle(string articleId, bool showBackButton) =>
        new() { Name = "open-news-article", Data = Hide(showBackButton, ("id", articleId)) };

    public static GleapBridgeMessage OpenFeatureRequests(bool showBackButton) =>
        new() { Name = "open-feature-requests", Data = Hide(showBackButton) };

    public static GleapBridgeMessage OpenChecklists(bool showBackButton) =>
        new() { Name = "open-checklists", Data = Hide(showBackButton) };

    public static GleapBridgeMessage OpenChecklist(string checklistId, bool showBackButton) =>
        new() { Name = "open-checklist", Data = Hide(showBackButton, ("id", checklistId)) };

    public static GleapBridgeMessage StartChecklist(string outboundId, bool showBackButton) =>
        new() { Name = "start-checklist", Data = Hide(showBackButton, ("outboundId", outboundId)) };

    public static GleapBridgeMessage AskAI(string question, bool showBackButton) =>
        new() { Name = "ask-ai", Data = Hide(showBackButton, ("question", question)) };

    public static GleapBridgeMessage StartSurvey(string surveyId, SurveyFormat format)
    {
        var formatStr = format == SurveyFormat.SurveyFull ? "survey_full" : "survey";
        return new()
        {
            Name = "start-survey",
            Data = new Dictionary<string, object>
            {
                ["flow"] = surveyId,
                ["isSurvey"] = true,
                ["format"] = formatStr,
                ["hideBackButton"] = false
            }
        };
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter WidgetCommandsTests`
Expected: PASS (5 tests).

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "feat: add widget navigation command builders"
```

---

## Task 9: HTTP transport + ApiClient (sessions & config)

**Files:**
- Create: `CSharp-SDK/src/Gleap.Core/Http/GleapEndpoints.cs`
- Create: `CSharp-SDK/src/Gleap.Core/Http/HttpResult.cs`
- Create: `CSharp-SDK/src/Gleap.Core/Http/IHttpTransport.cs`
- Create: `CSharp-SDK/src/Gleap.Core/Http/HttpTransport.cs`
- Create: `CSharp-SDK/src/Gleap.Core/Http/SessionResult.cs`
- Create: `CSharp-SDK/src/Gleap.Core/Http/ApiClient.cs`
- Create: `CSharp-SDK/tests/Gleap.Core.Tests/Fakes/FakeHttpTransport.cs`
- Test: `CSharp-SDK/tests/Gleap.Core.Tests/ApiClientTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/Gleap.Core.Tests/Fakes/FakeHttpTransport.cs`:

```csharp
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GleapSDK.Http;

namespace Gleap.Core.Tests.Fakes;

public sealed class FakeHttpTransport : IHttpTransport
{
    public sealed record Call(string Method, string Url, string? Body, IReadOnlyDictionary<string, string> Headers);

    public List<Call> Calls { get; } = new();
    public Queue<HttpResult> Responses { get; } = new();

    public Task<HttpResult> SendAsync(
        string method, string url, string? jsonBody,
        IReadOnlyDictionary<string, string> headers, CancellationToken ct)
    {
        Calls.Add(new Call(method, url, jsonBody, headers));
        var result = Responses.Count > 0 ? Responses.Dequeue() : new HttpResult(200, "{}");
        return Task.FromResult(result);
    }
}
```

Create `tests/Gleap.Core.Tests/ApiClientTests.cs`:

```csharp
using System.Threading;
using System.Threading.Tasks;
using Gleap.Core.Tests.Fakes;
using GleapSDK.Http;
using GleapSDK.Serialization;
using Xunit;

namespace Gleap.Core.Tests;

public class ApiClientTests
{
    private static ApiClient NewClient(FakeHttpTransport t) =>
        new ApiClient(t, new SystemTextJsonSerializer(), GleapEndpoints.Default, "sdk-key-123");

    [Fact]
    public async Task CreateSession_PostsToSessions_WithApiTokenHeader()
    {
        var t = new FakeHttpTransport();
        t.Responses.Enqueue(new HttpResult(200, "{\"gleapId\":\"g1\",\"gleapHash\":\"h1\"}"));
        var client = NewClient(t);

        var res = await client.CreateSessionAsync("en", "desktop", null, null, CancellationToken.None);

        var call = t.Calls[0];
        Assert.Equal("POST", call.Method);
        Assert.Equal("https://api.gleap.io/sessions", call.Url);
        Assert.Equal("sdk-key-123", call.Headers["Api-Token"]);
        Assert.Equal("g1", res.GleapId);
        Assert.Equal("h1", res.GleapHash);
    }

    [Fact]
    public async Task CreateSession_SendsGuestIdsWhenPresent()
    {
        var t = new FakeHttpTransport();
        t.Responses.Enqueue(new HttpResult(200, "{\"gleapId\":\"g2\",\"gleapHash\":\"h2\"}"));
        var client = NewClient(t);

        await client.CreateSessionAsync("en", "desktop", "guestId", "guestHash", CancellationToken.None);

        var call = t.Calls[0];
        Assert.Equal("guestId", call.Headers["Gleap-Id"]);
        Assert.Equal("guestHash", call.Headers["Gleap-Hash"]);
    }

    [Fact]
    public async Task LoadConfig_GetsConfigEndpoint_WithLang()
    {
        var t = new FakeHttpTransport();
        t.Responses.Enqueue(new HttpResult(200, "{\"flowConfig\":{\"a\":1},\"projectActions\":{\"b\":2}}"));
        var client = NewClient(t);

        var raw = await client.LoadConfigAsync("de", CancellationToken.None);

        var call = t.Calls[0];
        Assert.Equal("GET", call.Method);
        Assert.Equal("https://api.gleap.io/config/sdk-key-123?lang=de", call.Url);
        Assert.Contains("flowConfig", raw);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter ApiClientTests`
Expected: FAIL — HTTP types / `ApiClient` do not exist.

- [ ] **Step 3: Write endpoints, result type & transport abstraction (one type per file)**

Create `src/Gleap.Core/Http/GleapEndpoints.cs`:

```csharp
namespace GleapSDK.Http;

/// <summary>Configurable Gleap service base URLs.</summary>
public sealed class GleapEndpoints
{
    public string ApiUrl { get; set; } = "https://api.gleap.io";
    public string WsUrl { get; set; } = "wss://ws.gleap.io";
    public string FrameUrl { get; set; } = "https://messenger-app.gleap.io/appnew";

    public static GleapEndpoints Default => new();
}
```

Create `src/Gleap.Core/Http/HttpResult.cs`:

```csharp
namespace GleapSDK.Http;

/// <summary>Immutable HTTP response value: status code + raw body.</summary>
public readonly struct HttpResult
{
    public int StatusCode { get; }
    public string Body { get; }
    public bool IsSuccess => StatusCode is >= 200 and < 300;

    public HttpResult(int statusCode, string body)
    {
        StatusCode = statusCode;
        Body = body;
    }
}
```

Create `src/Gleap.Core/Http/IHttpTransport.cs`:

```csharp
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace GleapSDK.Http;

/// <summary>Minimal HTTP seam so the API layer is testable without a real network.</summary>
public interface IHttpTransport
{
    Task<HttpResult> SendAsync(
        string method, string url, string? jsonBody,
        IReadOnlyDictionary<string, string> headers, CancellationToken ct);
}
```

- [ ] **Step 4: Write the real transport (thin HttpClient wrapper)**

Create `src/Gleap.Core/Http/HttpTransport.cs`:

```csharp
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GleapSDK.Http;

public sealed class HttpTransport : IHttpTransport
{
    private readonly HttpClient _http;

    public HttpTransport(HttpClient? http = null) => _http = http ?? new HttpClient();

    public async Task<HttpResult> SendAsync(
        string method, string url, string? jsonBody,
        IReadOnlyDictionary<string, string> headers, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(new HttpMethod(method), url);
        if (jsonBody != null)
            req.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        foreach (var kv in headers)
            req.Headers.TryAddWithoutValidation(kv.Key, kv.Value);

        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
        return new HttpResult((int)resp.StatusCode, body);
    }
}
```

- [ ] **Step 5: Write the SessionResult DTO and the ApiClient (one type per file)**

Create `src/Gleap.Core/Http/SessionResult.cs`:

```csharp
namespace GleapSDK.Http;

/// <summary>Parsed <c>POST /sessions</c> response.</summary>
public sealed class SessionResult
{
    public string GleapId { get; set; } = "";
    public string GleapHash { get; set; } = "";
}
```

Create `src/Gleap.Core/Http/ApiClient.cs`:

```csharp
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GleapSDK.Serialization;

namespace GleapSDK.Http;

/// <summary>Typed access to the Gleap REST endpoints used during bootstrap.</summary>
public sealed class ApiClient
{
    private readonly IHttpTransport _http;
    private readonly IJsonSerializer _json;
    private readonly GleapEndpoints _endpoints;
    private readonly string _sdkKey;

    public ApiClient(IHttpTransport http, IJsonSerializer json, GleapEndpoints endpoints, string sdkKey)
    {
        _http = http;
        _json = json;
        _endpoints = endpoints;
        _sdkKey = sdkKey;
    }

    private Dictionary<string, string> BaseHeaders(string? gleapId, string? gleapHash)
    {
        var h = new Dictionary<string, string> { ["Api-Token"] = _sdkKey };
        if (!string.IsNullOrEmpty(gleapId)) h["Gleap-Id"] = gleapId!;
        if (!string.IsNullOrEmpty(gleapHash)) h["Gleap-Hash"] = gleapHash!;
        return h;
    }

    public async Task<SessionResult> CreateSessionAsync(
        string lang, string deviceType, string? guestId, string? guestHash, CancellationToken ct)
    {
        var body = _json.Serialize(new Dictionary<string, object>
        {
            ["lang"] = lang,
            ["platform"] = "windows",   // TODO SP-0 Part 2: platform per runtime (see spec §8)
            ["deviceType"] = deviceType
        });
        var res = await _http.SendAsync("POST", _endpoints.ApiUrl + "/sessions", body,
            BaseHeaders(guestId, guestHash), ct).ConfigureAwait(false);

        using var doc = JsonDocument.Parse(res.Body);
        var root = doc.RootElement;
        return new SessionResult
        {
            GleapId = root.TryGetProperty("gleapId", out var i) ? i.GetString() ?? "" : "",
            GleapHash = root.TryGetProperty("gleapHash", out var hsh) ? hsh.GetString() ?? "" : ""
        };
    }

    /// <summary>Returns the raw config JSON body for ConfigManager to split.</summary>
    public async Task<string> LoadConfigAsync(string lang, CancellationToken ct)
    {
        var url = _endpoints.ApiUrl + "/config/" + _sdkKey + "?lang=" + lang;
        var res = await _http.SendAsync("GET", url, null, BaseHeaders(null, null), ct)
            .ConfigureAwait(false);
        return res.Body;
    }
}
```

> The `platform` literal is a known placeholder tracked in spec §8 (SDK-type registration). It is set per-runtime in SP-0 Part 2; hard-coding `"windows"` here is correct for the first desktop target and keeps this task self-contained.

- [ ] **Step 6: Run test to verify it passes**

Run: `dotnet test --filter ApiClientTests`
Expected: PASS (3 tests).

- [ ] **Step 7: Commit**

```bash
git add -A && git commit -m "feat: HTTP transport and ApiClient for sessions and config"
```

---

## Task 10: SessionManager + key-value store

**Files:**
- Create: `CSharp-SDK/src/Gleap.Core/Session/IKeyValueStore.cs`
- Create: `CSharp-SDK/src/Gleap.Core/Session/InMemoryKeyValueStore.cs`
- Create: `CSharp-SDK/src/Gleap.Core/Session/SessionManager.cs`
- Test: `CSharp-SDK/tests/Gleap.Core.Tests/SessionManagerTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/Gleap.Core.Tests/SessionManagerTests.cs`:

```csharp
using System.Threading;
using System.Threading.Tasks;
using Gleap.Core.Tests.Fakes;
using GleapSDK.Http;
using GleapSDK.Serialization;
using GleapSDK.Session;
using Xunit;

namespace Gleap.Core.Tests;

public class SessionManagerTests
{
    private static (SessionManager sm, InMemoryKeyValueStore store, FakeHttpTransport http) New()
    {
        var http = new FakeHttpTransport();
        var api = new ApiClient(http, new SystemTextJsonSerializer(), GleapEndpoints.Default, "key");
        var store = new InMemoryKeyValueStore();
        return (new SessionManager(api, store), store, http);
    }

    [Fact]
    public async Task Start_PersistsGleapIdAndHash()
    {
        var (sm, store, http) = New();
        http.Responses.Enqueue(new HttpResult(200, "{\"gleapId\":\"g1\",\"gleapHash\":\"h1\"}"));

        await sm.StartAsync("en", "desktop", CancellationToken.None);

        Assert.Equal("g1", sm.GleapId);
        Assert.Equal("h1", sm.GleapHash);
        Assert.Equal("g1", store.Get("gleapId"));
        Assert.Equal("h1", store.Get("gleapHash"));
    }

    [Fact]
    public async Task Start_ReusesStoredGuestIds()
    {
        var (sm, store, http) = New();
        store.Set("gleapId", "guest");
        store.Set("gleapHash", "guestHash");
        http.Responses.Enqueue(new HttpResult(200, "{\"gleapId\":\"g1\",\"gleapHash\":\"h1\"}"));

        await sm.StartAsync("en", "desktop", CancellationToken.None);

        Assert.Equal("guest", http.Calls[0].Headers["Gleap-Id"]);
    }

    [Fact]
    public async Task ClearIdentity_WipesStoredIds()
    {
        var (sm, store, http) = New();
        http.Responses.Enqueue(new HttpResult(200, "{\"gleapId\":\"g1\",\"gleapHash\":\"h1\"}"));
        await sm.StartAsync("en", "desktop", CancellationToken.None);

        sm.ClearIdentity();

        Assert.Null(store.Get("gleapId"));
        Assert.Equal("", sm.GleapId);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter SessionManagerTests`
Expected: FAIL — session types do not exist.

- [ ] **Step 3: Write the store abstraction + in-memory impl**

Create `src/Gleap.Core/Session/IKeyValueStore.cs`:

```csharp
namespace GleapSDK.Session;

/// <summary>Persistence for session ids. Platform packages back this with
/// NSUserDefaults / SharedPreferences / registry / file; Core uses in-memory in tests.</summary>
public interface IKeyValueStore
{
    string? Get(string key);
    void Set(string key, string value);
    void Remove(string key);
}
```

Create `src/Gleap.Core/Session/InMemoryKeyValueStore.cs`:

```csharp
using System.Collections.Generic;

namespace GleapSDK.Session;

public sealed class InMemoryKeyValueStore : IKeyValueStore
{
    private readonly Dictionary<string, string> _data = new();

    public string? Get(string key) => _data.TryGetValue(key, out var v) ? v : null;
    public void Set(string key, string value) => _data[key] = value;
    public void Remove(string key) => _data.Remove(key);
}
```

- [ ] **Step 4: Write the SessionManager**

Create `src/Gleap.Core/Session/SessionManager.cs`:

```csharp
using System.Threading;
using System.Threading.Tasks;
using GleapSDK.Http;

namespace GleapSDK.Session;

public sealed class SessionManager
{
    private const string KeyId = "gleapId";
    private const string KeyHash = "gleapHash";

    private readonly ApiClient _api;
    private readonly IKeyValueStore _store;

    public SessionManager(ApiClient api, IKeyValueStore store)
    {
        _api = api;
        _store = store;
    }

    public string GleapId { get; private set; } = "";
    public string GleapHash { get; private set; } = "";
    public bool HasSession => !string.IsNullOrEmpty(GleapId);

    public async Task StartAsync(string lang, string deviceType, CancellationToken ct)
    {
        var guestId = _store.Get(KeyId);
        var guestHash = _store.Get(KeyHash);

        var res = await _api.CreateSessionAsync(lang, deviceType, guestId, guestHash, ct)
            .ConfigureAwait(false);

        GleapId = res.GleapId;
        GleapHash = res.GleapHash;
        _store.Set(KeyId, GleapId);
        _store.Set(KeyHash, GleapHash);
    }

    public void ClearIdentity()
    {
        GleapId = "";
        GleapHash = "";
        _store.Remove(KeyId);
        _store.Remove(KeyHash);
    }
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test --filter SessionManagerTests`
Expected: PASS (3 tests).

- [ ] **Step 6: Commit**

```bash
git add -A && git commit -m "feat: SessionManager with pluggable key-value store"
```

---

## Task 11: ConfigManager

**Files:**
- Create: `CSharp-SDK/src/Gleap.Core/Session/ConfigManager.cs`
- Test: `CSharp-SDK/tests/Gleap.Core.Tests/ConfigManagerTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/Gleap.Core.Tests/ConfigManagerTests.cs`:

```csharp
using System.Threading;
using System.Threading.Tasks;
using Gleap.Core.Tests.Fakes;
using GleapSDK.Http;
using GleapSDK.Serialization;
using GleapSDK.Session;
using Xunit;

namespace Gleap.Core.Tests;

public class ConfigManagerTests
{
    [Fact]
    public async Task Load_SplitsFlowConfigAndProjectActions()
    {
        var http = new FakeHttpTransport();
        http.Responses.Enqueue(new HttpResult(200,
            "{\"flowConfig\":{\"color\":\"#fff\"},\"projectActions\":{\"x\":1}}"));
        var api = new ApiClient(http, new SystemTextJsonSerializer(), GleapEndpoints.Default, "key");
        var cfg = new ConfigManager(api);

        await cfg.LoadAsync("en", CancellationToken.None);

        Assert.True(cfg.IsLoaded);
        Assert.Contains("color", cfg.FlowConfigJson);
        Assert.Contains("\"x\":1", cfg.ProjectActionsJson);
    }

    [Fact]
    public async Task Load_HandlesMissingProjectActions()
    {
        var http = new FakeHttpTransport();
        http.Responses.Enqueue(new HttpResult(200, "{\"flowConfig\":{\"color\":\"#fff\"}}"));
        var api = new ApiClient(http, new SystemTextJsonSerializer(), GleapEndpoints.Default, "key");
        var cfg = new ConfigManager(api);

        await cfg.LoadAsync("en", CancellationToken.None);

        Assert.Equal("{}", cfg.ProjectActionsJson);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter ConfigManagerTests`
Expected: FAIL — `ConfigManager` does not exist.

- [ ] **Step 3: Write the ConfigManager**

Create `src/Gleap.Core/Session/ConfigManager.cs`:

```csharp
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GleapSDK.Http;

namespace GleapSDK.Session;

/// <summary>
/// Loads project config and splits it into flowConfig (widget config) and
/// projectActions, kept as raw JSON to forward verbatim in the "config-update" message.
/// </summary>
public sealed class ConfigManager
{
    private readonly ApiClient _api;

    public ConfigManager(ApiClient api) => _api = api;

    public bool IsLoaded { get; private set; }
    public string FlowConfigJson { get; private set; } = "{}";
    public string ProjectActionsJson { get; private set; } = "{}";

    public async Task LoadAsync(string lang, CancellationToken ct)
    {
        var raw = await _api.LoadConfigAsync(lang, ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(raw);
        var root = doc.RootElement;

        FlowConfigJson = root.TryGetProperty("flowConfig", out var f) ? f.GetRawText() : "{}";
        ProjectActionsJson = root.TryGetProperty("projectActions", out var a) ? a.GetRawText() : "{}";
        IsLoaded = true;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter ConfigManagerTests`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "feat: ConfigManager splits flowConfig and projectActions"
```

---

## Task 12: WidgetBootstrapper — ping → bootstrap sequence → flush

**Files:**
- Create: `CSharp-SDK/src/Gleap.Core/Session/SessionSnapshot.cs`
- Create: `CSharp-SDK/src/Gleap.Core/Session/WidgetBootstrapper.cs`
- Test: `CSharp-SDK/tests/Gleap.Core.Tests/WidgetBootstrapperTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/Gleap.Core.Tests/WidgetBootstrapperTests.cs`:

```csharp
using System.Linq;
using Gleap.Core.Tests.Fakes;
using GleapSDK.Bridge;
using GleapSDK.Serialization;
using GleapSDK.Session;
using Xunit;

namespace Gleap.Core.Tests;

public class WidgetBootstrapperTests
{
    [Fact]
    public void OnPing_PushesBootstrapSequence_InOrder()
    {
        var ch = new FakeWebViewChannel();
        var bridge = new WebViewBridge(ch, new SystemTextJsonSerializer());
        var state = new SessionSnapshot
        {
            SdkKey = "key", ApiUrl = "https://api.gleap.io",
            GleapId = "g1", GleapHash = "h1",
            FlowConfigJson = "{\"color\":\"#fff\"}", ProjectActionsJson = "{\"x\":1}",
            Language = "en"
        };
        _ = new WidgetBootstrapper(bridge, () => state);

        ch.SimulateIncoming("{\"name\":\"ping\"}");

        var names = ch.ExecutedScripts;
        int Idx(string n) => names.FindIndex(s => s.Contains("\"name\":\"" + n + "\""));
        Assert.True(Idx("widget-status-update") >= 0);
        Assert.True(Idx("config-update") > Idx("widget-status-update"));
        Assert.True(Idx("session-update") > Idx("config-update"));
    }

    [Fact]
    public void SessionUpdate_CarriesSessionDataAndSdkKey()
    {
        var ch = new FakeWebViewChannel();
        var bridge = new WebViewBridge(ch, new SystemTextJsonSerializer());
        var state = new SessionSnapshot
        {
            SdkKey = "key-xyz", ApiUrl = "https://api.gleap.io",
            GleapId = "gid", GleapHash = "gh",
            FlowConfigJson = "{}", ProjectActionsJson = "{}", Language = "en"
        };
        _ = new WidgetBootstrapper(bridge, () => state);

        ch.SimulateIncoming("{\"name\":\"ping\"}");

        var sessionMsg = ch.ExecutedScripts.First(s => s.Contains("session-update"));
        Assert.Contains("\"sdkKey\":\"key-xyz\"", sessionMsg);
        Assert.Contains("\"gleapId\":\"gid\"", sessionMsg);
    }

    [Fact]
    public void QueuedNavCommand_FlushesAfterBootstrap()
    {
        var ch = new FakeWebViewChannel();
        var bridge = new WebViewBridge(ch, new SystemTextJsonSerializer());
        var state = new SessionSnapshot
        {
            SdkKey = "key", ApiUrl = "https://api.gleap.io", GleapId = "g", GleapHash = "h",
            FlowConfigJson = "{}", ProjectActionsJson = "{}", Language = "en"
        };
        _ = new WidgetBootstrapper(bridge, () => state);
        bridge.Send(WidgetCommands.OpenNews(showBackButton: true)); // queued pre-ping

        ch.SimulateIncoming("{\"name\":\"ping\"}");

        int cfg = ch.ExecutedScripts.FindIndex(s => s.Contains("config-update"));
        int news = ch.ExecutedScripts.FindIndex(s => s.Contains("open-news"));
        Assert.True(news > cfg, "nav command must flush after bootstrap");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter WidgetBootstrapperTests`
Expected: FAIL — `WidgetBootstrapper` / `SessionSnapshot` do not exist.

- [ ] **Step 3: Write the snapshot type**

Create `src/Gleap.Core/Session/SessionSnapshot.cs`:

```csharp
using System.Collections.Generic;

namespace GleapSDK.Session;

/// <summary>Immutable view of everything the bootstrap sequence needs.</summary>
public sealed class SessionSnapshot
{
    public string SdkKey { get; set; } = "";
    public string ApiUrl { get; set; } = "";
    public string GleapId { get; set; } = "";
    public string GleapHash { get; set; } = "";
    public string FlowConfigJson { get; set; } = "{}";
    public string ProjectActionsJson { get; set; } = "{}";
    public string Language { get; set; } = "en";
    public string? UserId { get; set; }
    public string? Name { get; set; }
    public string? Email { get; set; }

    public IReadOnlyDictionary<string, object?>? PreFillFormData { get; set; }
}
```

- [ ] **Step 4: Write the bootstrapper**

Create `src/Gleap.Core/Session/WidgetBootstrapper.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Text.Json;
using GleapSDK.Bridge;

namespace GleapSDK.Session;

/// <summary>
/// Subscribes to the bridge's ping handshake and pushes the mandatory bootstrap
/// sequence (widget-status-update -> config-update -> session-update ->
/// prefill-form-data), before the bridge flushes queued navigation commands.
/// </summary>
public sealed class WidgetBootstrapper
{
    private readonly WebViewBridge _bridge;
    private readonly Func<SessionSnapshot> _snapshot;

    public WidgetBootstrapper(WebViewBridge bridge, Func<SessionSnapshot> snapshot)
    {
        _bridge = bridge;
        _snapshot = snapshot;
        _bridge.PingReceived += OnPing;
    }

    private void OnPing()
    {
        var s = _snapshot();

        _bridge.Send(new GleapBridgeMessage
        {
            Name = "widget-status-update",
            Data = new Dictionary<string, object> { ["isWidgetOpen"] = true }
        });

        _bridge.Send(new GleapBridgeMessage
        {
            Name = "config-update",
            Data = new Dictionary<string, object>
            {
                ["config"] = RawJson(s.FlowConfigJson),
                ["actions"] = RawJson(s.ProjectActionsJson),
                ["overrideLanguage"] = s.Language,
                ["isApp"] = true
            }
        });

        _bridge.Send(new GleapBridgeMessage
        {
            Name = "session-update",
            Data = new Dictionary<string, object?>
            {
                ["sessionData"] = new Dictionary<string, object?>
                {
                    ["gleapId"] = s.GleapId,
                    ["gleapHash"] = s.GleapHash,
                    ["userId"] = s.UserId,
                    ["name"] = s.Name,
                    ["email"] = s.Email
                },
                ["apiUrl"] = s.ApiUrl,
                ["sdkKey"] = s.SdkKey
            }
        });

        if (s.PreFillFormData != null)
        {
            _bridge.Send(new GleapBridgeMessage
            {
                Name = "prefill-form-data",
                Data = s.PreFillFormData
            });
        }
    }

    /// <summary>Wrap already-serialized JSON so the serializer emits it verbatim.</summary>
    private static JsonElement RawJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }
}
```

> `RawJson` returns a cloned `JsonElement`; `System.Text.Json` serializes a `JsonElement` back to its literal JSON, so the server's `flowConfig`/`projectActions` are forwarded unchanged inside `config-update`.

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test --filter WidgetBootstrapperTests`
Expected: PASS (3 tests).

- [ ] **Step 6: Commit**

```bash
git add -A && git commit -m "feat: WidgetBootstrapper pushes bootstrap sequence on ping"
```

---

## Task 13: Backend contract, ManagedBackend & Gleap facade

**Files:**
- Create: `CSharp-SDK/src/Gleap.Core/IGleapBackend.cs`
- Create: `CSharp-SDK/src/Gleap.Core/ManagedBackend.cs`
- Create: `CSharp-SDK/src/Gleap.Core/Gleap.cs`
- Test: `CSharp-SDK/tests/Gleap.Core.Tests/ManagedBackendTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/Gleap.Core.Tests/ManagedBackendTests.cs`:

```csharp
using System.Threading;
using System.Threading.Tasks;
using Gleap.Core.Tests.Fakes;
using GleapSDK;
using GleapSDK.Http;
using GleapSDK.Serialization;
using GleapSDK.Session;
using Xunit;

namespace Gleap.Core.Tests;

public class ManagedBackendTests
{
    private static (ManagedBackend backend, FakeWebViewChannel ch, FakeHttpTransport http) New()
    {
        var http = new FakeHttpTransport();
        var ch = new FakeWebViewChannel();
        var deps = new ManagedBackend.Dependencies
        {
            Http = http,
            Json = new SystemTextJsonSerializer(),
            Store = new InMemoryKeyValueStore(),
            Channel = ch,
            Endpoints = GleapEndpoints.Default
        };
        return (new ManagedBackend(deps), ch, http);
    }

    [Fact]
    public async Task Initialize_CreatesSessionAndLoadsConfig()
    {
        var (backend, ch, http) = New();
        http.Responses.Enqueue(new HttpResult(200, "{\"gleapId\":\"g1\",\"gleapHash\":\"h1\"}")); // sessions
        http.Responses.Enqueue(new HttpResult(200, "{\"flowConfig\":{},\"projectActions\":{}}")); // config

        await backend.InitializeAsync("token-1", CancellationToken.None);

        Assert.Equal("https://api.gleap.io/sessions", http.Calls[0].Url);
        Assert.StartsWith("https://api.gleap.io/config/token-1", http.Calls[1].Url);
    }

    [Fact]
    public async Task Open_AfterPing_SendsBootstrapThenNoCommand()
    {
        var (backend, ch, http) = New();
        http.Responses.Enqueue(new HttpResult(200, "{\"gleapId\":\"g1\",\"gleapHash\":\"h1\"}"));
        http.Responses.Enqueue(new HttpResult(200, "{\"flowConfig\":{},\"projectActions\":{}}"));
        await backend.InitializeAsync("token-1", CancellationToken.None);

        backend.Open();                               // queued (not connected yet)
        ch.SimulateIncoming("{\"name\":\"ping\"}");   // handshake

        Assert.Contains(ch.ExecutedScripts, s => s.Contains("session-update"));
    }

    [Fact]
    public async Task StartBot_QueuesCommand_FlushedAfterPing()
    {
        var (backend, ch, http) = New();
        http.Responses.Enqueue(new HttpResult(200, "{\"gleapId\":\"g1\",\"gleapHash\":\"h1\"}"));
        http.Responses.Enqueue(new HttpResult(200, "{\"flowConfig\":{},\"projectActions\":{}}"));
        await backend.InitializeAsync("token-1", CancellationToken.None);

        backend.StartBot("bot42", showBackButton: false);
        ch.SimulateIncoming("{\"name\":\"ping\"}");

        Assert.Contains(ch.ExecutedScripts, s => s.Contains("\"botId\":\"bot42\""));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter ManagedBackendTests`
Expected: FAIL — backend types do not exist.

- [ ] **Step 3: Write the backend contract**

Create `src/Gleap.Core/IGleapBackend.cs`:

```csharp
using System.Threading;
using System.Threading.Tasks;

namespace GleapSDK;

/// <summary>
/// The swappable engine behind the <see cref="Gleap"/> facade.
/// SP-0 provides <c>ManagedBackend</c>; platform packages later add native-bridge backends.
/// Only the surface needed for SP-0 Part 1 is declared; it grows in later plans.
/// </summary>
public interface IGleapBackend
{
    Task InitializeAsync(string token, CancellationToken ct);
    void Open();
    void Close();
    void StartConversation(bool showBackButton);
    void StartBot(string botId, bool showBackButton);
    void OpenConversation(string shareToken);
    void OpenHelpCenter(bool showBackButton);
    void OpenNews(bool showBackButton);
    void ShowSurvey(string surveyId, SurveyFormat format);
}
```

- [ ] **Step 4: Write the ManagedBackend**

Create `src/Gleap.Core/ManagedBackend.cs`:

```csharp
using System.Threading;
using System.Threading.Tasks;
using GleapSDK.Bridge;
using GleapSDK.Http;
using GleapSDK.Serialization;
using GleapSDK.Session;

namespace GleapSDK;

/// <summary>Pure-C# backend used where no native SDK exists (desktop).</summary>
public sealed class ManagedBackend : IGleapBackend
{
    public sealed class Dependencies
    {
        public IHttpTransport Http { get; set; } = new HttpTransport();
        public IJsonSerializer Json { get; set; } = new SystemTextJsonSerializer();
        public IKeyValueStore Store { get; set; } = new InMemoryKeyValueStore();
        public IWebViewChannel Channel { get; set; } = null!;
        public GleapEndpoints Endpoints { get; set; } = GleapEndpoints.Default;
    }

    private readonly Dependencies _d;
    private WebViewBridge _bridge = null!;
    private SessionManager _session = null!;
    private ConfigManager _config = null!;
    private string _token = "";

    public ManagedBackend(Dependencies dependencies) => _d = dependencies;

    public async Task InitializeAsync(string token, CancellationToken ct)
    {
        _token = token;
        var api = new ApiClient(_d.Http, _d.Json, _d.Endpoints, token);
        _session = new SessionManager(api, _d.Store);
        _config = new ConfigManager(api);

        _bridge = new WebViewBridge(_d.Channel, _d.Json);
        _ = new WidgetBootstrapper(_bridge, BuildSnapshot);

        await _session.StartAsync("en", "desktop", ct).ConfigureAwait(false);
        await _config.LoadAsync("en", ct).ConfigureAwait(false);
    }

    private SessionSnapshot BuildSnapshot() => new()
    {
        SdkKey = _token,
        ApiUrl = _d.Endpoints.ApiUrl,
        GleapId = _session.GleapId,
        GleapHash = _session.GleapHash,
        FlowConfigJson = _config.FlowConfigJson,
        ProjectActionsJson = _config.ProjectActionsJson,
        Language = "en"
    };

    public void Open() => _bridge.Send(new GleapBridgeMessage
    {
        Name = "widget-status-update",
        Data = new System.Collections.Generic.Dictionary<string, object> { ["isWidgetOpen"] = true }
    });

    public void Close() => _bridge.Send(new GleapBridgeMessage
    {
        Name = "widget-status-update",
        Data = new System.Collections.Generic.Dictionary<string, object> { ["isWidgetOpen"] = false }
    });

    public void StartConversation(bool showBackButton) => _bridge.Send(WidgetCommands.StartConversation(showBackButton));
    public void StartBot(string botId, bool showBackButton) => _bridge.Send(WidgetCommands.StartBot(botId, showBackButton));
    public void OpenConversation(string shareToken) => _bridge.Send(WidgetCommands.OpenConversation(shareToken));
    public void OpenHelpCenter(bool showBackButton) => _bridge.Send(WidgetCommands.OpenHelpCenter(showBackButton));
    public void OpenNews(bool showBackButton) => _bridge.Send(WidgetCommands.OpenNews(showBackButton));
    public void ShowSurvey(string surveyId, SurveyFormat format) => _bridge.Send(WidgetCommands.StartSurvey(surveyId, format));
}
```

- [ ] **Step 5: Write the public facade**

Create `src/Gleap.Core/Gleap.cs`:

```csharp
using System.Threading;
using System.Threading.Tasks;

namespace GleapSDK;

/// <summary>
/// Public entry point. Platform packages create the backend (managed or native-bridge)
/// and attach it via <see cref="UseBackend"/>; app code then calls the static API.
/// </summary>
public static class Gleap
{
    private static IGleapBackend? _backend;

    private static IGleapBackend Backend =>
        _backend ?? throw new System.InvalidOperationException(
            "Gleap backend not attached. A platform package must call Gleap.UseBackend(...).");

    /// <summary>Attach the platform backend. Called by platform packages, not app code.</summary>
    public static void UseBackend(IGleapBackend backend) => _backend = backend;

    public static Task InitializeAsync(string token, CancellationToken ct = default) =>
        Backend.InitializeAsync(token, ct);

    public static void Open() => Backend.Open();
    public static void Close() => Backend.Close();
    public static void StartConversation(bool showBackButton = true) => Backend.StartConversation(showBackButton);
    public static void StartBot(string botId, bool showBackButton = true) => Backend.StartBot(botId, showBackButton);
    public static void OpenConversation(string shareToken) => Backend.OpenConversation(shareToken);
    public static void OpenHelpCenter(bool showBackButton = true) => Backend.OpenHelpCenter(showBackButton);
    public static void OpenNews(bool showBackButton = true) => Backend.OpenNews(showBackButton);
    public static void ShowSurvey(string surveyId, SurveyFormat format = SurveyFormat.Survey) => Backend.ShowSurvey(surveyId, format);

    /// <summary>Test-only reset so xUnit cases don't leak backend state.</summary>
    internal static void ResetForTest() => _backend = null;
}
```

- [ ] **Step 6: Run test to verify it passes**

Run: `dotnet test --filter ManagedBackendTests`
Expected: PASS (3 tests).

- [ ] **Step 7: Run the FULL suite**

Run: `cd /Users/tobiasduelli/development/projects/gleap/CSharp-SDK && dotnet test`
Expected: all tests green (Tasks 1–13).

- [ ] **Step 8: Commit**

```bash
git add -A && git commit -m "feat: IGleapBackend, ManagedBackend and public Gleap facade"
```

---

## Task 14: Quality gate (formatting + warnings-as-errors)

**Files:** none (verification + fixes only).

- [ ] **Step 1: Verify formatting matches `.editorconfig`**

Run: `cd /Users/tobiasduelli/development/projects/gleap/CSharp-SDK && dotnet format --verify-no-changes`
Expected: exit code 0, no files reported. If it fails, run `dotnet format` (no flag) to auto-fix, then re-run `--verify-no-changes`.

- [ ] **Step 2: Build with warnings as errors**

Run: `dotnet build -warnaserror`
Expected: build succeeds with 0 warnings. Fix any analyzer/style warnings surfaced (naming, unused usings, readonly fields, etc.) — do not suppress them except the pre-agreed `CS1591`.

- [ ] **Step 3: Full suite once more**

Run: `dotnet test`
Expected: all tests green.

- [ ] **Step 4: Commit**

```bash
git add -A && git commit -m "chore: pass formatting and warnings-as-errors quality gate"
```

---

## Definition of Done (SP-0 Part 1)

- `dotnet test` is green across all tasks.
- `Gleap.Core` builds for `netstandard2.0`.
- **Quality gate passes: `dotnet format --verify-no-changes` clean and `dotnet build -warnaserror` with 0 warnings** (code-quality bar is machine-enforced, not by taste).
- One public type per file; folder == namespace; shared build config in `Directory.Build.props`; style enforced by `.editorconfig`.
- A headless integration path works end-to-end with fakes: `ManagedBackend.InitializeAsync` → session created + config loaded → `ping` → bootstrap sequence pushed (`widget-status-update`, `config-update`, `session-update`) → queued navigation commands flushed in the correct order.
- No real WebView, no platform code, no network — all behind `IWebViewChannel` / `IHttpTransport` / `IKeyValueStore`.

## Follow-up (next plans)

- **SP-0 Part 2:** console/network/event log buffers (+1 MB truncation, blacklist/propsToIgnore), `/bugs/v2` feedback + silent-crash assembly, `/sessions/ping` polling + outbound (banner/modal/survey) dispatch, replay scheduling, custom-data/ticket-attribute/tag/attachment buffers, AI-tools execution, metadata provider interface, per-runtime `platform`/`sdkType` values.
- **SP-1:** Windows Desktop — implement `IWebViewChannel` over WebView2, real `IKeyValueStore`, metadata provider; wire `Gleap.UseBackend(new ManagedBackend(...))`; verify against the live `messenger-app.gleap.io/appnew`.
```
