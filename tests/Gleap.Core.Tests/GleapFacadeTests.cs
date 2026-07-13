using GleapSDK;
using GleapFacade = GleapSDK.Gleap;

namespace Gleap.Core.Tests;

public class GleapFacadeTests
{
    private sealed class FakeBackend : IGleapBackend
    {
        public bool OpenCalled { get; private set; }
        public string? StartedBotId { get; private set; }
        public bool OpenNewsCalled { get; private set; }

        public Task InitializeAsync(string token, CancellationToken ct) => Task.CompletedTask;
        public void Open() => OpenCalled = true;
        public void Close() { }
        public void StartConversation(bool showBackButton) { }
        public void StartBot(string botId, bool showBackButton) => StartedBotId = botId;
        public void OpenConversation(string shareToken) { }
        public void OpenHelpCenter(bool showBackButton) { }
        public void OpenNews(bool showBackButton) => OpenNewsCalled = true;
        public void ShowSurvey(string surveyId, SurveyFormat format) { }
    }

    [Fact]
    public void CallBeforeUseBackend_Throws()
    {
        GleapFacade.ResetForTest();

        Assert.Throws<InvalidOperationException>(() => GleapFacade.Open());
    }

    [Fact]
    public void AfterUseBackend_DelegatesToBackend()
    {
        GleapFacade.ResetForTest();
        var fake = new FakeBackend();
        GleapFacade.UseBackend(fake);

        GleapFacade.OpenNews();
        GleapFacade.StartBot("b");
        GleapFacade.Open();

        Assert.True(fake.OpenNewsCalled);
        Assert.Equal("b", fake.StartedBotId);
        Assert.True(fake.OpenCalled);
    }
}
