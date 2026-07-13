using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace GleapSDK;

/// <summary>
/// Public entry point. Platform packages create the backend (managed or native-bridge)
/// and attach it via <see cref="UseBackend"/>; app code then calls the static API.
/// </summary>
public static class Gleap
{
    private static IGleapBackend? _backend;

    private static IGleapBackend Backend =>
        _backend ?? throw new System.InvalidOperationException(
            "Gleap backend not attached. A platform package must call Gleap.UseBackend(...).");

    /// <summary>Attach the platform backend. Called by platform packages, not app code.</summary>
    public static void UseBackend(IGleapBackend backend) => _backend = backend;

    public static Task InitializeAsync(string token, CancellationToken ct = default) =>
        Backend.InitializeAsync(token, ct);

    public static void Open() => Backend.Open();
    public static void Close() => Backend.Close();
    public static void StartConversation(bool showBackButton = true) => Backend.StartConversation(showBackButton);
    public static void StartBot(string botId, bool showBackButton = true) => Backend.StartBot(botId, showBackButton);
    public static void OpenConversation(string shareToken) => Backend.OpenConversation(shareToken);
    public static void OpenHelpCenter(bool showBackButton = true) => Backend.OpenHelpCenter(showBackButton);
    public static void OpenNews(bool showBackButton = true) => Backend.OpenNews(showBackButton);
    public static void ShowSurvey(string surveyId, SurveyFormat format = SurveyFormat.Survey) => Backend.ShowSurvey(surveyId, format);
    public static void OpenConversations(bool showBackButton = true) => Backend.OpenConversations(showBackButton);
    public static void StartClassicForm(string formId, bool showBackButton = true) => Backend.StartClassicForm(formId, showBackButton);
    public static void OpenHelpCenterArticle(string articleId, bool showBackButton = true) => Backend.OpenHelpCenterArticle(articleId, showBackButton);
    public static void OpenHelpCenterCollection(string collectionId, bool showBackButton = true) => Backend.OpenHelpCenterCollection(collectionId, showBackButton);
    public static void SearchHelpCenter(string term, bool showBackButton = true) => Backend.SearchHelpCenter(term, showBackButton);
    public static void OpenNewsArticle(string articleId, bool showBackButton = true) => Backend.OpenNewsArticle(articleId, showBackButton);
    public static void OpenFeatureRequests(bool showBackButton = true) => Backend.OpenFeatureRequests(showBackButton);
    public static void OpenChecklists(bool showBackButton = true) => Backend.OpenChecklists(showBackButton);
    public static void OpenChecklist(string checklistId, bool showBackButton = true) => Backend.OpenChecklist(checklistId, showBackButton);
    public static void StartChecklist(string outboundId, bool showBackButton = true) => Backend.StartChecklist(outboundId, showBackButton);
    public static void AskAI(string question, bool showBackButton = true) => Backend.AskAI(question, showBackButton);
    public static Task IdentifyContactAsync(string userId, Models.GleapUserProperty? properties = null, string? userHash = null, CancellationToken ct = default) => Backend.IdentifyContactAsync(userId, properties, userHash, ct);
    public static Task UpdateContactAsync(Models.GleapUserProperty properties, CancellationToken ct = default) => Backend.UpdateContactAsync(properties, ct);
    public static Task ClearIdentityAsync(CancellationToken ct = default) => Backend.ClearIdentityAsync(ct);
    public static bool IsUserIdentified() => Backend.IsUserIdentified();
    public static Models.GleapUserProperty? GetIdentity() => Backend.GetIdentity();

    public static void Log(string message, LogLevel level = LogLevel.Info) => Backend.Log(message, level);
    public static void TrackEvent(string name, object? data = null) => Backend.TrackEvent(name, data);
    public static void TrackPage(string pageName) => Backend.TrackPage(pageName);
    public static void SetCustomData(string key, string value) => Backend.SetCustomData(key, value);
    public static void AttachCustomData(IReadOnlyDictionary<string, object> data) => Backend.AttachCustomData(data);
    public static void RemoveCustomDataForKey(string key) => Backend.RemoveCustomDataForKey(key);
    public static void ClearCustomData() => Backend.ClearCustomData();
    public static void SetTicketAttribute(string key, object value) => Backend.SetTicketAttribute(key, value);
    public static void UnsetTicketAttribute(string key) => Backend.UnsetTicketAttribute(key);
    public static void ClearTicketAttributes() => Backend.ClearTicketAttributes();
    public static void SetTags(string[] tags) => Backend.SetTags(tags);
    public static void AddAttachment(string base64File, string fileName) => Backend.AddAttachment(base64File, fileName);
    public static void RemoveAllAttachments() => Backend.RemoveAllAttachments();
    public static void SetNetworkLogsBlacklist(string[] blacklist) => Backend.SetNetworkLogsBlacklist(blacklist);
    public static void SetNetworkLogPropsToIgnore(string[] propsToIgnore) => Backend.SetNetworkLogPropsToIgnore(propsToIgnore);
    public static Task SendSilentCrashReportAsync(string description, Severity severity, IReadOnlyDictionary<string, object>? excludeData = null, CancellationToken ct = default) => Backend.SendSilentCrashReportAsync(description, severity, excludeData, ct);

    /// <summary>Test-only reset so xUnit cases don't leak backend state.</summary>
    internal static void ResetForTest() => _backend = null;
}
