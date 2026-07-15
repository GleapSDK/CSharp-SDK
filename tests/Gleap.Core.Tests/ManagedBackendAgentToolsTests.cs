using Gleap.Core.Tests.Fakes;
using GleapSDK;
using GleapSDK.Http;
using GleapSDK.Serialization;
using GleapSDK.Session;

namespace Gleap.Core.Tests;

/// <summary>
/// The Frontend-tool execution round-trip: the widget posts <c>frontend-tool-execute</c>
/// { toolCallId, name, params }; the SDK runs the handler registered via
/// Gleap.RegisterAgentTool and posts <c>frontend-tool-result</c>
/// { toolCallId, name, result } back so the waiting agent can continue.
/// </summary>
public class ManagedBackendAgentToolsTests
{
    private static (ManagedBackend backend, FakeWebViewChannel ch) NewConnected()
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
        ch.SimulateIncoming("{\"name\":\"ping\"}"); // connect so Send() reaches the channel
        return (backend, ch);
    }

    private static string? LastToolResult(FakeWebViewChannel ch) =>
        ch.ExecutedScripts.LastOrDefault(s => s.Contains("frontend-tool-result"));

    [Fact]
    public void FrontendToolExecute_RunsRegisteredHandler_AndPostsResult()
    {
        var (backend, ch) = NewConnected();
        IReadOnlyDictionary<string, object?>? received = null;
        backend.RegisterAgentTool("send-money", parameters =>
        {
            received = parameters;
            return Task.FromResult<object?>("Transfer initiated.");
        });

        ch.SimulateIncoming(
            "{\"name\":\"frontend-tool-execute\",\"data\":{\"toolCallId\":\"call-1\",\"name\":\"send-money\",\"params\":{\"amount\":42,\"contact\":\"Ada\"}}}");

        var result = LastToolResult(ch);
        Assert.NotNull(result);
        Assert.Contains("\"toolCallId\":\"call-1\"", result);
        Assert.Contains("Transfer initiated.", result);
        Assert.NotNull(received);
        Assert.Equal("Ada", received!["contact"]);
        Assert.Equal(42L, received!["amount"]); // JSON numbers arrive as long/double, not JsonElement
    }

    [Fact]
    public void FrontendToolExecute_NoRegisteredHandler_PostsExplanatoryResult()
    {
        var (backend, ch) = NewConnected();

        ch.SimulateIncoming(
            "{\"name\":\"frontend-tool-execute\",\"data\":{\"toolCallId\":\"c2\",\"name\":\"unknown-tool\",\"params\":{}}}");

        var result = LastToolResult(ch);
        Assert.NotNull(result);
        Assert.Contains("\"toolCallId\":\"c2\"", result);
        Assert.Contains("No handler registered", result);
        Assert.Contains("unknown-tool", result);
    }

    [Fact]
    public void FrontendToolExecute_HandlerThrows_PostsFailureResult()
    {
        var (backend, ch) = NewConnected();
        backend.RegisterAgentTool("boom", _ => throw new System.InvalidOperationException("kaboom"));

        ch.SimulateIncoming(
            "{\"name\":\"frontend-tool-execute\",\"data\":{\"toolCallId\":\"c3\",\"name\":\"boom\",\"params\":{}}}");

        var result = LastToolResult(ch);
        Assert.NotNull(result);
        Assert.Contains("Tool execution failed", result);
        Assert.Contains("kaboom", result);
    }

    [Fact]
    public void FrontendToolExecute_ObjectResult_IsSerializedToJson()
    {
        var (backend, ch) = NewConnected();
        backend.RegisterAgentTool("lookup", _ =>
            Task.FromResult<object?>(new Dictionary<string, object?> { ["status"] = "ok", ["id"] = 7 }));

        ch.SimulateIncoming(
            "{\"name\":\"frontend-tool-execute\",\"data\":{\"toolCallId\":\"c4\",\"name\":\"lookup\",\"params\":{}}}");

        var result = LastToolResult(ch);
        Assert.NotNull(result);
        // The object result is JSON-stringified, then embedded as the string "result" field.
        Assert.Contains("status", result);
        Assert.Contains("ok", result);
    }

    [Fact]
    public void FrontendToolExecute_EmptyResult_PostsCompletedFallback()
    {
        var (backend, ch) = NewConnected();
        backend.RegisterAgentTool("silent", _ => Task.FromResult<object?>(""));

        ch.SimulateIncoming(
            "{\"name\":\"frontend-tool-execute\",\"data\":{\"toolCallId\":\"c5\",\"name\":\"silent\",\"params\":{}}}");

        var result = LastToolResult(ch);
        Assert.NotNull(result);
        Assert.Contains("completed without returning a result", result);
    }

    [Fact]
    public void FrontendToolExecute_DuplicateToolCallId_RunsHandlerOnce()
    {
        var (backend, ch) = NewConnected();
        var calls = 0;
        backend.RegisterAgentTool("dedupe", _ =>
        {
            calls++;
            return Task.FromResult<object?>("done");
        });

        var msg = "{\"name\":\"frontend-tool-execute\",\"data\":{\"toolCallId\":\"same\",\"name\":\"dedupe\",\"params\":{}}}";
        ch.SimulateIncoming(msg);
        ch.SimulateIncoming(msg);

        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task FrontendToolExecute_AwaitsAsyncHandler()
    {
        var (backend, ch) = NewConnected();
        var gate = new TaskCompletionSource<object?>();
        backend.RegisterAgentTool("slow", _ => gate.Task);

        ch.SimulateIncoming(
            "{\"name\":\"frontend-tool-execute\",\"data\":{\"toolCallId\":\"c6\",\"name\":\"slow\",\"params\":{}}}");

        // Handler has not returned yet -> nothing posted.
        Assert.Null(LastToolResult(ch));

        gate.SetResult("late result");
        await Task.Delay(20);

        var result = LastToolResult(ch);
        Assert.NotNull(result);
        Assert.Contains("late result", result);
    }

    [Fact]
    public void ToolExecutionNotification_EmitsEventWithPayload()
    {
        var (backend, ch) = NewConnected();
        object? payload = null;
        var fired = false;
        backend.RegisterListener("toolExecution", data => { fired = true; payload = data; });

        ch.SimulateIncoming(
            "{\"name\":\"tool-execution\",\"data\":{\"name\":\"some-tool\",\"params\":{\"a\":1}}}");

        Assert.True(fired);
        Assert.NotNull(payload); // regression: the payload used to be dropped
        Assert.Contains("some-tool", payload!.ToString());
    }
}
