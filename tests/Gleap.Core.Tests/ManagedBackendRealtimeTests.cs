using System.Text.Json;
using Gleap.Core.Tests.Fakes;
using GleapSDK;
using GleapSDK.Http;
using GleapSDK.Serialization;
using GleapSDK.Session;

namespace Gleap.Core.Tests;

public class ManagedBackendRealtimeTests
{
    private static async Task<(ManagedBackend backend, FakeWebViewChannel ch, FakeHttpTransport http, FakeRealtimeChannel rt)>
        NewInitializedAsync()
    {
        var http = new FakeHttpTransport();
        http.Responses.Enqueue(new HttpResult(200, "{\"gleapId\":\"g1\",\"gleapHash\":\"h1\"}"));
        http.Responses.Enqueue(new HttpResult(200, "{\"flowConfig\":{},\"projectActions\":{}}"));
        var ch = new FakeWebViewChannel();
        var rt = new FakeRealtimeChannel();
        var backend = new ManagedBackend(new ManagedBackend.Dependencies
        {
            Http = http,
            Json = new SystemTextJsonSerializer(),
            Store = new InMemoryKeyValueStore(),
            Channel = ch,
            Endpoints = GleapEndpoints.Default,
            Realtime = rt,
            SdkVersion = "9.9.9"
        });
        await backend.InitializeAsync("token-1", CancellationToken.None);
        return (backend, ch, http, rt);
    }

    [Fact]
    public async Task Realtime_ConnectsWithSessionIdentity_OnInit()
    {
        var (_, _, _, rt) = await NewInitializedAsync();

        Assert.Single(rt.ConnectUrls);
        var url = rt.ConnectUrls[0];
        Assert.StartsWith("wss://ws.gleap.io?", url);
        Assert.Contains("gleapId=g1", url);
        Assert.Contains("gleapHash=h1", url);
        Assert.Contains("apiKey=token-1", url);
        Assert.Contains("sdkVersion=9.9.9", url);
    }

    [Fact]
    public async Task Realtime_UpdateFrame_EmitsNotificationCount()
    {
        var (backend, _, _, rt) = await NewInitializedAsync();
        object? received = null;
        backend.RegisterListener("notificationCountUpdated", d => received = d);

        rt.Emit("{\"name\":\"update\",\"data\":{\"a\":[],\"u\":7}}");

        Assert.Equal(7, received);
    }

    [Fact]
    public async Task Realtime_UpdateFrame_EmitsOutboundSent()
    {
        var (backend, _, _, rt) = await NewInitializedAsync();
        object? received = null;
        backend.RegisterListener("outboundSent", d => received = d);

        rt.Emit("{\"name\":\"update\",\"data\":{\"a\":[{\"actionType\":\"notification\",\"outbound\":\"ob9\"}],\"u\":1}}");

        var json = JsonSerializer.Serialize(received);
        Assert.Contains("notification", json);
        Assert.Contains("ob9", json);
    }

    [Fact]
    public async Task Realtime_NonUpdateFrame_IsIgnored()
    {
        var (backend, _, _, rt) = await NewInitializedAsync();
        var fired = false;
        backend.RegisterListener("notificationCountUpdated", _ => fired = true);

        rt.Emit("{\"name\":\"checklist\",\"data\":{\"id\":\"cl1\"}}");
        rt.Emit("not even json");

        Assert.False(fired);
    }

    private const string ChecklistFrame = """
    {"name":"checklist","data":{
      "id":"cl-1","outboundId":"ob-1","status":"active",
      "completedStepsBefore":["s1"],"completedSteps":["s1","s2"],
      "steps":[{"id":"s1","title":"One"},{"id":"s2","title":"Two"},{"id":"s3","title":"Three"}]
    }}
    """;

    [Fact]
    public async Task Realtime_ChecklistFrame_EmitsUpdatedAndStepCompleted()
    {
        var (backend, _, _, rt) = await NewInitializedAsync();
        object? updated = null;
        var stepEvents = new List<object?>();
        backend.RegisterListener("checklistUpdated", d => updated = d);
        backend.RegisterListener("checklistStepCompleted", d => stepEvents.Add(d));

        rt.Emit(ChecklistFrame);

        Assert.Contains("cl-1", JsonSerializer.Serialize(updated));
        // Only s2 is newly completed — s1 was already done (the JS re-fire bug is not ported).
        var step = Assert.Single(stepEvents);
        var json = JsonSerializer.Serialize(step);
        Assert.Contains("s2", json);
        Assert.DoesNotContain("\"stepId\":\"s1\"", json);
    }

    [Fact]
    public async Task Realtime_ChecklistFrame_EmitsCompleted_OnlyWhenDone()
    {
        var (backend, _, _, rt) = await NewInitializedAsync();
        var completed = 0;
        backend.RegisterListener("checklistCompleted", _ => completed++);

        rt.Emit(ChecklistFrame);                                   // status active -> no completion
        Assert.Equal(0, completed);

        rt.Emit(ChecklistFrame.Replace("\"status\":\"active\"", "\"status\":\"done\""));
        Assert.Equal(1, completed);
    }

    [Fact]
    public async Task Realtime_ReconnectsOnClearIdentity()
    {
        var (backend, _, http, rt) = await NewInitializedAsync();
        http.Responses.Enqueue(new HttpResult(200, "{\"gleapId\":\"g2\",\"gleapHash\":\"h2\"}")); // new guest session

        await backend.ClearIdentityAsync(CancellationToken.None);

        Assert.Equal(2, rt.ConnectUrls.Count);
        Assert.Contains("gleapId=g2", rt.ConnectUrls[^1]);
    }

    [Fact]
    public async Task Init_LogsSessionStartedEvent()
    {
        var (backend, ch, http, _) = await NewInitializedAsync();
        ch.SimulateIncoming("{\"name\":\"ping\"}");
        http.Responses.Enqueue(new HttpResult(200, "{}"));

        await backend.PollOutboundOnceAsync(CancellationToken.None);

        Assert.Contains("sessionStarted", http.Calls[^1].Body);
    }

    [Fact]
    public async Task ClearIdentity_LogsAnotherSessionStarted()
    {
        var (backend, ch, http, _) = await NewInitializedAsync();
        ch.SimulateIncoming("{\"name\":\"ping\"}");
        http.Responses.Enqueue(new HttpResult(200, "{}"));
        await backend.PollOutboundOnceAsync(CancellationToken.None);   // flushes the first sessionStarted

        http.Responses.Enqueue(new HttpResult(200, "{\"gleapId\":\"g2\",\"gleapHash\":\"h2\"}"));
        await backend.ClearIdentityAsync(CancellationToken.None);
        http.Responses.Enqueue(new HttpResult(200, "{}"));
        await backend.PollOutboundOnceAsync(CancellationToken.None);

        Assert.Contains("sessionStarted", http.Calls[^1].Body);
    }

    [Fact]
    public async Task Ping_SetsWsFlagTrue_WhenRealtimeConnected()
    {
        var (backend, ch, http, rt) = await NewInitializedAsync();
        ch.SimulateIncoming("{\"name\":\"ping\"}");
        rt.IsConnected = true;
        http.Responses.Enqueue(new HttpResult(200, "{}"));

        await backend.PollOutboundOnceAsync(CancellationToken.None);

        Assert.Contains("\"ws\":true", http.Calls[^1].Body);
    }

    [Fact]
    public async Task Ping_DoesNotProcessResponse_WhileRealtimeConnected()
    {
        var (backend, ch, http, rt) = await NewInitializedAsync();
        ch.SimulateIncoming("{\"name\":\"ping\"}");

        // The socket delivers the real unread count...
        object? unread = null;
        backend.RegisterListener("notificationCountUpdated", d => unread = d);
        rt.IsConnected = true;
        rt.Emit("{\"name\":\"update\",\"data\":{\"a\":[],\"u\":7}}");
        Assert.Equal(7, unread);

        // ...and the ws-mode ping answers with an empty body. Processing it would emit 0 and hide the badge.
        http.Responses.Enqueue(new HttpResult(200, "{}"));
        await backend.PollOutboundOnceAsync(CancellationToken.None);

        Assert.Equal(7, unread);
    }

    [Fact]
    public async Task Ping_StillProcessesResponse_WhenRealtimeDisconnected()
    {
        var (backend, ch, http, rt) = await NewInitializedAsync();
        ch.SimulateIncoming("{\"name\":\"ping\"}");
        object? unread = null;
        backend.RegisterListener("notificationCountUpdated", d => unread = d);
        rt.IsConnected = false;   // poll is the fallback transport -> response carries the update
        http.Responses.Enqueue(new HttpResult(200, "{\"a\":[],\"u\":3}"));

        await backend.PollOutboundOnceAsync(CancellationToken.None);

        Assert.Equal(3, unread);
    }
}
