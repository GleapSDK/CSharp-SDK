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
