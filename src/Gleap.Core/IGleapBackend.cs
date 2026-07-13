using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GleapSDK.Models;

namespace GleapSDK;

/// <summary>
/// The swappable engine behind the <see cref="Gleap"/> facade.
/// SP-0 provides <c>ManagedBackend</c>; platform packages later add native-bridge backends.
/// Only the surface needed for SP-0 Part 1 is declared; it grows in later plans.
/// </summary>
public interface IGleapBackend
{
    Task InitializeAsync(string token, CancellationToken ct);
    void RegisterListener(string eventName, System.Action<object?> handler);
    Task PollOutboundOnceAsync(CancellationToken ct);
    void Open();
    void Close();
    void StartConversation(bool showBackButton);
    void StartBot(string botId, bool showBackButton);
    void OpenConversation(string shareToken);
    void OpenHelpCenter(bool showBackButton);
    void OpenNews(bool showBackButton);
    void ShowSurvey(string surveyId, SurveyFormat format);
    void OpenConversations(bool showBackButton);
    void StartClassicForm(string formId, bool showBackButton);
    void OpenHelpCenterArticle(string articleId, bool showBackButton);
    void OpenHelpCenterCollection(string collectionId, bool showBackButton);
    void SearchHelpCenter(string term, bool showBackButton);
    void OpenNewsArticle(string articleId, bool showBackButton);
    void OpenFeatureRequests(bool showBackButton);
    void OpenChecklists(bool showBackButton);
    void OpenChecklist(string checklistId, bool showBackButton);
    void StartChecklist(string outboundId, bool showBackButton);
    void AskAI(string question, bool showBackButton);
    Task IdentifyContactAsync(string userId, GleapUserProperty? properties, string? userHash, CancellationToken ct);
    Task UpdateContactAsync(GleapUserProperty properties, CancellationToken ct);
    Task ClearIdentityAsync(CancellationToken ct);
    bool IsUserIdentified();
    GleapUserProperty? GetIdentity();
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
    Task SendSilentCrashReportAsync(string description, Severity severity, IReadOnlyDictionary<string, object>? excludeData, CancellationToken ct);
    void SetLanguage(string language);
    bool IsOpened();
    void ShowFeedbackButton(bool visible);
    void SetDisableInAppNotifications(bool disable);
    void PreFillForm(IReadOnlyDictionary<string, object?> formData);
    void StartNetworkLogging();
    void StopNetworkLogging();
    void EnableDebugConsoleLog();
    void DisableConsoleLog();
    void SetActivationMethods(ActivationMethod[] activationMethods);
}
