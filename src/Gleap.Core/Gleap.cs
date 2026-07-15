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

    /// <summary>Initializes the SDK with your project's SDK key and loads the session and config.</summary>
    public static Task InitializeAsync(string token, CancellationToken ct = default) =>
        Backend.InitializeAsync(token, ct);

    /// <summary>Subscribes to a Gleap event (e.g. <c>initialized</c>, <c>open</c>, <c>close</c>, <c>feedback-sent</c>, <c>customActionTriggered</c>).</summary>
    public static void RegisterListener(string eventName, System.Action<object?> handler) => Backend.RegisterListener(eventName, handler);

    /// <summary>Removes a previously registered event listener.</summary>
    public static void RemoveListener(string eventName, System.Action<object?> handler) => Backend.RemoveListener(eventName, handler);

    /// <summary>Manually runs a single outbound poll cycle (surveys, banners, modals, news).</summary>
    public static Task CheckOutboundAsync(CancellationToken ct = default) => Backend.PollOutboundOnceAsync(ct);

    /// <summary>Opens the Gleap messenger.</summary>
    public static void Open() => Backend.Open();

    /// <summary>Closes the Gleap messenger.</summary>
    public static void Close() => Backend.Close();

    /// <summary>Starts a new conversation with the default bot (or live support if no bot is configured).</summary>
    public static void StartConversation(bool showBackButton = true) => Backend.StartConversation(showBackButton);

    /// <summary>Starts a conversation with a specific bot.</summary>
    public static void StartBot(string botId, bool showBackButton = true) => Backend.StartBot(botId, showBackButton);

    /// <summary>Opens an existing conversation by its share token.</summary>
    public static void OpenConversation(string shareToken) => Backend.OpenConversation(shareToken);

    /// <summary>Opens the help center.</summary>
    public static void OpenHelpCenter(bool showBackButton = true) => Backend.OpenHelpCenter(showBackButton);

    /// <summary>Opens the news overview.</summary>
    public static void OpenNews(bool showBackButton = true) => Backend.OpenNews(showBackButton);

    /// <summary>Manually shows a survey by id, in the given <paramref name="format"/>.</summary>
    public static void ShowSurvey(string surveyId, SurveyFormat format = SurveyFormat.Survey) => Backend.ShowSurvey(surveyId, format);

    /// <summary>Opens the conversations overview.</summary>
    public static void OpenConversations(bool showBackButton = true) => Backend.OpenConversations(showBackButton);

    /// <summary>Starts a classic feedback form / feedback flow by its id.</summary>
    public static void StartClassicForm(string formId, bool showBackButton = true) => Backend.StartClassicForm(formId, showBackButton);

    /// <summary>Opens a specific help center article.</summary>
    public static void OpenHelpCenterArticle(string articleId, bool showBackButton = true) => Backend.OpenHelpCenterArticle(articleId, showBackButton);

    /// <summary>Opens a specific help center collection.</summary>
    public static void OpenHelpCenterCollection(string collectionId, bool showBackButton = true) => Backend.OpenHelpCenterCollection(collectionId, showBackButton);

    /// <summary>Opens the help center with a search term prefilled.</summary>
    public static void SearchHelpCenter(string term, bool showBackButton = true) => Backend.SearchHelpCenter(term, showBackButton);

    /// <summary>Opens a specific news article.</summary>
    public static void OpenNewsArticle(string articleId, bool showBackButton = true) => Backend.OpenNewsArticle(articleId, showBackButton);

    /// <summary>Opens the feature requests board.</summary>
    public static void OpenFeatureRequests(bool showBackButton = true) => Backend.OpenFeatureRequests(showBackButton);

    /// <summary>Opens the checklists overview.</summary>
    public static void OpenChecklists(bool showBackButton = true) => Backend.OpenChecklists(showBackButton);

    /// <summary>Opens a specific checklist.</summary>
    public static void OpenChecklist(string checklistId, bool showBackButton = true) => Backend.OpenChecklist(checklistId, showBackButton);

    /// <summary>Starts a checklist from an outbound id.</summary>
    public static void StartChecklist(string outboundId, bool showBackButton = true) => Backend.StartChecklist(outboundId, showBackButton);

    /// <summary>Opens the AI assistant with a question prefilled.</summary>
    public static void AskAI(string question, bool showBackButton = true) => Backend.AskAI(question, showBackButton);

    /// <summary>Identifies the current user, optionally with <paramref name="properties"/> and a verification <paramref name="userHash"/>.</summary>
    public static Task IdentifyContactAsync(string userId, Models.GleapUserProperty? properties = null, string? userHash = null, CancellationToken ct = default) => Backend.IdentifyContactAsync(userId, properties, userHash, ct);

    /// <summary>Updates properties of the currently identified user.</summary>
    public static Task UpdateContactAsync(Models.GleapUserProperty properties, CancellationToken ct = default) => Backend.UpdateContactAsync(properties, ct);

    /// <summary>Clears the current user identity and ends the session (logout).</summary>
    public static Task ClearIdentityAsync(CancellationToken ct = default) => Backend.ClearIdentityAsync(ct);

    /// <summary>Returns whether a user is currently identified.</summary>
    public static bool IsUserIdentified() => Backend.IsUserIdentified();

    /// <summary>Returns the currently identified user's properties, or <c>null</c> if none.</summary>
    public static Models.GleapUserProperty? GetIdentity() => Backend.GetIdentity();

    /// <summary>Adds a message to the SDK console log that is attached to reports.</summary>
    public static void Log(string message, LogLevel level = LogLevel.Info) => Backend.Log(message, level);

    /// <summary>Tracks a custom event with optional structured <paramref name="data"/>.</summary>
    public static void TrackEvent(string name, object? data = null) => Backend.TrackEvent(name, data);

    /// <summary>Tracks a page / screen view.</summary>
    public static void TrackPage(string pageName) => Backend.TrackPage(pageName);

    /// <summary>Sets a single custom-data key/value attached to reports.</summary>
    public static void SetCustomData(string key, string value) => Backend.SetCustomData(key, value);

    /// <summary>Merges a dictionary of custom data attached to reports.</summary>
    public static void AttachCustomData(IReadOnlyDictionary<string, object> data) => Backend.AttachCustomData(data);

    /// <summary>Removes a single custom-data key.</summary>
    public static void RemoveCustomDataForKey(string key) => Backend.RemoveCustomDataForKey(key);

    /// <summary>Clears all custom data.</summary>
    public static void ClearCustomData() => Backend.ClearCustomData();

    /// <summary>Sets a ticket attribute sent with the next created ticket.</summary>
    public static void SetTicketAttribute(string key, object value) => Backend.SetTicketAttribute(key, value);

    /// <summary>Removes a ticket attribute.</summary>
    public static void UnsetTicketAttribute(string key) => Backend.UnsetTicketAttribute(key);

    /// <summary>Clears all ticket attributes.</summary>
    public static void ClearTicketAttributes() => Backend.ClearTicketAttributes();

    /// <summary>Sets the tags applied to created tickets.</summary>
    public static void SetTags(string[] tags) => Backend.SetTags(tags);

    /// <summary>Attaches a base64-encoded file to the next report.</summary>
    public static void AddAttachment(string base64File, string fileName) => Backend.AddAttachment(base64File, fileName);

    /// <summary>Removes all manually added attachments.</summary>
    public static void RemoveAllAttachments() => Backend.RemoveAllAttachments();

    /// <summary>Sets URL fragments whose network requests are excluded from network logs.</summary>
    public static void SetNetworkLogsBlacklist(string[] blacklist) => Backend.SetNetworkLogsBlacklist(blacklist);

    /// <summary>Sets request/response header and body properties stripped from network logs (privacy).</summary>
    public static void SetNetworkLogPropsToIgnore(string[] propsToIgnore) => Backend.SetNetworkLogPropsToIgnore(propsToIgnore);

    /// <summary>Silently submits a crash/bug report without opening the widget; <paramref name="excludeData"/> can omit data sections.</summary>
    public static Task SendSilentCrashReportAsync(string description, Severity severity, IReadOnlyDictionary<string, object>? excludeData = null, CancellationToken ct = default) => Backend.SendSilentCrashReportAsync(description, severity, excludeData, ct);

    /// <summary>Sets the widget language (ISO 639-1 code, e.g. <c>en</c>, <c>de</c>).</summary>
    public static void SetLanguage(string language) => Backend.SetLanguage(language);

    /// <summary>Returns whether the messenger is currently open.</summary>
    public static bool IsOpened() => Backend.IsOpened();

    /// <summary>Shows or hides the floating feedback button.</summary>
    public static void ShowFeedbackButton(bool visible) => Backend.ShowFeedbackButton(visible);

    /// <summary>Whether the feedback launcher should currently be shown (project config default, overridden
    /// by <see cref="ShowFeedbackButton"/>).</summary>
    public static bool IsFeedbackButtonVisible => Backend.IsFeedbackButtonVisible;

    /// <summary>The project's configured feedback-button position (e.g. <c>BOTTOM_RIGHT</c>,
    /// <c>BUTTON_HIDE</c>), or an empty string when unset.</summary>
    public static string FeedbackButtonPosition => Backend.FeedbackButtonPosition;

    /// <summary>Enables or disables in-app notification toasts.</summary>
    public static void SetDisableInAppNotifications(bool disable) => Backend.SetDisableInAppNotifications(disable);

    /// <summary>Pre-fills form fields for the next feedback flow.</summary>
    public static void PreFillForm(IReadOnlyDictionary<string, object?> formData) => Backend.PreFillForm(formData);

    /// <summary>Starts automatic network-request logging.</summary>
    public static void StartNetworkLogging() => Backend.StartNetworkLogging();

    /// <summary>Stops automatic network-request logging.</summary>
    public static void StopNetworkLogging() => Backend.StopNetworkLogging();

    /// <summary>Enables verbose SDK debug logging to the console.</summary>
    public static void EnableDebugConsoleLog() => Backend.EnableDebugConsoleLog();

    /// <summary>Disables automatic console-log capture.</summary>
    public static void DisableConsoleLog() => Backend.DisableConsoleLog();

    /// <summary>Sets the activation methods (e.g. shake, screenshot) that open the widget.</summary>
    public static void SetActivationMethods(ActivationMethod[] activationMethods) => Backend.SetActivationMethods(activationMethods);

    /// <summary>Declares the AI (frontend) tools your agent can call.</summary>
    public static void SetAiTools(Models.AITool[] tools) => Backend.SetAiTools(tools);

    /// <summary>Adds a base64 frame to the rolling session-replay buffer.</summary>
    public static void AddReplayFrame(string base64) => Backend.AddReplayFrame(base64);

    /// <summary>Test-only reset so xUnit cases don't leak backend state.</summary>
    internal static void ResetForTest() => _backend = null;
}
