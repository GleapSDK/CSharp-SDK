using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GleapSDK.Bridge;
using GleapSDK.Capture;
using GleapSDK.Collection;
using GleapSDK.Data;
using GleapSDK.Events;
using GleapSDK.Feedback;
using GleapSDK.Http;
using GleapSDK.Metadata;
using GleapSDK.Models;
using GleapSDK.Serialization;
using GleapSDK.Session;
using GleapSDK.Time;

namespace GleapSDK;

/// <summary>Pure-C# backend used where no native SDK exists (desktop).</summary>
public sealed class ManagedBackend : IGleapBackend
{
    public sealed class Dependencies
    {
        public IHttpTransport Http { get; set; } = new HttpTransport();
        public IJsonSerializer Json { get; set; } = new SystemTextJsonSerializer();
        public IKeyValueStore Store { get; set; } = new InMemoryKeyValueStore();
        public IWebViewChannel Channel { get; set; } = null!;
        public GleapEndpoints Endpoints { get; set; } = GleapEndpoints.Default;
        public IMetadataProvider Metadata { get; set; } = new DefaultMetadataProvider("NET", "0.1.0");
        public IScreenshotProvider? Screenshot { get; set; }
    }

    private readonly Dependencies _d;
    private WebViewBridge _bridge = null!;
    private ApiClient _api = null!;
    private SessionManager _session = null!;
    private ConfigManager _config = null!;
    private WidgetBootstrapper _bootstrapper = null!;
    private string _token = "";
    private readonly ConsoleLogBuffer _consoleLog;
    private readonly EventBuffer _eventLog;
    private readonly NetworkLogBuffer _networkLog;
    private readonly CustomDataStore _customData = new();
    private readonly TicketAttributeStore _ticketAttributes = new();
    private readonly TagStore _tags = new();
    private readonly AttachmentStore _attachments = new();
    private readonly ReplayBuffer _replay = new(intervalMs: 1000, capacity: 60);
    private readonly SessionDataCollector _collector;
    private readonly GleapEventDispatcher _events = new();
    private bool _widgetOpen;
    private string? _editedScreenshot;
    private string _language = "en";
    private bool _feedbackButtonVisible;
    private bool _inAppNotificationsDisabled;
    private IReadOnlyDictionary<string, object?>? _prefill;
    private IReadOnlyList<ActivationMethod> _activationMethods = System.Array.Empty<ActivationMethod>();
    private System.Collections.Generic.IReadOnlyList<GleapSDK.Models.AITool> _aiTools = System.Array.Empty<GleapSDK.Models.AITool>();

    /// <summary>The most recently started send-feedback round-trip; exposed so tests can await it.</summary>
    internal Task? LastFeedbackTask { get; private set; }

    public ManagedBackend(Dependencies dependencies)
    {
        _d = dependencies;
        var clock = new SystemClock(); // concrete local avoids CA1859 (interface-typed local)
        _consoleLog = new ConsoleLogBuffer(clock, capacity: 100);
        _eventLog = new EventBuffer(clock, capacity: 100);
        _networkLog = new NetworkLogBuffer(capacity: 20);
        _collector = new SessionDataCollector(
            _consoleLog, _eventLog, _networkLog,
            _customData, _ticketAttributes, _tags,
            _d.Metadata);
    }

    private WebViewBridge Bridge => _bridge ?? throw new System.InvalidOperationException(
        "Gleap is not initialized. Call InitializeAsync before using the messenger.");

    public void RegisterListener(string eventName, System.Action<object?> handler) => _events.Register(eventName, handler);

    public async Task InitializeAsync(string token, CancellationToken ct)
    {
        _token = token;
        _api = new ApiClient(_d.Http, _d.Json, _d.Endpoints, token);
        _session = new SessionManager(_api, _d.Store);
        _config = new ConfigManager(_api);

        _bridge = new WebViewBridge(_d.Channel, _d.Json);
        _bootstrapper = new WidgetBootstrapper(_bridge, BuildSnapshot);

        _bridge.CollectTicketDataRequested += () =>
            _bridge.Send(new GleapBridgeMessage { Name = "collect-ticket-data", Data = _collector.BuildTicketData() });
        // The widget can close itself (its own close control). Reflect that in our own
        // opened-state so IsOpened()/the ping "opened" flag stay correct; do NOT echo a
        // widget-status-update back — the widget has already closed.
        _bridge.CloseWidgetRequested += () =>
        {
            _widgetOpen = false;
            _events.Emit("widgetClosed");
        };
        _bridge.SendFeedbackRequested += data => { LastFeedbackTask = HandleSendFeedbackAsync(data); };
        _bridge.HeightUpdated += height => _events.Emit("widgetHeightChanged", height);
        _bridge.OpenUrlRequested += url => _events.Emit("openURL", url);
        _bridge.ScreenshotUpdated += shot => _editedScreenshot = shot;
        _bridge.DrawingsCleanedUp += () => _editedScreenshot = null;   // revert to the original capture (iOS behavior)
        _bridge.FeedbackFlowStarted += _ => _events.Emit("feedbackFlowStarted");
        _bridge.CustomActionTriggered += (name, token) =>
            _events.Emit("customActionTriggered", new Dictionary<string, object?> { ["name"] = name, ["shareToken"] = token });
        _bridge.ToolExecutionRequested += _ => _events.Emit("toolExecution");

        await _session.StartAsync(_language, "desktop", ct).ConfigureAwait(false);
        await _config.LoadAsync(_language, ct).ConfigureAwait(false);
        _events.Emit("initialized");
    }

    private async Task HandleSendFeedbackAsync(JsonElement data)
    {
        var formData = ReadObject(data, "formData");
        var action = data.ValueKind == JsonValueKind.Object && data.TryGetProperty("action", out var a) ? a : default;
        var type = action.ValueKind == JsonValueKind.Object && action.TryGetProperty("feedbackType", out var ft)
            && ft.ValueKind == JsonValueKind.String ? ft.GetString()! : "BUG";
        var excludeKeys = ReadExcludeKeys(action);

        try
        {
            string? screenshot = null;
            if (!excludeKeys.Contains("screenshot"))
            {
                if (_editedScreenshot != null)
                {
                    screenshot = _editedScreenshot;   // user-annotated version from the widget's editor
                }
                else if (_d.Screenshot != null)
                {
                    try
                    {
                        screenshot = await _d.Screenshot.CaptureScreenshotAsync(default).ConfigureAwait(false);
                    }
                    catch (System.Exception)
                    {
                        screenshot = null;
                    }
                }
            }
            var replay = _replay.Snapshot().Count > 0 && !excludeKeys.Contains("replays") ? _replay.BuildReplay() : null;

            var body = FeedbackAssembler.Build(
                _collector.BuildTicketData(), formData, type, null, false, excludeKeys, _attachments.Snapshot(),
                screenshot: screenshot, replay: replay);
            var response = await _api.SubmitBugAsync(body, _session.GleapId, _session.GleapHash, default).ConfigureAwait(false);
            _bridge.Send(new GleapBridgeMessage { Name = "feedback-sent", Data = new Dictionary<string, object?> { ["response"] = response } });
            _events.Emit("feedbackSent", response);
        }
        catch (System.Exception ex)
        {
            _bridge.Send(new GleapBridgeMessage { Name = "feedback-sending-failed", Data = ex.Message });
        }
    }

    /// <summary>Silently submits a <c>CRASH</c> report without any widget interaction.</summary>
    public async Task SendSilentCrashReportAsync(
        string description, Severity severity, IReadOnlyDictionary<string, object>? excludeData, CancellationToken ct)
    {
        var priority = severity switch
        {
            Severity.High => "HIGH",
            Severity.Medium => "MEDIUM",
            _ => "LOW"
        };
        var excludeKeys = excludeData != null
            ? new HashSet<string>(excludeData.Keys)
            : new HashSet<string> { "screenshot", "replays", "attachments" };
        var formData = new Dictionary<string, object?> { ["description"] = description };

        string? screenshot = null;
        if (_d.Screenshot != null && !excludeKeys.Contains("screenshot"))
        {
            try
            {
                screenshot = await _d.Screenshot.CaptureScreenshotAsync(ct).ConfigureAwait(false);
            }
            catch (System.Exception)
            {
                screenshot = null;
            }
        }
        var replay = _replay.Snapshot().Count > 0 && !excludeKeys.Contains("replays") ? _replay.BuildReplay() : null;

        var body = FeedbackAssembler.Build(
            _collector.BuildTicketData(), formData, "CRASH", priority, true, excludeKeys, _attachments.Snapshot(),
            screenshot: screenshot, replay: replay);
        await _api.SubmitBugAsync(body, _session.GleapId, _session.GleapHash, ct).ConfigureAwait(false);
    }

    /// <summary>Pushes a periodically captured screenshot into the bounded replay ring
    /// (platform host owns the timer cadence).</summary>
    public void AddReplayFrame(string base64) => _replay.AddFrame(base64);

    /// <summary>Captures the current app surface and pushes it to the widget via <c>screenshot-update</c>,
    /// so the user can preview/annotate it in the report flow. Clears any prior edited screenshot to
    /// start fresh. The platform host calls this just before opening the widget.</summary>
    public async Task PrepareScreenshotAsync(CancellationToken ct = default)
    {
        _editedScreenshot = null;
        if (_d.Screenshot == null)
        {
            return;
        }
        string? shot;
        try
        {
            shot = await _d.Screenshot.CaptureScreenshotAsync(ct).ConfigureAwait(false);
        }
        catch (System.Exception)
        {
            shot = null;
        }
        if (shot != null)
        {
            _bridge.Send(new GleapBridgeMessage { Name = "screenshot-update", Data = shot });
        }
    }

    private static Dictionary<string, object?> ReadObject(JsonElement parent, string name)
    {
        var result = new Dictionary<string, object?>();
        if (parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out var obj)
            && obj.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in obj.EnumerateObject())
            {
                result[prop.Name] = prop.Value.Clone();
            }
        }
        return result;
    }

    private static HashSet<string> ReadExcludeKeys(JsonElement action)
    {
        var keys = new HashSet<string>();
        if (action.ValueKind == JsonValueKind.Object && action.TryGetProperty("excludeData", out var ex)
            && ex.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in ex.EnumerateObject())
            {
                if (prop.Value.ValueKind == JsonValueKind.True)
                {
                    keys.Add(prop.Name);
                }
            }
        }
        return keys;
    }

    private SessionSnapshot BuildSnapshot() => new()
    {
        SdkKey = _token,
        ApiUrl = _d.Endpoints.ApiUrl,
        GleapId = _session.GleapId,
        GleapHash = _session.GleapHash,
        FlowConfigJson = _config.FlowConfigJson,
        ProjectActionsJson = _config.ProjectActionsJson,
        Language = _language,
        UserId = _session.Identity?.UserId,
        Name = _session.Identity?.Name,
        Email = _session.Identity?.Email,
        PreFillFormData = _prefill,
        AiTools = _aiTools
    };

    public void Open()
    {
        Bridge.Send(new GleapBridgeMessage
        {
            Name = "widget-status-update",
            Data = new System.Collections.Generic.Dictionary<string, object> { ["isWidgetOpen"] = true }
        });
        _widgetOpen = true;
        _events.Emit("widgetOpened");
    }

    public void Close()
    {
        Bridge.Send(new GleapBridgeMessage
        {
            Name = "widget-status-update",
            Data = new System.Collections.Generic.Dictionary<string, object> { ["isWidgetOpen"] = false }
        });
        _widgetOpen = false;
        _events.Emit("widgetClosed");
    }

    /// <summary>Runs one outbound poll cycle: flush events, ping, emit notificationCountUpdated +
    /// outboundSent per action, and auto-open survey/feedback-flow actions. The platform host calls
    /// this on a timer (Core stays threadless).</summary>
    public async Task PollOutboundOnceAsync(CancellationToken ct)
    {
        var events = new List<object?>();
        foreach (var e in _eventLog.Snapshot())
        {
            events.Add(new Dictionary<string, object?> { ["name"] = e.Name, ["data"] = e.Data, ["date"] = e.Date });
        }

        var time = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var response = await _api.PingAsync(time, events, _widgetOpen, _session.GleapId, _session.GleapHash, ct)
            .ConfigureAwait(false);

        _events.Emit("notificationCountUpdated", response.UnreadCount);

        foreach (var action in response.Actions)
        {
            _events.Emit("outboundSent", new Dictionary<string, object?>
            {
                ["actionType"] = action.ActionType,
                ["outboundId"] = action.OutboundId,
                // Raw action JSON so the platform host can answer banner-data / modal-data.
                ["data"] = action.Data.ValueKind == JsonValueKind.Undefined ? null : action.Data.GetRawText()
            });

            if (action.ActionType == "survey")
            {
                var flow = action.Data.TryGetProperty("flow", out var f) && f.ValueKind == JsonValueKind.String
                    ? f.GetString()! : action.OutboundId ?? "";
                Bridge.Send(WidgetCommands.StartSurvey(flow, SurveyFormat.Survey));
            }
            else if (action.ActionType == "feedbackflow")
            {
                var flow = action.Data.TryGetProperty("flow", out var f) && f.ValueKind == JsonValueKind.String
                    ? f.GetString()! : action.OutboundId ?? "";
                Bridge.Send(WidgetCommands.StartClassicForm(flow, showBackButton: true));
            }
            // notification / banner / modal: surfaced via outboundSent; rendering is the platform host's job.
        }
    }

    public void StartConversation(bool showBackButton) => Bridge.Send(WidgetCommands.StartConversation(showBackButton));
    public void StartBot(string botId, bool showBackButton) => Bridge.Send(WidgetCommands.StartBot(botId, showBackButton));
    public void OpenConversation(string shareToken) => Bridge.Send(WidgetCommands.OpenConversation(shareToken));
    public void OpenHelpCenter(bool showBackButton) => Bridge.Send(WidgetCommands.OpenHelpCenter(showBackButton));
    public void OpenNews(bool showBackButton) => Bridge.Send(WidgetCommands.OpenNews(showBackButton));
    public void ShowSurvey(string surveyId, SurveyFormat format) => Bridge.Send(WidgetCommands.StartSurvey(surveyId, format));
    public void OpenConversations(bool showBackButton) => Bridge.Send(WidgetCommands.OpenConversations(showBackButton));
    public void StartClassicForm(string formId, bool showBackButton) => Bridge.Send(WidgetCommands.StartClassicForm(formId, showBackButton));
    public void OpenHelpCenterArticle(string articleId, bool showBackButton) => Bridge.Send(WidgetCommands.OpenHelpCenterArticle(articleId, showBackButton));
    public void OpenHelpCenterCollection(string collectionId, bool showBackButton) => Bridge.Send(WidgetCommands.OpenHelpCenterCollection(collectionId, showBackButton));
    public void SearchHelpCenter(string term, bool showBackButton) => Bridge.Send(WidgetCommands.SearchHelpCenter(term, showBackButton));
    public void OpenNewsArticle(string articleId, bool showBackButton) => Bridge.Send(WidgetCommands.OpenNewsArticle(articleId, showBackButton));
    public void OpenFeatureRequests(bool showBackButton) => Bridge.Send(WidgetCommands.OpenFeatureRequests(showBackButton));
    public void OpenChecklists(bool showBackButton) => Bridge.Send(WidgetCommands.OpenChecklists(showBackButton));
    public void OpenChecklist(string checklistId, bool showBackButton) => Bridge.Send(WidgetCommands.OpenChecklist(checklistId, showBackButton));
    public void StartChecklist(string outboundId, bool showBackButton) => Bridge.Send(WidgetCommands.StartChecklist(outboundId, showBackButton));
    public void AskAI(string question, bool showBackButton) => Bridge.Send(WidgetCommands.AskAI(question, showBackButton));

    public async Task IdentifyContactAsync(string userId, GleapUserProperty? properties, string? userHash, CancellationToken ct)
    {
        await _session.IdentifyAsync(userId, properties ?? new GleapUserProperty(), userHash, ct).ConfigureAwait(false);
        _bootstrapper.SendSessionUpdate();
    }

    public async Task UpdateContactAsync(GleapUserProperty properties, CancellationToken ct)
    {
        await _session.UpdateContactAsync(properties, ct).ConfigureAwait(false);
        _bootstrapper.SendSessionUpdate();
    }

    public async Task ClearIdentityAsync(CancellationToken ct)
    {
        _session.ClearIdentity();
        await _session.StartAsync("en", "desktop", ct).ConfigureAwait(false);
        _bootstrapper.SendSessionUpdate();
    }

    public bool IsUserIdentified() => _session.IsIdentified;
    public GleapUserProperty? GetIdentity() => _session.Identity;

    /// <summary>The raw <c>flowConfig</c> JSON from <c>/config</c> (widget appearance: colors, logo, …),
    /// or <c>"{}"</c> before initialization. Platform hosts use it to style native chrome such as the
    /// launcher button to match the project's configured widget.</summary>
    public string FlowConfigJson => _config?.FlowConfigJson ?? "{}";

    public void Log(string message, LogLevel level) => _consoleLog.Add(message, level);
    public void TrackEvent(string name, object? data) => _eventLog.Add(name, data);
    public void TrackPage(string pageName) => _eventLog.Add("pageView", new Dictionary<string, object> { ["page"] = pageName });
    public void SetCustomData(string key, string value) => _customData.Set(key, value);
    public void AttachCustomData(IReadOnlyDictionary<string, object> data) => _customData.Merge(data);
    public void RemoveCustomDataForKey(string key) => _customData.Remove(key);
    public void ClearCustomData() => _customData.Clear();
    public void SetTicketAttribute(string key, object value) => _ticketAttributes.Set(key, value);
    public void UnsetTicketAttribute(string key) => _ticketAttributes.Unset(key);
    public void ClearTicketAttributes() => _ticketAttributes.Clear();
    public void SetTags(string[] tags) => _tags.Set(tags);
    public void AddAttachment(string base64File, string fileName) => _attachments.Add(base64File, fileName);
    public void RemoveAllAttachments() => _attachments.Clear();
    public void SetNetworkLogsBlacklist(string[] blacklist) => _networkLog.SetBlacklist(blacklist);
    public void SetNetworkLogPropsToIgnore(string[] propsToIgnore) => _networkLog.SetPropsToIgnore(propsToIgnore);

    public void SetLanguage(string language) => _language = language;
    public bool IsOpened() => _widgetOpen;
    public void ShowFeedbackButton(bool visible) => _feedbackButtonVisible = visible;
    public void SetDisableInAppNotifications(bool disable) => _inAppNotificationsDisabled = disable;

    public void PreFillForm(IReadOnlyDictionary<string, object?> formData)
    {
        _prefill = formData;
        _bootstrapper.SendPrefill();
    }

    public void StartNetworkLogging() => _networkLog.Enabled = true;
    public void StopNetworkLogging() => _networkLog.Enabled = false;
    public void EnableDebugConsoleLog() => _consoleLog.Enabled = true;
    public void DisableConsoleLog() => _consoleLog.Enabled = false;
    public void SetActivationMethods(ActivationMethod[] activationMethods) => _activationMethods = activationMethods;

    public void SetAiTools(GleapSDK.Models.AITool[] tools)
    {
        _aiTools = tools;
        _bootstrapper.SendConfigUpdate();
    }
}
