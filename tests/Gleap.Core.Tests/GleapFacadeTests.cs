using GleapSDK;
using GleapSDK.Models;
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
        public void RegisterListener(string eventName, Action<object?> handler) { }
        public void Open() => OpenCalled = true;
        public void Close() { }
        public void StartConversation(bool showBackButton) { }
        public void StartBot(string botId, bool showBackButton) => StartedBotId = botId;
        public void OpenConversation(string shareToken) { }
        public void OpenHelpCenter(bool showBackButton) { }
        public void OpenNews(bool showBackButton) => OpenNewsCalled = true;
        public void ShowSurvey(string surveyId, SurveyFormat format) { }
        public void OpenConversations(bool showBackButton) { }
        public void StartClassicForm(string formId, bool showBackButton) { }
        public void OpenHelpCenterArticle(string articleId, bool showBackButton) { }
        public void OpenHelpCenterCollection(string collectionId, bool showBackButton) { }
        public void SearchHelpCenter(string term, bool showBackButton) { }
        public void OpenNewsArticle(string articleId, bool showBackButton) { }
        public void OpenFeatureRequests(bool showBackButton) { }
        public void OpenChecklists(bool showBackButton) { }
        public void OpenChecklist(string checklistId, bool showBackButton) { }
        public void StartChecklist(string outboundId, bool showBackButton) { }
        public void AskAI(string question, bool showBackButton) { }
        public Task IdentifyContactAsync(string userId, GleapUserProperty? properties, string? userHash, CancellationToken ct) => Task.CompletedTask;
        public Task UpdateContactAsync(GleapUserProperty properties, CancellationToken ct) => Task.CompletedTask;
        public Task ClearIdentityAsync(CancellationToken ct) => Task.CompletedTask;
        public bool IsUserIdentified() => false;
        public GleapUserProperty? GetIdentity() => null;
        public void Log(string message, LogLevel level) { }
        public void TrackEvent(string name, object? data) { }
        public void TrackPage(string pageName) { }
        public void SetCustomData(string key, string value) { }
        public void AttachCustomData(IReadOnlyDictionary<string, object> data) { }
        public void RemoveCustomDataForKey(string key) { }
        public void ClearCustomData() { }
        public void SetTicketAttribute(string key, object value) { }
        public void UnsetTicketAttribute(string key) { }
        public void ClearTicketAttributes() { }
        public void SetTags(string[] tags) { }
        public void AddAttachment(string base64File, string fileName) { }
        public void RemoveAllAttachments() { }
        public void SetNetworkLogsBlacklist(string[] blacklist) { }
        public void SetNetworkLogPropsToIgnore(string[] propsToIgnore) { }
        public Task SendSilentCrashReportAsync(string description, Severity severity, IReadOnlyDictionary<string, object>? excludeData, CancellationToken ct) => Task.CompletedTask;
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
