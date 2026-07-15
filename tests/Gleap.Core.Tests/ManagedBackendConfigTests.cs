using Gleap.Core.Tests.Fakes;
using GleapSDK;
using GleapSDK.Http;
using GleapSDK.Serialization;
using GleapSDK.Session;

namespace Gleap.Core.Tests;

public class ManagedBackendConfigTests
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

    /// <summary>Like <see cref="NewInitialized"/>, but also hands back the transport and lets the caller
    /// choose the flowConfig the project returns.</summary>
    private static (ManagedBackend backend, FakeWebViewChannel ch, FakeHttpTransport http) NewInitializedWithHttp(
        string flowConfig = "{}")
    {
        var http = new FakeHttpTransport();
        http.Responses.Enqueue(new HttpResult(200, "{\"gleapId\":\"g1\",\"gleapHash\":\"h1\"}"));
        http.Responses.Enqueue(new HttpResult(200, $"{{\"flowConfig\":{flowConfig},\"projectActions\":{{}}}}"));
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
        return (backend, ch, http);
    }

    [Fact]
    public void IsOpened_ReflectsOpenClose()
    {
        var (backend, ch) = NewInitialized();
        ch.SimulateIncoming("{\"name\":\"ping\"}");

        backend.Open();
        Assert.True(backend.IsOpened());

        backend.Close();
        Assert.False(backend.IsOpened());
    }

    [Fact]
    public void ConfigSetters_AfterInit_DoNotThrow()
    {
        var (backend, ch) = NewInitialized();
        ch.SimulateIncoming("{\"name\":\"ping\"}");

        backend.SetLanguage("de");
        backend.ShowFeedbackButton(true);
        backend.SetDisableInAppNotifications(true);
        backend.PreFillForm(new Dictionary<string, object?> { ["email"] = "a@b.c" });
        backend.StartNetworkLogging();
        backend.StopNetworkLogging();
        backend.EnableDebugConsoleLog();
        backend.DisableConsoleLog();
    }

    [Fact]
    public async Task DefaultLanguage_ComesFromTheOs_NotHardcodedEnglish()
    {
        var previous = System.Globalization.CultureInfo.CurrentUICulture;
        try
        {
            System.Globalization.CultureInfo.CurrentUICulture = new System.Globalization.CultureInfo("de-DE");
            var http = new FakeHttpTransport();
            http.Responses.Enqueue(new HttpResult(200, "{\"gleapId\":\"g1\",\"gleapHash\":\"h1\"}"));
            http.Responses.Enqueue(new HttpResult(200, "{\"flowConfig\":{},\"projectActions\":{}}"));
            var backend = new ManagedBackend(new ManagedBackend.Dependencies
            {
                Http = http,
                Json = new SystemTextJsonSerializer(),
                Store = new InMemoryKeyValueStore(),
                Channel = new FakeWebViewChannel(),
                Endpoints = GleapEndpoints.Default
            });

            await backend.InitializeAsync("t", CancellationToken.None);

            Assert.Contains("\"lang\":\"de\"", http.Calls[0].Body);        // POST /sessions
            Assert.Contains("lang=de", http.Calls[1].Url);                 // GET /config?lang=
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentUICulture = previous;
        }
    }

    [Fact]
    public async Task SetLanguage_AfterInit_ReloadsConfigAndPushesItToTheWidget()
    {
        var (backend, ch, http) = NewInitializedWithHttp();
        ch.SimulateIncoming("{\"name\":\"ping\"}");
        http.Responses.Enqueue(new HttpResult(200, "{\"flowConfig\":{\"color\":\"#111111\"},\"projectActions\":{}}"));

        backend.SetLanguage("fr");
        await backend.LastLanguageTask!;

        Assert.Contains("lang=fr", http.Calls[^1].Url);
        var configUpdate = ch.ExecutedScripts.Last(s => s.Contains("config-update"));
        Assert.Contains("fr", configUpdate);
        Assert.Contains("#111111", configUpdate);
    }

    [Fact]
    public void RemoteConfig_TurnsNetworkLoggingOff_WhenProjectDisablesIt()
    {
        var (backend, _, _) = NewInitializedWithHttp("{\"enableNetworkLogs\":false}");

        Assert.False(backend.NetworkLogEnabled);
    }

    [Fact]
    public void RemoteConfig_LeavesNetworkLoggingOn_ByDefault()
    {
        var (backend, _, _) = NewInitializedWithHttp();

        Assert.True(backend.NetworkLogEnabled);
    }

    [Fact]
    public void ShowFeedbackButton_IsVisibleByDefault_AndRaisesOnChange()
    {
        var (backend, _) = NewInitialized();
        Assert.True(backend.IsFeedbackButtonVisible);
        object? raised = null;
        backend.RegisterListener("feedbackButtonVisibilityChanged", v => raised = v);

        backend.ShowFeedbackButton(false);

        Assert.False(backend.IsFeedbackButtonVisible);
        Assert.Equal(false, raised);
    }

    [Fact]
    public void SetDisableInAppNotifications_IsObservable()
    {
        var (backend, _) = NewInitialized();
        Assert.False(backend.InAppNotificationsDisabled);

        backend.SetDisableInAppNotifications(true);

        Assert.True(backend.InAppNotificationsDisabled);
    }

    [Fact]
    public void PreFillForm_AfterConnected_SendsPrefillData()
    {
        var (backend, ch) = NewInitialized();
        ch.SimulateIncoming("{\"name\":\"ping\"}");

        backend.PreFillForm(new Dictionary<string, object?> { ["email"] = "a@b.c" });

        Assert.Contains(ch.ExecutedScripts, s => s.Contains("prefill-form-data") && s.Contains("a@b.c"));
    }
}
