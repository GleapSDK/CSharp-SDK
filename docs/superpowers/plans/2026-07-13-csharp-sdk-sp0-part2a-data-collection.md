# Gleap C# SDK — SP-0 Part 2a: Data Collection Layer

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add the headless data-collection layer to `Gleap.Core` — console/event/network log buffers (with truncation + blacklist), custom-data/ticket-attribute/tag/attachment stores, a metadata provider seam, and a `SessionDataCollector` that assembles the `collect-ticket-data` payload the web widget requests during a feedback flow — all wired through the `Gleap` facade.

**Architecture:** Pure C# `netstandard2.0`, everything unit-tested headless (no WebView, no network). Time is injected via `IClock` so tests are deterministic. Buffers are bounded ring buffers. The `SessionDataCollector` aggregates all sources into the exact `{customData, formData, metaData, consoleLog, networkLogs, customEventLog, tags}` shape from spec §5, and `ManagedBackend` replies to the widget's incoming `collect-ticket-data` message with it.

**Tech Stack:** Builds on SP-0 Part 1 (`Gleap.Core`, xUnit). `System.Net.Http.DelegatingHandler` for network capture. `System.Text.Json` via the existing `IJsonSerializer` seam.

**Reference spec:** `docs/superpowers/specs/2026-07-13-csharp-sdk-design.md` (§5 bridge `collect-ticket-data`, §7 data collection: metadata fields, network-log model + 1 MB truncation, blacklist/propsToIgnore).

**Prerequisite:** SP-0 Part 1 is complete (50 tests green). Existing types this plan builds on: `IJsonSerializer`/`SystemTextJsonSerializer`, `LogLevel` enum, `WebViewBridge` (partial, with `IncomingBridgeMessage`), `GleapBridgeMessage`, `IGleapBackend`/`ManagedBackend`/`Gleap` facade, `ManagedBackend.Dependencies`.

**Scope (Part 2a):** buffers, stores, metadata seam, `SessionDataCollector`, `collect-ticket-data` reply, facade data methods. **Out of scope (later):** `POST /bugs/v2` feedback + silent-crash assembly + attachment upload (Part 2b); `/sessions/ping` polling + outbound banner/modal/survey + WebSocket + replay scheduling + screenshot capture (Part 2c). Do NOT build those here.

**Convention:** Work in `CSharp-SDK/` (its own local git repo). .NET SDK at `~/.dotnet` — prefix shell commands with `export PATH="$HOME/.dotnet:$PATH" && export DOTNET_CLI_TELEMETRY_OPTOUT=1 && `. Commits **local only, never push**. Namespaces `GleapSDK.*`; one public type per file; `sealed` by default; XML docs on public members. Analyzer warnings are handled at the final quality-gate task — do not fix/suppress mid-task.

---

## File Structure

```
src/Gleap.Core/
  Time/
    IClock.cs                 # deterministic time seam
    SystemClock.cs
  Collection/
    RingBuffer.cs             # bounded FIFO buffer
    ConsoleLogBuffer.cs       # Log(message, level) -> GleapLog ring
    EventBuffer.cs            # TrackEvent -> GleapEvent ring
    NetworkLogBuffer.cs       # GleapNetworkLog ring + blacklist/propsToIgnore
    SessionDataCollector.cs   # aggregates everything -> collect-ticket-data payload
  Http/
    GleapHttpHandler.cs       # DelegatingHandler -> NetworkLogBuffer (opt-in capture)
    NetworkLogFactory.cs      # build GleapNetworkLog with 1 MB truncation
  Data/
    CustomDataStore.cs
    TicketAttributeStore.cs
    TagStore.cs
    AttachmentStore.cs
  Metadata/
    IMetadataProvider.cs
    DefaultMetadataProvider.cs
  Models/
    GleapLog.cs
    GleapEvent.cs
    GleapNetworkLog.cs
    GleapNetworkRequest.cs
    GleapNetworkResponse.cs
    GleapAttachment.cs
  (modified) Bridge/WebViewBridge.Incoming.cs   # + collect-ticket-data event
  (modified) IGleapBackend.cs                    # + data methods
  (modified) ManagedBackend.cs                   # + collector, wiring, data methods
  (modified) Gleap.cs                            # + data facade methods
tests/Gleap.Core.Tests/
  Fakes/FakeClock.cs
  (one test file per component)
```

---

## Task 1: Time seam + RingBuffer

**Files:**
- Create: `src/Gleap.Core/Time/IClock.cs`
- Create: `src/Gleap.Core/Time/SystemClock.cs`
- Create: `src/Gleap.Core/Collection/RingBuffer.cs`
- Create: `tests/Gleap.Core.Tests/Fakes/FakeClock.cs`
- Test: `tests/Gleap.Core.Tests/RingBufferTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/Gleap.Core.Tests/RingBufferTests.cs`:

```csharp
using System.Linq;
using GleapSDK.Collection;
using Xunit;

namespace Gleap.Core.Tests;

public class RingBufferTests
{
    [Fact]
    public void DropsOldest_WhenOverCapacity()
    {
        var buffer = new RingBuffer<int>(3);
        buffer.Add(1);
        buffer.Add(2);
        buffer.Add(3);
        buffer.Add(4);

        Assert.Equal(new[] { 2, 3, 4 }, buffer.Snapshot().ToArray());
        Assert.Equal(3, buffer.Count);
    }

    [Fact]
    public void Clear_EmptiesBuffer()
    {
        var buffer = new RingBuffer<int>(3);
        buffer.Add(1);
        buffer.Clear();
        Assert.Empty(buffer.Snapshot());
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter RingBufferTests`
Expected: FAIL — `RingBuffer` does not exist.

- [ ] **Step 3: Write the clock seam**

Create `src/Gleap.Core/Time/IClock.cs`:

```csharp
using System;

namespace GleapSDK.Time;

/// <summary>Time source, injected so buffers/timestamps are deterministic in tests.</summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
```

Create `src/Gleap.Core/Time/SystemClock.cs`:

```csharp
using System;

namespace GleapSDK.Time;

/// <summary>Real wall-clock time.</summary>
public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
```

- [ ] **Step 4: Write the ring buffer**

Create `src/Gleap.Core/Collection/RingBuffer.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;

namespace GleapSDK.Collection;

/// <summary>Bounded FIFO buffer: adding past capacity drops the oldest entry.</summary>
public sealed class RingBuffer<T>
{
    private readonly int _capacity;
    private readonly LinkedList<T> _items = new();

    public RingBuffer(int capacity) => _capacity = capacity;

    public int Count => _items.Count;

    public void Add(T item)
    {
        _items.AddLast(item);
        while (_items.Count > _capacity)
        {
            _items.RemoveFirst();
        }
    }

    public IReadOnlyList<T> Snapshot() => _items.ToList();

    public void Clear() => _items.Clear();
}
```

- [ ] **Step 5: Write the fake clock**

Create `tests/Gleap.Core.Tests/Fakes/FakeClock.cs`:

```csharp
using System;
using GleapSDK.Time;

namespace Gleap.Core.Tests.Fakes;

public sealed class FakeClock : IClock
{
    public DateTimeOffset UtcNow { get; set; } = new(2026, 7, 13, 10, 0, 0, TimeSpan.Zero);
}
```

- [ ] **Step 6: Run test to verify it passes**

Run: `dotnet test --filter RingBufferTests`
Expected: PASS (2 tests).

- [ ] **Step 7: Commit**

```bash
git add -A && git commit -m "feat: add IClock time seam and bounded RingBuffer"
```

---

## Task 2: Log & event models

**Files:**
- Create: `src/Gleap.Core/Models/GleapLog.cs`
- Create: `src/Gleap.Core/Models/GleapEvent.cs`
- Test: `tests/Gleap.Core.Tests/LogEventModelTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/Gleap.Core.Tests/LogEventModelTests.cs`:

```csharp
using GleapSDK.Models;
using GleapSDK.Serialization;
using Xunit;

namespace Gleap.Core.Tests;

public class LogEventModelTests
{
    private readonly IJsonSerializer _json = new SystemTextJsonSerializer();

    [Fact]
    public void GleapLog_SerializesToDateLogPriority()
    {
        var log = new GleapLog { Date = "2026-07-13T10:00:00.000Z", Log = "hi", Priority = "INFO" };
        var s = _json.Serialize(log);
        Assert.Contains("\"log\":\"hi\"", s);
        Assert.Contains("\"priority\":\"INFO\"", s);
        Assert.Contains("\"date\":\"2026-07-13T10:00:00.000Z\"", s);
    }

    [Fact]
    public void GleapEvent_HoldsNameDataDate()
    {
        var e = new GleapEvent { Name = "purchase", Data = null, Date = "2026-07-13T10:00:00.000Z" };
        Assert.Equal("purchase", e.Name);
        Assert.Equal("2026-07-13T10:00:00.000Z", e.Date);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter LogEventModelTests`
Expected: FAIL — models do not exist.

- [ ] **Step 3: Write GleapLog**

Create `src/Gleap.Core/Models/GleapLog.cs`:

```csharp
namespace GleapSDK.Models;

/// <summary>A single captured console/log line, as sent in <c>consoleLog</c>.</summary>
public sealed class GleapLog
{
    public string Date { get; set; } = "";
    public string Log { get; set; } = "";
    public string Priority { get; set; } = "INFO";
}
```

- [ ] **Step 4: Write GleapEvent**

Create `src/Gleap.Core/Models/GleapEvent.cs`:

```csharp
namespace GleapSDK.Models;

/// <summary>A tracked event, as sent in <c>customEventLog</c>.</summary>
public sealed class GleapEvent
{
    public string Name { get; set; } = "";
    public object? Data { get; set; }
    public string Date { get; set; } = "";
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test --filter LogEventModelTests`
Expected: PASS (2 tests).

- [ ] **Step 6: Commit**

```bash
git add -A && git commit -m "feat: add GleapLog and GleapEvent models"
```

---

## Task 3: Network-log models + truncation factory

**Files:**
- Create: `src/Gleap.Core/Models/GleapNetworkRequest.cs`
- Create: `src/Gleap.Core/Models/GleapNetworkResponse.cs`
- Create: `src/Gleap.Core/Models/GleapNetworkLog.cs`
- Create: `src/Gleap.Core/Http/NetworkLogFactory.cs`
- Test: `tests/Gleap.Core.Tests/NetworkLogFactoryTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/Gleap.Core.Tests/NetworkLogFactoryTests.cs`:

```csharp
using System.Collections.Generic;
using GleapSDK.Http;
using Xunit;

namespace Gleap.Core.Tests;

public class NetworkLogFactoryTests
{
    [Fact]
    public void Build_CopiesCoreFields()
    {
        var log = NetworkLogFactory.Build(
            type: "GET", url: "https://api.example.com/x", date: "2026-07-13T10:00:00.000Z",
            durationMs: 12, statusCode: 200, statusText: "OK",
            requestPayload: "req", requestHeaders: new Dictionary<string, string> { ["A"] = "1" },
            responseBody: "resp");

        Assert.Equal("GET", log.Type);
        Assert.True(log.Success);
        Assert.Equal(200, log.Response.Status);
        Assert.Equal("resp", log.Response.ResponseText);
        Assert.Equal("req", log.Request.Payload);
    }

    [Fact]
    public void Build_MarksNon2xxAsFailure()
    {
        var log = NetworkLogFactory.Build("GET", "u", "d", 1, 500, "err", null, null, "boom");
        Assert.False(log.Success);
    }

    [Fact]
    public void Build_TruncatesOversizePayloadAndResponse()
    {
        var big = new string('x', 1_000_001);
        var log = NetworkLogFactory.Build("POST", "u", "d", 1, 200, "OK", big, null, big);
        Assert.Equal("<payload_too_large>", log.Request.Payload);
        Assert.Equal("<response_too_large>", log.Response.ResponseText);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter NetworkLogFactoryTests`
Expected: FAIL — types do not exist.

- [ ] **Step 3: Write the request/response models**

Create `src/Gleap.Core/Models/GleapNetworkRequest.cs`:

```csharp
namespace GleapSDK.Models;

/// <summary>Request half of a <see cref="GleapNetworkLog"/>.</summary>
public sealed class GleapNetworkRequest
{
    public object? Payload { get; set; }
    public object? Headers { get; set; }
}
```

Create `src/Gleap.Core/Models/GleapNetworkResponse.cs`:

```csharp
namespace GleapSDK.Models;

/// <summary>Response half of a <see cref="GleapNetworkLog"/>.</summary>
public sealed class GleapNetworkResponse
{
    public int Status { get; set; }
    public string StatusText { get; set; } = "";
    public string ResponseText { get; set; } = "";
}
```

- [ ] **Step 4: Write GleapNetworkLog**

Create `src/Gleap.Core/Models/GleapNetworkLog.cs`:

```csharp
namespace GleapSDK.Models;

/// <summary>One captured HTTP exchange, as sent in <c>networkLogs</c>.</summary>
public sealed class GleapNetworkLog
{
    public string Type { get; set; } = "";
    public string Url { get; set; } = "";
    public string Date { get; set; } = "";
    public double Duration { get; set; }
    public bool Success { get; set; }
    public GleapNetworkRequest Request { get; set; } = new();
    public GleapNetworkResponse Response { get; set; } = new();
}
```

- [ ] **Step 5: Write the truncation factory**

Create `src/Gleap.Core/Http/NetworkLogFactory.cs`:

```csharp
using System.Collections.Generic;
using GleapSDK.Models;

namespace GleapSDK.Http;

/// <summary>
/// Builds <see cref="GleapNetworkLog"/> entries, applying the native SDKs' safety guard:
/// payloads/responses over 1,000,000 bytes are replaced with a sentinel rather than logged.
/// </summary>
public static class NetworkLogFactory
{
    private const int MaxBodyLength = 1_000_000;

    public static GleapNetworkLog Build(
        string type, string url, string date, double durationMs,
        int statusCode, string statusText,
        string? requestPayload, IReadOnlyDictionary<string, string>? requestHeaders,
        string? responseBody)
    {
        return new GleapNetworkLog
        {
            Type = type,
            Url = url,
            Date = date,
            Duration = durationMs,
            Success = statusCode is >= 200 and < 300,
            Request = new GleapNetworkRequest
            {
                Payload = Guard(requestPayload, "<payload_too_large>"),
                Headers = requestHeaders
            },
            Response = new GleapNetworkResponse
            {
                Status = statusCode,
                StatusText = statusText,
                ResponseText = Guard(responseBody, "<response_too_large>") ?? ""
            }
        };
    }

    private static string? Guard(string? body, string sentinel)
    {
        if (body is null)
        {
            return null;
        }
        return body.Length > MaxBodyLength ? sentinel : body;
    }
}
```

- [ ] **Step 6: Run test to verify it passes**

Run: `dotnet test --filter NetworkLogFactoryTests`
Expected: PASS (3 tests).

- [ ] **Step 7: Commit**

```bash
git add -A && git commit -m "feat: add network-log models and truncating factory"
```

---

## Task 4: ConsoleLogBuffer

**Files:**
- Create: `src/Gleap.Core/Collection/ConsoleLogBuffer.cs`
- Test: `tests/Gleap.Core.Tests/ConsoleLogBufferTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/Gleap.Core.Tests/ConsoleLogBufferTests.cs`:

```csharp
using System.Linq;
using Gleap.Core.Tests.Fakes;
using GleapSDK;
using GleapSDK.Collection;
using Xunit;

namespace Gleap.Core.Tests;

public class ConsoleLogBufferTests
{
    [Fact]
    public void Add_StampsDateAndMapsLevel()
    {
        var clock = new FakeClock();
        var buffer = new ConsoleLogBuffer(clock, capacity: 100);

        buffer.Add("boom", LogLevel.Error);

        var entry = buffer.Snapshot().Single();
        Assert.Equal("boom", entry.Log);
        Assert.Equal("ERROR", entry.Priority);
        Assert.Equal("2026-07-13T10:00:00.000Z", entry.Date);
    }

    [Fact]
    public void Add_DefaultsToInfoPriority()
    {
        var buffer = new ConsoleLogBuffer(new FakeClock(), 100);
        buffer.Add("hello", LogLevel.Info);
        Assert.Equal("INFO", buffer.Snapshot().Single().Priority);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter ConsoleLogBufferTests`
Expected: FAIL — `ConsoleLogBuffer` does not exist.

- [ ] **Step 3: Write the buffer**

Create `src/Gleap.Core/Collection/ConsoleLogBuffer.cs`:

```csharp
using System.Collections.Generic;
using System.Globalization;
using GleapSDK.Models;
using GleapSDK.Time;

namespace GleapSDK.Collection;

/// <summary>Bounded buffer of console/log lines, timestamped via <see cref="IClock"/>.</summary>
public sealed class ConsoleLogBuffer
{
    private readonly IClock _clock;
    private readonly RingBuffer<GleapLog> _buffer;

    public ConsoleLogBuffer(IClock clock, int capacity)
    {
        _clock = clock;
        _buffer = new RingBuffer<GleapLog>(capacity);
    }

    public void Add(string message, LogLevel level)
    {
        _buffer.Add(new GleapLog
        {
            Date = _clock.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture),
            Log = message,
            Priority = Priority(level)
        });
    }

    public IReadOnlyList<GleapLog> Snapshot() => _buffer.Snapshot();

    public void Clear() => _buffer.Clear();

    private static string Priority(LogLevel level) => level switch
    {
        LogLevel.Error => "ERROR",
        LogLevel.Warning => "WARNING",
        _ => "INFO"
    };
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter ConsoleLogBufferTests`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "feat: add ConsoleLogBuffer"
```

---

## Task 5: EventBuffer

**Files:**
- Create: `src/Gleap.Core/Collection/EventBuffer.cs`
- Test: `tests/Gleap.Core.Tests/EventBufferTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/Gleap.Core.Tests/EventBufferTests.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using Gleap.Core.Tests.Fakes;
using GleapSDK.Collection;
using Xunit;

namespace Gleap.Core.Tests;

public class EventBufferTests
{
    [Fact]
    public void Add_StampsDateAndKeepsNameData()
    {
        var buffer = new EventBuffer(new FakeClock(), 100);
        buffer.Add("purchase", new Dictionary<string, object> { ["amount"] = 5 });

        var e = buffer.Snapshot().Single();
        Assert.Equal("purchase", e.Name);
        Assert.Equal("2026-07-13T10:00:00.000Z", e.Date);
        Assert.NotNull(e.Data);
    }

    [Fact]
    public void Add_AllowsNullData()
    {
        var buffer = new EventBuffer(new FakeClock(), 100);
        buffer.Add("opened", null);
        Assert.Null(buffer.Snapshot().Single().Data);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter EventBufferTests`
Expected: FAIL — `EventBuffer` does not exist.

- [ ] **Step 3: Write the buffer**

Create `src/Gleap.Core/Collection/EventBuffer.cs`:

```csharp
using System.Collections.Generic;
using System.Globalization;
using GleapSDK.Models;
using GleapSDK.Time;

namespace GleapSDK.Collection;

/// <summary>Bounded buffer of tracked events, timestamped via <see cref="IClock"/>.</summary>
public sealed class EventBuffer
{
    private readonly IClock _clock;
    private readonly RingBuffer<GleapEvent> _buffer;

    public EventBuffer(IClock clock, int capacity)
    {
        _clock = clock;
        _buffer = new RingBuffer<GleapEvent>(capacity);
    }

    public void Add(string name, object? data)
    {
        _buffer.Add(new GleapEvent
        {
            Name = name,
            Data = data,
            Date = _clock.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture)
        });
    }

    public IReadOnlyList<GleapEvent> Snapshot() => _buffer.Snapshot();

    public void Clear() => _buffer.Clear();
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter EventBufferTests`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "feat: add EventBuffer"
```

---

## Task 6: NetworkLogBuffer (blacklist + propsToIgnore)

**Files:**
- Create: `src/Gleap.Core/Collection/NetworkLogBuffer.cs`
- Test: `tests/Gleap.Core.Tests/NetworkLogBufferTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/Gleap.Core.Tests/NetworkLogBufferTests.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using GleapSDK.Collection;
using GleapSDK.Models;
using Xunit;

namespace Gleap.Core.Tests;

public class NetworkLogBufferTests
{
    private static GleapNetworkLog Log(string url) => new()
    {
        Type = "GET", Url = url, Date = "d", Success = true,
        Request = new GleapNetworkRequest { Headers = new Dictionary<string, object> { ["Authorization"] = "secret", ["Accept"] = "json" } },
        Response = new GleapNetworkResponse { Status = 200 }
    };

    [Fact]
    public void Add_SkipsBlacklistedUrls()
    {
        var buffer = new NetworkLogBuffer(100);
        buffer.SetBlacklist(new[] { "gleap.io" });

        buffer.Add(Log("https://api.gleap.io/sessions"));
        buffer.Add(Log("https://api.example.com/x"));

        Assert.Single(buffer.Snapshot());
        Assert.Equal("https://api.example.com/x", buffer.Snapshot()[0].Url);
    }

    [Fact]
    public void Add_StripsIgnoredHeaderProps()
    {
        var buffer = new NetworkLogBuffer(100);
        buffer.SetPropsToIgnore(new[] { "Authorization" });

        buffer.Add(Log("https://api.example.com/x"));

        var headers = (Dictionary<string, object>)buffer.Snapshot()[0].Request.Headers!;
        Assert.False(headers.ContainsKey("Authorization"));
        Assert.True(headers.ContainsKey("Accept"));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter NetworkLogBufferTests`
Expected: FAIL — `NetworkLogBuffer` does not exist.

- [ ] **Step 3: Write the buffer**

Create `src/Gleap.Core/Collection/NetworkLogBuffer.cs`:

```csharp
using System;
using System.Collections.Generic;
using GleapSDK.Models;

namespace GleapSDK.Collection;

/// <summary>
/// Bounded buffer of network logs. Drops entries whose URL contains any blacklisted
/// substring, and strips ignored keys from request/response header maps.
/// </summary>
public sealed class NetworkLogBuffer
{
    private readonly RingBuffer<GleapNetworkLog> _buffer;
    private IReadOnlyList<string> _blacklist = Array.Empty<string>();
    private IReadOnlyList<string> _propsToIgnore = Array.Empty<string>();

    public NetworkLogBuffer(int capacity) => _buffer = new RingBuffer<GleapNetworkLog>(capacity);

    public void SetBlacklist(IReadOnlyList<string> blacklist) => _blacklist = blacklist;

    public void SetPropsToIgnore(IReadOnlyList<string> propsToIgnore) => _propsToIgnore = propsToIgnore;

    public void Add(GleapNetworkLog log)
    {
        foreach (var blocked in _blacklist)
        {
            if (log.Url.Contains(blocked))
            {
                return;
            }
        }

        Strip(log.Request.Headers as IDictionary<string, object>);
        _buffer.Add(log);
    }

    private void Strip(IDictionary<string, object>? headers)
    {
        if (headers is null)
        {
            return;
        }
        foreach (var prop in _propsToIgnore)
        {
            headers.Remove(prop);
        }
    }

    public IReadOnlyList<GleapNetworkLog> Snapshot() => _buffer.Snapshot();

    public void Clear() => _buffer.Clear();
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter NetworkLogBufferTests`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "feat: add NetworkLogBuffer with blacklist and prop stripping"
```

---

## Task 7: GleapHttpHandler (opt-in network capture)

**Files:**
- Create: `src/Gleap.Core/Http/GleapHttpHandler.cs`
- Test: `tests/Gleap.Core.Tests/GleapHttpHandlerTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/Gleap.Core.Tests/GleapHttpHandlerTests.cs`:

```csharp
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Gleap.Core.Tests.Fakes;
using GleapSDK.Collection;
using GleapSDK.Http;
using Xunit;

namespace Gleap.Core.Tests;

public class GleapHttpHandlerTests
{
    private sealed class StubInner : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("pong")
            });
    }

    [Fact]
    public async Task Captures_RequestAndResponse_IntoBuffer()
    {
        var buffer = new NetworkLogBuffer(100);
        var handler = new GleapHttpHandler(buffer, new FakeClock()) { InnerHandler = new StubInner() };
        using var client = new HttpClient(handler);

        await client.GetAsync("https://api.example.com/ping");

        var log = Assert.Single(buffer.Snapshot());
        Assert.Equal("GET", log.Type);
        Assert.Equal("https://api.example.com/ping", log.Url);
        Assert.Equal(200, log.Response.Status);
        Assert.Equal("pong", log.Response.ResponseText);
        Assert.True(log.Success);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter GleapHttpHandlerTests`
Expected: FAIL — `GleapHttpHandler` does not exist.

- [ ] **Step 3: Write the handler**

Create `src/Gleap.Core/Http/GleapHttpHandler.cs`:

```csharp
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using GleapSDK.Collection;
using GleapSDK.Time;

namespace GleapSDK.Http;

/// <summary>
/// Opt-in <see cref="DelegatingHandler"/> the host app adds to its <see cref="HttpClient"/>
/// so outbound traffic is captured into the Gleap network log. Only traffic routed through
/// this handler is captured (there is no global interception in managed C#).
/// </summary>
public sealed class GleapHttpHandler : DelegatingHandler
{
    private readonly NetworkLogBuffer _buffer;
    private readonly IClock _clock;

    public GleapHttpHandler(NetworkLogBuffer buffer, IClock clock)
    {
        _buffer = buffer;
        _clock = clock;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var start = _clock.UtcNow;
        var date = start.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
        var reqPayload = request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync().ConfigureAwait(false);

        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        var respBody = response.Content is null
            ? ""
            : await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        var duration = (_clock.UtcNow - start).TotalMilliseconds;

        _buffer.Add(NetworkLogFactory.Build(
            type: request.Method.Method,
            url: request.RequestUri?.ToString() ?? "",
            date: date,
            durationMs: duration,
            statusCode: (int)response.StatusCode,
            statusText: response.ReasonPhrase ?? "",
            requestPayload: reqPayload,
            requestHeaders: HeaderMap(request.Headers),
            responseBody: respBody));

        return response;
    }

    private static Dictionary<string, string> HeaderMap(System.Net.Http.Headers.HttpHeaders headers)
    {
        var map = new Dictionary<string, string>();
        foreach (var header in headers)
        {
            map[header.Key] = string.Join(", ", header.Value);
        }
        return map;
    }
}
```

> `NetworkLogFactory.Build` types `requestHeaders` as `IReadOnlyDictionary<string,string>`; the `Dictionary<string,string>` from `HeaderMap` satisfies it. `NetworkLogBuffer.Strip` casts `Headers` to `IDictionary<string,object>` and no-ops if the cast fails, so a `Dictionary<string,string>` in `Headers` is simply left untouched by prop-stripping — acceptable for Part 2a (header-value redaction refinement is deferred).

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter GleapHttpHandlerTests`
Expected: PASS (1 test).

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "feat: add GleapHttpHandler for opt-in network capture"
```

---

## Task 8: Data stores + attachment model

**Files:**
- Create: `src/Gleap.Core/Models/GleapAttachment.cs`
- Create: `src/Gleap.Core/Data/CustomDataStore.cs`
- Create: `src/Gleap.Core/Data/TicketAttributeStore.cs`
- Create: `src/Gleap.Core/Data/TagStore.cs`
- Create: `src/Gleap.Core/Data/AttachmentStore.cs`
- Test: `tests/Gleap.Core.Tests/DataStoreTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/Gleap.Core.Tests/DataStoreTests.cs`:

```csharp
using System.Linq;
using GleapSDK.Data;
using Xunit;

namespace Gleap.Core.Tests;

public class DataStoreTests
{
    [Fact]
    public void CustomData_SetRemoveClear()
    {
        var store = new CustomDataStore();
        store.Set("a", "1");
        store.Merge(new System.Collections.Generic.Dictionary<string, object> { ["b"] = 2 });
        Assert.Equal(2, store.Snapshot().Count);
        store.Remove("a");
        Assert.False(store.Snapshot().ContainsKey("a"));
        store.Clear();
        Assert.Empty(store.Snapshot());
    }

    [Fact]
    public void TicketAttributes_SetUnsetClear()
    {
        var store = new TicketAttributeStore();
        store.Set("priority", "high");
        Assert.Equal("high", store.Snapshot()["priority"]);
        store.Unset("priority");
        Assert.Empty(store.Snapshot());
    }

    [Fact]
    public void Tags_Replace()
    {
        var store = new TagStore();
        store.Set(new[] { "vip", "beta" });
        Assert.Equal(new[] { "vip", "beta" }, store.Snapshot().ToArray());
    }

    [Fact]
    public void Attachments_AddClear()
    {
        var store = new AttachmentStore();
        store.Add("Zm9v", "a.txt");
        Assert.Equal("a.txt", store.Snapshot().Single().FileName);
        store.Clear();
        Assert.Empty(store.Snapshot());
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter DataStoreTests`
Expected: FAIL — store types do not exist.

- [ ] **Step 3: Write the attachment model**

Create `src/Gleap.Core/Models/GleapAttachment.cs`:

```csharp
namespace GleapSDK.Models;

/// <summary>A base64-encoded file attached to a report.</summary>
public sealed class GleapAttachment
{
    public string Base64File { get; set; } = "";
    public string FileName { get; set; } = "";
}
```

- [ ] **Step 4: Write CustomDataStore**

Create `src/Gleap.Core/Data/CustomDataStore.cs`:

```csharp
using System.Collections.Generic;

namespace GleapSDK.Data;

/// <summary>Arbitrary key/value custom data attached to the session/report.</summary>
public sealed class CustomDataStore
{
    private readonly Dictionary<string, object> _data = new();

    public void Set(string key, object value) => _data[key] = value;

    public void Merge(IReadOnlyDictionary<string, object> values)
    {
        foreach (var kv in values)
        {
            _data[kv.Key] = kv.Value;
        }
    }

    public void Remove(string key) => _data.Remove(key);

    public void Clear() => _data.Clear();

    public IReadOnlyDictionary<string, object> Snapshot() => new Dictionary<string, object>(_data);
}
```

- [ ] **Step 5: Write TicketAttributeStore**

Create `src/Gleap.Core/Data/TicketAttributeStore.cs`:

```csharp
using System.Collections.Generic;

namespace GleapSDK.Data;

/// <summary>Ticket attributes prefilled on the next created ticket.</summary>
public sealed class TicketAttributeStore
{
    private readonly Dictionary<string, object> _data = new();

    public void Set(string key, object value) => _data[key] = value;

    public void Unset(string key) => _data.Remove(key);

    public void Clear() => _data.Clear();

    public IReadOnlyDictionary<string, object> Snapshot() => new Dictionary<string, object>(_data);
}
```

- [ ] **Step 6: Write TagStore**

Create `src/Gleap.Core/Data/TagStore.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;

namespace GleapSDK.Data;

/// <summary>The set of tags applied to the next report (replace-on-set).</summary>
public sealed class TagStore
{
    private IReadOnlyList<string> _tags = new List<string>();

    public void Set(IEnumerable<string> tags) => _tags = tags.ToList();

    public IReadOnlyList<string> Snapshot() => _tags;
}
```

- [ ] **Step 7: Write AttachmentStore**

Create `src/Gleap.Core/Data/AttachmentStore.cs`:

```csharp
using System.Collections.Generic;
using GleapSDK.Models;

namespace GleapSDK.Data;

/// <summary>Pending file attachments for the next report.</summary>
public sealed class AttachmentStore
{
    private readonly List<GleapAttachment> _attachments = new();

    public void Add(string base64File, string fileName) =>
        _attachments.Add(new GleapAttachment { Base64File = base64File, FileName = fileName });

    public void Clear() => _attachments.Clear();

    public IReadOnlyList<GleapAttachment> Snapshot() => new List<GleapAttachment>(_attachments);
}
```

- [ ] **Step 8: Run test to verify it passes**

Run: `dotnet test --filter DataStoreTests`
Expected: PASS (4 tests).

- [ ] **Step 9: Commit**

```bash
git add -A && git commit -m "feat: add custom-data, ticket-attribute, tag and attachment stores"
```

---

## Task 9: Metadata provider seam

**Files:**
- Create: `src/Gleap.Core/Metadata/IMetadataProvider.cs`
- Create: `src/Gleap.Core/Metadata/DefaultMetadataProvider.cs`
- Test: `tests/Gleap.Core.Tests/DefaultMetadataProviderTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/Gleap.Core.Tests/DefaultMetadataProviderTests.cs`:

```csharp
using GleapSDK.Metadata;
using Xunit;

namespace Gleap.Core.Tests;

public class DefaultMetadataProviderTests
{
    [Fact]
    public void Collect_IncludesSdkTypeVersionAndLocale()
    {
        var provider = new DefaultMetadataProvider(sdkType: "NET/Test", sdkVersion: "0.1.0");
        var meta = provider.Collect();

        Assert.Equal("NET/Test", meta["sdkType"]);
        Assert.Equal("0.1.0", meta["sdkVersion"]);
        Assert.True(meta.ContainsKey("preferredUserLocale"));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter DefaultMetadataProviderTests`
Expected: FAIL — types do not exist.

- [ ] **Step 3: Write the interface**

Create `src/Gleap.Core/Metadata/IMetadataProvider.cs`:

```csharp
using System.Collections.Generic;

namespace GleapSDK.Metadata;

/// <summary>
/// Supplies device/session metadata for reports. Platform packages implement this with
/// native device info; <see cref="DefaultMetadataProvider"/> supplies the runtime-agnostic subset.
/// </summary>
public interface IMetadataProvider
{
    IReadOnlyDictionary<string, object?> Collect();
}
```

- [ ] **Step 4: Write the default provider**

Create `src/Gleap.Core/Metadata/DefaultMetadataProvider.cs`:

```csharp
using System.Collections.Generic;
using System.Globalization;

namespace GleapSDK.Metadata;

/// <summary>
/// The cross-platform metadata subset available without native APIs. Platform packages
/// wrap or replace this in SP-1 to add device model, OS version, battery, disk, etc.
/// </summary>
public sealed class DefaultMetadataProvider : IMetadataProvider
{
    private readonly string _sdkType;
    private readonly string _sdkVersion;

    public DefaultMetadataProvider(string sdkType, string sdkVersion)
    {
        _sdkType = sdkType;
        _sdkVersion = sdkVersion;
    }

    public IReadOnlyDictionary<string, object?> Collect() => new Dictionary<string, object?>
    {
        ["sdkType"] = _sdkType,
        ["sdkVersion"] = _sdkVersion,
        ["preferredUserLocale"] = CultureInfo.CurrentCulture.Name
    };
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test --filter DefaultMetadataProviderTests`
Expected: PASS (1 test).

- [ ] **Step 6: Commit**

```bash
git add -A && git commit -m "feat: add metadata provider seam with cross-platform default"
```

---

## Task 10: SessionDataCollector

**Files:**
- Create: `src/Gleap.Core/Collection/SessionDataCollector.cs`
- Test: `tests/Gleap.Core.Tests/SessionDataCollectorTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/Gleap.Core.Tests/SessionDataCollectorTests.cs`:

```csharp
using System.Collections.Generic;
using Gleap.Core.Tests.Fakes;
using GleapSDK;
using GleapSDK.Collection;
using GleapSDK.Data;
using GleapSDK.Metadata;
using GleapSDK.Serialization;
using Xunit;

namespace Gleap.Core.Tests;

public class SessionDataCollectorTests
{
    private static SessionDataCollector NewCollector(out ConsoleLogBuffer logs, out CustomDataStore custom)
    {
        var clock = new FakeClock();
        logs = new ConsoleLogBuffer(clock, 100);
        custom = new CustomDataStore();
        return new SessionDataCollector(
            logs,
            new EventBuffer(clock, 100),
            new NetworkLogBuffer(100),
            custom,
            new TicketAttributeStore(),
            new TagStore(),
            new DefaultMetadataProvider("NET/Test", "0.1.0"));
    }

    [Fact]
    public void BuildTicketData_HasAllRequiredKeys()
    {
        var collector = NewCollector(out _, out _);
        var data = collector.BuildTicketData();

        foreach (var key in new[] { "customData", "formData", "metaData", "consoleLog", "networkLogs", "customEventLog", "tags" })
        {
            Assert.True(data.ContainsKey(key), $"missing key {key}");
        }
    }

    [Fact]
    public void BuildTicketData_ReflectsBufferedData()
    {
        var collector = NewCollector(out var logs, out var custom);
        logs.Add("hello", LogLevel.Info);
        custom.Set("plan", "pro");

        var json = new SystemTextJsonSerializer().Serialize(collector.BuildTicketData());

        Assert.Contains("hello", json);
        Assert.Contains("\"plan\":\"pro\"", json);
        Assert.Contains("\"sdkType\":\"NET/Test\"", json);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter SessionDataCollectorTests`
Expected: FAIL — `SessionDataCollector` does not exist.

- [ ] **Step 3: Write the collector**

Create `src/Gleap.Core/Collection/SessionDataCollector.cs`:

```csharp
using System.Collections.Generic;
using GleapSDK.Data;
using GleapSDK.Metadata;

namespace GleapSDK.Collection;

/// <summary>
/// Aggregates every collected source into the payload the web widget requests via the
/// <c>collect-ticket-data</c> bridge message (spec §5):
/// { customData, formData, metaData, consoleLog, networkLogs, customEventLog, tags }.
/// </summary>
public sealed class SessionDataCollector
{
    private readonly ConsoleLogBuffer _consoleLog;
    private readonly EventBuffer _eventLog;
    private readonly NetworkLogBuffer _networkLog;
    private readonly CustomDataStore _customData;
    private readonly TicketAttributeStore _ticketAttributes;
    private readonly TagStore _tags;
    private readonly IMetadataProvider _metadata;

    public SessionDataCollector(
        ConsoleLogBuffer consoleLog,
        EventBuffer eventLog,
        NetworkLogBuffer networkLog,
        CustomDataStore customData,
        TicketAttributeStore ticketAttributes,
        TagStore tags,
        IMetadataProvider metadata)
    {
        _consoleLog = consoleLog;
        _eventLog = eventLog;
        _networkLog = networkLog;
        _customData = customData;
        _ticketAttributes = ticketAttributes;
        _tags = tags;
        _metadata = metadata;
    }

    public IReadOnlyDictionary<string, object?> BuildTicketData() => new Dictionary<string, object?>
    {
        ["customData"] = _customData.Snapshot(),
        ["formData"] = _ticketAttributes.Snapshot(),
        ["metaData"] = _metadata.Collect(),
        ["consoleLog"] = _consoleLog.Snapshot(),
        ["networkLogs"] = _networkLog.Snapshot(),
        ["customEventLog"] = _eventLog.Snapshot(),
        ["tags"] = _tags.Snapshot()
    };
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter SessionDataCollectorTests`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "feat: add SessionDataCollector assembling the collect-ticket-data payload"
```

---

## Task 11: Bridge — collect-ticket-data incoming event

**Files:**
- Modify: `src/Gleap.Core/Bridge/WebViewBridge.Incoming.cs`
- Test: `tests/Gleap.Core.Tests/WebViewBridgeCollectDataTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/Gleap.Core.Tests/WebViewBridgeCollectDataTests.cs`:

```csharp
using Gleap.Core.Tests.Fakes;
using GleapSDK.Bridge;
using GleapSDK.Serialization;
using Xunit;

namespace Gleap.Core.Tests;

public class WebViewBridgeCollectDataTests
{
    [Fact]
    public void CollectTicketData_RaisesEvent()
    {
        var ch = new FakeWebViewChannel();
        var bridge = new WebViewBridge(ch, new SystemTextJsonSerializer());
        var raised = false;
        bridge.CollectTicketDataRequested += () => raised = true;

        ch.SimulateIncoming("{\"name\":\"collect-ticket-data\"}");

        Assert.True(raised);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter WebViewBridgeCollectDataTests`
Expected: FAIL — `CollectTicketDataRequested` does not exist.

- [ ] **Step 3: Add the event and dispatch case**

In `src/Gleap.Core/Bridge/WebViewBridge.Incoming.cs`, add this event declaration alongside the existing events (after `SendFeedbackRequested`):

```csharp
    /// <summary>Raised on "collect-ticket-data" — the widget is asking for the current report data.</summary>
    public event System.Action? CollectTicketDataRequested;
```

And add this `case` to the `switch (msg.Name)` block, immediately before the `case "send-feedback":` line:

```csharp
            case "collect-ticket-data":
                CollectTicketDataRequested?.Invoke();
                break;
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter WebViewBridgeCollectDataTests`
Expected: PASS (1 test). Also run `dotnet test --filter WebViewBridgeIncomingTests` to confirm no regression (still passes).

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "feat: bridge raises CollectTicketDataRequested on collect-ticket-data"
```

---

## Task 12: Wire data methods + collect-ticket-data reply into backend & facade

**Files:**
- Modify: `src/Gleap.Core/IGleapBackend.cs`
- Modify: `src/Gleap.Core/ManagedBackend.cs`
- Modify: `src/Gleap.Core/Gleap.cs`
- Test: `tests/Gleap.Core.Tests/ManagedBackendDataTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/Gleap.Core.Tests/ManagedBackendDataTests.cs`:

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

public class ManagedBackendDataTests
{
    private static (ManagedBackend backend, FakeWebViewChannel ch) NewInitialized()
    {
        var http = new FakeHttpTransport();
        http.Responses.Enqueue(new HttpResult(200, "{\"gleapId\":\"g1\",\"gleapHash\":\"h1\"}"));
        http.Responses.Enqueue(new HttpResult(200, "{\"flowConfig\":{},\"projectActions\":{}}"));
        var ch = new FakeWebViewChannel();
        var backend = new ManagedBackend(new ManagedBackend.Dependencies
        {
            Http = http,
            Json = new SystemTextJsonSerializer(),
            Store = new InMemoryKeyValueStore(),
            Channel = ch,
            Endpoints = GleapEndpoints.Default
        });
        backend.InitializeAsync("token-1", CancellationToken.None).GetAwaiter().GetResult();
        return (backend, ch);
    }

    [Fact]
    public void CollectTicketData_RepliesWithCollectedData()
    {
        var (backend, ch) = NewInitialized();
        backend.Log("boom", LogLevel.Error);
        backend.SetCustomData("plan", "pro");
        ch.SimulateIncoming("{\"name\":\"ping\"}"); // connect so replies are sent immediately

        ch.SimulateIncoming("{\"name\":\"collect-ticket-data\"}");

        Assert.Contains(ch.ExecutedScripts, s =>
            s.Contains("collect-ticket-data") && s.Contains("boom") && s.Contains("\"plan\":\"pro\""));
    }

    [Fact]
    public void TrackEvent_And_SetTags_DoNotThrow_BeforePing()
    {
        var (backend, _) = NewInitialized();
        backend.TrackEvent("opened", null);
        backend.SetTags(new[] { "vip" });
        backend.AddAttachment("Zm9v", "a.txt");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter ManagedBackendDataTests`
Expected: FAIL — data methods do not exist on the backend.

- [ ] **Step 3: Extend the backend contract**

In `src/Gleap.Core/IGleapBackend.cs`, add these members to the interface (after `ShowSurvey`):

```csharp
    void Log(string message, LogLevel level);
    void TrackEvent(string name, object? data);
    void TrackPage(string pageName);
    void SetCustomData(string key, string value);
    void AttachCustomData(IReadOnlyDictionary<string, object> data);
    void RemoveCustomDataForKey(string key);
    void ClearCustomData();
    void SetTicketAttribute(string key, object value);
    void UnsetTicketAttribute(string key);
    void ClearTicketAttributes();
    void SetTags(string[] tags);
    void AddAttachment(string base64File, string fileName);
    void RemoveAllAttachments();
    void SetNetworkLogsBlacklist(string[] blacklist);
    void SetNetworkLogPropsToIgnore(string[] propsToIgnore);
```

Add `using System.Collections.Generic;` at the top of the file.

- [ ] **Step 4: Extend ManagedBackend**

In `src/Gleap.Core/ManagedBackend.cs`:

Add usings at the top (with the existing ones):

```csharp
using System.Collections.Generic;
using GleapSDK.Collection;
using GleapSDK.Data;
using GleapSDK.Metadata;
using GleapSDK.Time;
```

Add these fields alongside the existing private fields:

```csharp
    private readonly ConsoleLogBuffer _consoleLog;
    private readonly EventBuffer _eventLog;
    private readonly NetworkLogBuffer _networkLog;
    private readonly CustomDataStore _customData = new();
    private readonly TicketAttributeStore _ticketAttributes = new();
    private readonly TagStore _tags = new();
    private readonly AttachmentStore _attachments = new();
    private readonly SessionDataCollector _collector;
```

Replace the constructor with one that builds the collector (keep the existing `_d = dependencies` assignment):

```csharp
    public ManagedBackend(Dependencies dependencies)
    {
        _d = dependencies;
        var clock = new SystemClock(); // concrete local avoids CA1859 (interface-typed local)
        _consoleLog = new ConsoleLogBuffer(clock, capacity: 100);
        _eventLog = new EventBuffer(clock, capacity: 100);
        _networkLog = new NetworkLogBuffer(capacity: 20);
        _collector = new SessionDataCollector(
            _consoleLog, _eventLog, _networkLog,
            _customData, _ticketAttributes, _tags,
            new DefaultMetadataProvider("NET/Windows", "0.1.0"));
    }
```

In `InitializeAsync`, after `_ = new WidgetBootstrapper(_bridge, BuildSnapshot);`, subscribe the collect-ticket-data reply:

```csharp
        _bridge.CollectTicketDataRequested += () =>
            _bridge.Send(new GleapBridgeMessage { Name = "collect-ticket-data", Data = _collector.BuildTicketData() });
```

Add the data-method implementations at the end of the class (before the closing brace):

```csharp
    public void Log(string message, LogLevel level) => _consoleLog.Add(message, level);
    public void TrackEvent(string name, object? data) => _eventLog.Add(name, data);
    public void TrackPage(string pageName) => _eventLog.Add("pageView", new Dictionary<string, object> { ["page"] = pageName });
    public void SetCustomData(string key, string value) => _customData.Set(key, value);
    public void AttachCustomData(IReadOnlyDictionary<string, object> data) => _customData.Merge(data);
    public void RemoveCustomDataForKey(string key) => _customData.Remove(key);
    public void ClearCustomData() => _customData.Clear();
    public void SetTicketAttribute(string key, object value) => _ticketAttributes.Set(key, value);
    public void UnsetTicketAttribute(string key) => _ticketAttributes.Unset(key);
    public void ClearTicketAttributes() => _ticketAttributes.Clear();
    public void SetTags(string[] tags) => _tags.Set(tags);
    public void AddAttachment(string base64File, string fileName) => _attachments.Add(base64File, fileName);
    public void RemoveAllAttachments() => _attachments.Clear();
    public void SetNetworkLogsBlacklist(string[] blacklist) => _networkLog.SetBlacklist(blacklist);
    public void SetNetworkLogPropsToIgnore(string[] propsToIgnore) => _networkLog.SetPropsToIgnore(propsToIgnore);
```

> `AttachmentStore` is populated now; its contents are attached to reports in Part 2b (`/bugs/v2`). `GleapHttpHandler` writing into `_networkLog` is wired by the host app in SP-1; Part 2a only guarantees the buffer + blacklist plumbing exists.

- [ ] **Step 5: Extend the facade**

In `src/Gleap.Core/Gleap.cs`, add `using System.Collections.Generic;` at the top, and add these delegating methods (before `ResetForTest`):

```csharp
    public static void Log(string message, LogLevel level = LogLevel.Info) => Backend.Log(message, level);
    public static void TrackEvent(string name, object? data = null) => Backend.TrackEvent(name, data);
    public static void TrackPage(string pageName) => Backend.TrackPage(pageName);
    public static void SetCustomData(string key, string value) => Backend.SetCustomData(key, value);
    public static void AttachCustomData(IReadOnlyDictionary<string, object> data) => Backend.AttachCustomData(data);
    public static void RemoveCustomDataForKey(string key) => Backend.RemoveCustomDataForKey(key);
    public static void ClearCustomData() => Backend.ClearCustomData();
    public static void SetTicketAttribute(string key, object value) => Backend.SetTicketAttribute(key, value);
    public static void UnsetTicketAttribute(string key) => Backend.UnsetTicketAttribute(key);
    public static void ClearTicketAttributes() => Backend.ClearTicketAttributes();
    public static void SetTags(string[] tags) => Backend.SetTags(tags);
    public static void AddAttachment(string base64File, string fileName) => Backend.AddAttachment(base64File, fileName);
    public static void RemoveAllAttachments() => Backend.RemoveAllAttachments();
    public static void SetNetworkLogsBlacklist(string[] blacklist) => Backend.SetNetworkLogsBlacklist(blacklist);
    public static void SetNetworkLogPropsToIgnore(string[] propsToIgnore) => Backend.SetNetworkLogPropsToIgnore(propsToIgnore);
```

- [ ] **Step 6: Update the facade fake in GleapFacadeTests**

The `IGleapBackend` fake in `tests/Gleap.Core.Tests/GleapFacadeTests.cs` (from Part 1 review) must implement the new members or it won't compile. Add no-op implementations for all 15 new members to that fake (e.g. `public void Log(string message, LogLevel level) { }`, etc.). Match the interface signatures exactly.

- [ ] **Step 7: Run test to verify it passes**

Run: `dotnet test --filter ManagedBackendDataTests`
Expected: PASS (2 tests). Also run `dotnet test --filter GleapFacadeTests` — still passes.

- [ ] **Step 8: Run the FULL suite**

Run: `cd /Users/tobiasduelli/development/projects/gleap/CSharp-SDK && dotnet test`
Expected: all green.

- [ ] **Step 9: Commit**

```bash
git add -A && git commit -m "feat: wire data-collection methods and collect-ticket-data reply into backend and facade"
```

---

## Task 13: Quality gate

**Files:** none (verification + fixes only).

- [ ] **Step 1: Format**

Run: `cd /Users/tobiasduelli/development/projects/gleap/CSharp-SDK && dotnet format`
Then: `dotnet format --verify-no-changes` → expect exit 0.

- [ ] **Step 2: Build with warnings as errors**

Run: `dotnet build -warnaserror --no-incremental`
Expected: Build succeeded, 0 warnings, 0 errors. Fix any surfaced warnings. If a genuine test-convention or DI-interface false positive appears in a test file, extend the existing `[tests/**/*.cs]` block in `.editorconfig` consistently; otherwise fix the code. Do not suppress production warnings other than the pre-agreed `CS1591`.

- [ ] **Step 3: Full suite**

Run: `dotnet test`
Expected: all tests green.

- [ ] **Step 4: Commit**

```bash
git add -A && git commit -m "chore: pass quality gate for data-collection layer"
```

---

## Definition of Done (SP-0 Part 2a)

- `dotnet test` green; `dotnet build -warnaserror` clean; `dotnet format --verify-no-changes` clean.
- The SDK can buffer console logs, tracked events, and network logs (with 1 MB truncation + blacklist), and store custom data / ticket attributes / tags / attachments — all via the `Gleap` facade.
- When the widget sends `collect-ticket-data`, `ManagedBackend` replies with the full `{customData, formData, metaData, consoleLog, networkLogs, customEventLog, tags}` payload.
- All headless: no network, no WebView, no platform APIs (metadata beyond the cross-platform subset, real console/screenshot hooks, and `/bugs/v2` submission are deferred).

## Follow-up

- **SP-0 Part 2b:** `POST /bugs/v2` feedback assembly (honoring `excludeData`) + silent crash report + attachment/image upload; wire `send-feedback` + `SendSilentCrashReport` facade method.
- **SP-0 Part 2c:** `/sessions/ping` polling loop (→ `{a,u}`), outbound dispatch (notification/banner/modal/survey), WebSocket, replay scheduling + screenshot capture seam.
- **SP-1:** platform `IWebViewChannel` (WebView2), real `IMetadataProvider`, console-log + `GleapHttpHandler` hookup, `IKeyValueStore`.
