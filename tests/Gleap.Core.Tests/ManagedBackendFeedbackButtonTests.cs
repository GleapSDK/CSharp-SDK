using Gleap.Core.Tests.Fakes;
using GleapSDK;
using GleapSDK.Http;
using GleapSDK.Serialization;
using GleapSDK.Session;

namespace Gleap.Core.Tests;

public class ManagedBackendFeedbackButtonTests
{
    private static ManagedBackend NewInitialized(string flowConfigJson)
    {
        var http = new FakeHttpTransport();
        http.Responses.Enqueue(new HttpResult(200, "{\"gleapId\":\"g1\",\"gleapHash\":\"h1\"}"));
        http.Responses.Enqueue(new HttpResult(200,
            "{\"flowConfig\":" + flowConfigJson + ",\"projectActions\":{}}"));
        var backend = new ManagedBackend(new ManagedBackend.Dependencies
        {
            Http = http,
            Json = new SystemTextJsonSerializer(),
            Store = new InMemoryKeyValueStore(),
            Channel = new FakeWebViewChannel(),
            Endpoints = GleapEndpoints.Default
        });
        backend.InitializeAsync("token-1", CancellationToken.None).GetAwaiter().GetResult();
        return backend;
    }

    [Fact]
    public void DefaultsToVisible_WhenConfigHasNoPosition()
    {
        var backend = NewInitialized("{}");
        Assert.True(backend.IsFeedbackButtonVisible);
        Assert.Equal(string.Empty, backend.FeedbackButtonPosition);
    }

    [Fact]
    public void VisibleForNormalPosition_AndExposesPosition()
    {
        var backend = NewInitialized("{\"feedbackButtonPosition\":\"BOTTOM_RIGHT\"}");
        Assert.True(backend.IsFeedbackButtonVisible);
        Assert.Equal("BOTTOM_RIGHT", backend.FeedbackButtonPosition);
    }

    [Fact]
    public void HiddenByConfig_WhenPositionIsButtonHide()
    {
        var backend = NewInitialized("{\"feedbackButtonPosition\":\"BUTTON_HIDE\"}");
        Assert.False(backend.IsFeedbackButtonVisible);
        Assert.Equal("BUTTON_HIDE", backend.FeedbackButtonPosition);
    }

    [Fact]
    public void ShowFeedbackButton_False_HidesAndEmitsOnce()
    {
        var backend = NewInitialized("{\"feedbackButtonPosition\":\"BOTTOM_RIGHT\"}");
        var events = new List<object?>();
        backend.RegisterListener("feedbackButtonVisibilityUpdated", events.Add);

        backend.ShowFeedbackButton(false);

        Assert.False(backend.IsFeedbackButtonVisible);
        Assert.Single(events);
        Assert.Equal(false, events[0]);
    }

    [Fact]
    public void ShowFeedbackButton_True_OverridesConfigHide()
    {
        var backend = NewInitialized("{\"feedbackButtonPosition\":\"BUTTON_HIDE\"}");
        var events = new List<object?>();
        backend.RegisterListener("feedbackButtonVisibilityUpdated", events.Add);

        backend.ShowFeedbackButton(true);

        Assert.True(backend.IsFeedbackButtonVisible);
        Assert.Single(events);
        Assert.Equal(true, events[0]);
    }

    [Fact]
    public void ShowFeedbackButton_NoEvent_WhenEffectiveVisibilityUnchanged()
    {
        var backend = NewInitialized("{\"feedbackButtonPosition\":\"BOTTOM_RIGHT\"}");
        var events = new List<object?>();
        backend.RegisterListener("feedbackButtonVisibilityUpdated", events.Add);

        backend.ShowFeedbackButton(true);   // already effectively visible → no change

        Assert.True(backend.IsFeedbackButtonVisible);
        Assert.Empty(events);
    }
}
