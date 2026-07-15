using GleapSDK.Omnis;

namespace Gleap.Omnis.Tests;

/// <summary>
/// Verifies the COM facade's defensive behaviour: because Omnis drives it over COM (where an unhandled
/// exception surfaces as an HRESULT and can destabilise the host), every member must be safe to call before
/// <see cref="GleapOmnisClient.Initialize"/> and must never throw on malformed input. Full behaviour
/// (network, realtime, the OBrowser round-trip) is exercised on Windows/Omnis; these tests pin the guards
/// and the transport queues that run anywhere.
/// </summary>
public sealed class GleapOmnisClientTests
{
    [Fact]
    public void BeforeInitialize_QueriesReturnSafeDefaults()
    {
        using var client = new GleapOmnisClient();

        Assert.False(client.IsReady());
        Assert.False(client.IsOpened());
        Assert.False(client.IsUserIdentified());
        Assert.False(client.IsFeedbackButtonVisible());
        Assert.Equal(string.Empty, client.FeedbackButtonPosition());
        Assert.Equal(string.Empty, client.GetIdentityJson());
        Assert.Equal(string.Empty, client.DequeueWidgetMessage());
        Assert.Equal(string.Empty, client.DequeueAppEvent());
    }

    [Fact]
    public void ConfigurationSetters_DoNotThrow()
    {
        using var client = new GleapOmnisClient();

        var ex = Record.Exception(() =>
        {
            client.SetApiUrl("https://api.example.com");
            client.SetFrameUrl("https://frame.example.com");
            client.SetWsUrl("wss://ws.example.com");
            client.SetAppIdentity("My Omnis App", "2.4.1");
            client.SetWindowHandle(123456);
        });

        Assert.Null(ex);
    }

    [Fact]
    public void FacadeCalls_BeforeInitialize_AreNoOpsAndDoNotThrow()
    {
        using var client = new GleapOmnisClient();

        var ex = Record.Exception(() =>
        {
            client.Open();
            client.Close();
            client.StartBot("bot-1", true);
            client.OpenHelpCenterArticle("a-1", false);
            client.ShowSurvey("s-1", 1);
            client.AskAI("hello?", true);
            client.Log("hi", 2);
            client.TrackEvent("evt", "{\"a\":1}");
            client.TrackPage("Home");
            client.SetCustomData("k", "v");
            client.SetTicketAttribute("prio", "high");
            client.IdentifyContact("u-1", "{\"email\":\"a@b.c\"}", "");
            client.CaptureScreenshot();
            client.CheckOutbound();
        });

        Assert.Null(ex);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("")]
    [InlineData("{ broken")]
    public void MalformedJsonArguments_AreToleratedNotThrown(string json)
    {
        using var client = new GleapOmnisClient();

        var ex = Record.Exception(() =>
        {
            client.AttachCustomData(json);
            client.SetTags(json);
            client.PreFillForm(json);
            client.SetAiTools(json);
            client.IdentifyContact("u-1", json, "");
            client.SetNetworkLogsBlacklist(json);
        });

        Assert.Null(ex);
    }

    [Fact]
    public void PushMessage_BeforeInitialize_DoesNotThrow()
    {
        using var client = new GleapOmnisClient();

        var ex = Record.Exception(() => client.PushMessage("{\"name\":\"ping\"}"));

        Assert.Null(ex);
    }

    [Fact]
    public void ShutdownAndDispose_AreSafeBeforeInitialize()
    {
        var client = new GleapOmnisClient();

        var ex = Record.Exception(() =>
        {
            client.Shutdown();
            client.Dispose();
        });

        Assert.Null(ex);
    }
}
