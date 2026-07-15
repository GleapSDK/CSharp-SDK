using System.Runtime.InteropServices;

namespace GleapSDK.Omnis;

/// <summary>
/// The COM contract the Omnis 4GL glue drives. Every member uses only COM-friendly scalar/string types;
/// structured arguments cross the boundary as JSON strings (parsed internally), keeping the Omnis side
/// simple. The three transport members — <see cref="PushMessage"/>, <see cref="DequeueWidgetMessage"/> and
/// <see cref="DequeueAppEvent"/> — are how Omnis relays messages between the OBrowser bridge page and
/// Gleap.Core; everything else mirrors the Gleap facade one-to-one for capability parity with the native
/// SDKs.
/// </summary>
[ComVisible(true)]
[Guid("B4E9F1C2-3A6D-4C8E-9F21-7A0B5D6E8C34")]
[InterfaceType(ComInterfaceType.InterfaceIsDual)]
public interface IGleapOmnisClient
{
    // ---- Configuration (call before Initialize) ----------------------------------------------------

    /// <summary>Overrides the API base URL (self-hosting). Call before <see cref="Initialize"/>.</summary>
    void SetApiUrl(string url);

    /// <summary>Overrides the widget frame URL. Call before <see cref="Initialize"/>.</summary>
    void SetFrameUrl(string url);

    /// <summary>Overrides the realtime WebSocket URL. Call before <see cref="Initialize"/>.</summary>
    void SetWsUrl(string url);

    /// <summary>Sets the host application's name and version, reported in report metadata.</summary>
    void SetAppIdentity(string name, string version);

    /// <summary>Sets the Omnis top-level window handle used for native screenshots. Pass the HWND as a long.</summary>
    void SetWindowHandle(long hwnd);

    // ---- Lifecycle & transport ---------------------------------------------------------------------

    /// <summary>Initializes the SDK with your project's SDK key and begins loading session and config.
    /// Returns immediately; poll <see cref="IsReady"/> before loading the bridge page in OBrowser.</summary>
    void Initialize(string sdkKey);

    /// <summary>Whether initialization has completed and the config/session are loaded.</summary>
    bool IsReady();

    /// <summary>Absolute path to the bundled bridge HTML page (next to the assembly), for the OBrowser
    /// <c>$urlorcontrolname</c>/html-control. Returns an empty string if the file is missing.</summary>
    string BridgePagePath();

    /// <summary>Feeds a widget→host message (raw <c>{name,data}</c> JSON) from OBrowser's
    /// <c>evControlEvent</c> into Gleap.Core.</summary>
    void PushMessage(string json);

    /// <summary>Dequeues the next host→widget payload to deliver to the bridge page via
    /// <c>$callmethod("gleapDeliver", payload)</c>. Returns an empty string when nothing is queued.</summary>
    string DequeueWidgetMessage();

    /// <summary>Dequeues the next host-app callback as a JSON object (<c>{"type":...}</c>) — e.g.
    /// <c>customAction</c>, <c>notificationCount</c>, <c>widgetOpened</c>/<c>widgetClosed</c>,
    /// <c>openURL</c>, <c>toolExecution</c>. Returns an empty string when nothing is queued.</summary>
    string DequeueAppEvent();

    /// <summary>Captures the native Omnis window screenshot now and pushes it to the widget. Call right
    /// before making the OBrowser visible so the shot shows the app, not the widget.</summary>
    void CaptureScreenshot();

    /// <summary>Disconnects realtime and releases resources.</summary>
    void Shutdown();

    // ---- Messenger navigation ----------------------------------------------------------------------

    /// <summary>Opens the Gleap messenger.</summary>
    void Open();

    /// <summary>Closes the Gleap messenger.</summary>
    void Close();

    /// <summary>Whether the messenger is currently open.</summary>
    bool IsOpened();

    /// <summary>Starts a new conversation with the default bot or live support.</summary>
    void StartConversation(bool showBackButton);

    /// <summary>Starts a conversation with a specific bot.</summary>
    void StartBot(string botId, bool showBackButton);

    /// <summary>Opens an existing conversation by share token.</summary>
    void OpenConversation(string shareToken);

    /// <summary>Opens the conversations overview.</summary>
    void OpenConversations(bool showBackButton);

    /// <summary>Opens the help center.</summary>
    void OpenHelpCenter(bool showBackButton);

    /// <summary>Opens a specific help center article.</summary>
    void OpenHelpCenterArticle(string articleId, bool showBackButton);

    /// <summary>Opens a specific help center collection.</summary>
    void OpenHelpCenterCollection(string collectionId, bool showBackButton);

    /// <summary>Opens the help center with a search term prefilled.</summary>
    void SearchHelpCenter(string term, bool showBackButton);

    /// <summary>Opens the news overview.</summary>
    void OpenNews(bool showBackButton);

    /// <summary>Opens a specific news article.</summary>
    void OpenNewsArticle(string articleId, bool showBackButton);

    /// <summary>Opens the feature requests board.</summary>
    void OpenFeatureRequests(bool showBackButton);

    /// <summary>Opens the checklists overview.</summary>
    void OpenChecklists(bool showBackButton);

    /// <summary>Opens a specific checklist.</summary>
    void OpenChecklist(string checklistId, bool showBackButton);

    /// <summary>Starts a checklist from an outbound id.</summary>
    void StartChecklist(string outboundId, bool showBackButton);

    /// <summary>Manually shows a survey. <paramref name="format"/>: 0 = compact, 1 = full-screen.</summary>
    void ShowSurvey(string surveyId, int format);

    /// <summary>Starts a classic feedback form / flow by id.</summary>
    void StartClassicForm(string formId, bool showBackButton);

    /// <summary>Opens the AI assistant with a question prefilled.</summary>
    void AskAI(string question, bool showBackButton);

    // ---- Identity ----------------------------------------------------------------------------------

    /// <summary>Identifies the current user. <paramref name="propertiesJson"/> is an optional JSON object of
    /// user properties (name, email, phone, plan, companyName, companyId, avatar, value, sla, customData);
    /// <paramref name="userHash"/> is the optional verification hash (empty to omit).</summary>
    void IdentifyContact(string userId, string propertiesJson, string userHash);

    /// <summary>Updates properties of the identified user from a JSON object.</summary>
    void UpdateContact(string propertiesJson);

    /// <summary>Clears the current identity and ends the session (logout).</summary>
    void ClearIdentity();

    /// <summary>Whether a user is currently identified.</summary>
    bool IsUserIdentified();

    /// <summary>Returns the identified user's properties as a JSON object, or an empty string if none.</summary>
    string GetIdentityJson();

    // ---- Data, logging & reports -------------------------------------------------------------------

    /// <summary>Adds a message to the SDK console log. <paramref name="level"/>: 0 = error, 1 = warning, 2 = info.</summary>
    void Log(string message, int level);

    /// <summary>Tracks a custom event with optional structured data (JSON, empty for none).</summary>
    void TrackEvent(string name, string dataJson);

    /// <summary>Tracks a page / screen view (also used as the report's screen name).</summary>
    void TrackPage(string pageName);

    /// <summary>Sets a single custom-data key/value attached to reports.</summary>
    void SetCustomData(string key, string value);

    /// <summary>Merges a JSON object of custom data attached to reports.</summary>
    void AttachCustomData(string json);

    /// <summary>Removes a single custom-data key.</summary>
    void RemoveCustomDataForKey(string key);

    /// <summary>Clears all custom data.</summary>
    void ClearCustomData();

    /// <summary>Sets a ticket attribute sent with the next created ticket.</summary>
    void SetTicketAttribute(string key, string value);

    /// <summary>Removes a ticket attribute.</summary>
    void UnsetTicketAttribute(string key);

    /// <summary>Clears all ticket attributes.</summary>
    void ClearTicketAttributes();

    /// <summary>Sets the tags applied to created tickets, from a JSON array of strings.</summary>
    void SetTags(string tagsJson);

    /// <summary>Attaches a base64-encoded file to the next report.</summary>
    void AddAttachment(string base64File, string fileName);

    /// <summary>Removes all manually added attachments.</summary>
    void RemoveAllAttachments();

    /// <summary>Sets URL fragments (JSON array) whose requests are excluded from network logs.</summary>
    void SetNetworkLogsBlacklist(string json);

    /// <summary>Sets network-log properties (JSON array) stripped for privacy.</summary>
    void SetNetworkLogPropsToIgnore(string json);

    /// <summary>Silently submits a crash/bug report. <paramref name="severity"/>: 0 = low, 1 = medium,
    /// 2 = high. <paramref name="excludeJson"/> is an optional JSON object of data sections to omit.</summary>
    void SendSilentCrashReport(string description, int severity, string excludeJson);

    /// <summary>Sets the widget language (ISO 639-1, e.g. <c>en</c>, <c>de</c>).</summary>
    void SetLanguage(string language);

    /// <summary>Shows or hides the floating feedback button (rendered by the Omnis glue). Overrides the
    /// project config; raises the <c>feedbackButtonVisibility</c> app event so the glue's launcher reacts.</summary>
    void ShowFeedbackButton(bool visible);

    /// <summary>Whether the feedback launcher should currently be shown — the project's configured default
    /// (hidden when <see cref="FeedbackButtonPosition"/> is <c>BUTTON_HIDE</c>), overridden by
    /// <see cref="ShowFeedbackButton"/>. The glue's launcher reads this once ready, then reacts to the
    /// <c>feedbackButtonVisibility</c> app event.</summary>
    bool IsFeedbackButtonVisible();

    /// <summary>The project's configured feedback-button position (e.g. <c>BOTTOM_RIGHT</c>,
    /// <c>BOTTOM_LEFT</c>, <c>BUTTON_HIDE</c>), or an empty string when unset — so the glue can place it.</summary>
    string FeedbackButtonPosition();

    /// <summary>Enables or disables in-app notification toasts.</summary>
    void SetDisableInAppNotifications(bool disable);

    /// <summary>Pre-fills form fields for the next feedback flow, from a JSON object.</summary>
    void PreFillForm(string formDataJson);

    /// <summary>Starts automatic network-request logging (requests routed through the SDK handler).</summary>
    void StartNetworkLogging();

    /// <summary>Stops automatic network-request logging.</summary>
    void StopNetworkLogging();

    /// <summary>Enables verbose SDK debug console logging.</summary>
    void EnableDebugConsoleLog();

    /// <summary>Disables automatic console-log capture.</summary>
    void DisableConsoleLog();

    /// <summary>Runs a single outbound poll cycle (surveys, banners, modals, news).</summary>
    void CheckOutbound();

    /// <summary>Declares the AI (frontend) tools the agent can call, from a JSON array.</summary>
    void SetAiTools(string toolsJson);
}
