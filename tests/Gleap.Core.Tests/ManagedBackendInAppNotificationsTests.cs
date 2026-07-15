using Gleap.Core.Tests.Fakes;
using GleapSDK;
using GleapSDK.Http;
using GleapSDK.Serialization;
using GleapSDK.Session;

namespace Gleap.Core.Tests;

public class ManagedBackendInAppNotificationsTests
{
    private static (ManagedBackend backend, FakeWebViewChannel ch, FakeHttpTransport http) NewInitialized()
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
        ch.SimulateIncoming("{\"name\":\"ping\"}");
        return (backend, ch, http);
    }

    private static List<string> CaptureActionTypes(ManagedBackend backend)
    {
        var seen = new List<string>();
        backend.RegisterListener("outboundSent", data =>
        {
            if (data is IReadOnlyDictionary<string, object?> d && d.TryGetValue("actionType", out var at))
            {
                seen.Add(at as string ?? string.Empty);
            }
        });
        return seen;
    }

    [Fact]
    public async Task Disabled_SuppressesNotificationAction()
    {
        var (backend, _, http) = NewInitialized();
        backend.SetDisableInAppNotifications(true);
        var seen = CaptureActionTypes(backend);
        http.Responses.Enqueue(new HttpResult(200,
            "{\"a\":[{\"actionType\":\"notification\",\"outbound\":\"n1\"}],\"u\":0}"));

        await backend.PollOutboundOnceAsync(CancellationToken.None);

        Assert.DoesNotContain("notification", seen);
    }

    [Fact]
    public async Task Disabled_StillDeliversBannerAndSurvey()
    {
        var (backend, _, http) = NewInitialized();
        backend.SetDisableInAppNotifications(true);
        var seen = CaptureActionTypes(backend);
        http.Responses.Enqueue(new HttpResult(200,
            "{\"a\":[{\"actionType\":\"banner\",\"outbound\":\"b1\"}," +
            "{\"actionType\":\"survey123\",\"outbound\":\"s1\"}],\"u\":0}"));

        await backend.PollOutboundOnceAsync(CancellationToken.None);

        Assert.Contains("banner", seen);
        Assert.Contains("survey123", seen);
    }

    [Fact]
    public async Task Enabled_ByDefault_DeliversNotificationAction()
    {
        var (backend, _, http) = NewInitialized();
        var seen = CaptureActionTypes(backend);
        http.Responses.Enqueue(new HttpResult(200,
            "{\"a\":[{\"actionType\":\"notification\",\"outbound\":\"n1\"}],\"u\":0}"));

        await backend.PollOutboundOnceAsync(CancellationToken.None);

        Assert.Contains("notification", seen);
    }
}
