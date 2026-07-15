using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GleapSDK.Http;
using GleapSDK.Models;
using GleapSDK.Realtime;
using GleapSDK.Serialization;

namespace GleapSDK.Omnis;

/// <summary>
/// COM-visible entry point for the Omnis Studio (Windows) integration. The Omnis 4GL glue creates one
/// instance (ProgId <c>Gleap.OmnisClient</c>), configures endpoints/identity, calls <see cref="Initialize"/>,
/// then shuttles bridge messages between OBrowser and Gleap.Core via <see cref="PushMessage"/> /
/// <see cref="DequeueWidgetMessage"/> and reads host callbacks from <see cref="DequeueAppEvent"/>.
/// <para>
/// It reuses the whole Gleap.Core protocol engine (<see cref="ManagedBackend"/>) unchanged and injects the
/// Omnis platform providers: <see cref="OmnisWebViewChannel"/> (the OBrowser transport),
/// <see cref="Win32ScreenshotProvider"/>, <see cref="OmnisMetadataProvider"/>, a persistent session store,
/// and the shared <see cref="GleapWebSocket"/> realtime channel — giving capability parity with the native
/// SDKs (minus features that don't exist on desktop: product tours, shake/screenshot activation, APNs push).
/// </para>
/// Structured arguments cross the COM boundary as JSON strings and are parsed here.
/// </summary>
[ComVisible(true)]
[Guid("C5F0A2D3-4B7E-4D9F-8012-6B1C4E7F9D45")]
[ClassInterface(ClassInterfaceType.None)]
[ProgId("Gleap.OmnisClient")]
public sealed class GleapOmnisClient : IGleapOmnisClient, IDisposable
{
    /// <summary>Version reported in report metadata; bump with releases.</summary>
    public const string SdkVersion = "1.0.0";

    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly GleapEndpoints _endpoints = GleapEndpoints.Default;
    private readonly Win32ScreenshotProvider _screenshot = new Win32ScreenshotProvider();
    private readonly OmnisMetadataProvider _metadata = new OmnisMetadataProvider(SdkVersion);
    private readonly OmnisFileKeyValueStore _store = new OmnisFileKeyValueStore();
    private readonly System.Collections.Concurrent.ConcurrentQueue<string> _appEvents =
        new System.Collections.Concurrent.ConcurrentQueue<string>();
    private readonly CancellationTokenSource _cts = new CancellationTokenSource();

    private OmnisWebViewChannel? _channel;
    private ManagedBackend? _backend;
    private GleapWebSocket? _realtime;
    private volatile bool _ready;

    /// <summary>Creates an unconfigured client. Call <see cref="Initialize"/> after setting endpoints/identity.</summary>
    public GleapOmnisClient()
    {
    }

    // ---- Configuration ------------------------------------------------------------------------------

    /// <inheritdoc />
    public void SetApiUrl(string url)
    {
        if (!string.IsNullOrEmpty(url))
        {
            _endpoints.ApiUrl = url;
        }
    }

    /// <inheritdoc />
    public void SetFrameUrl(string url)
    {
        if (!string.IsNullOrEmpty(url))
        {
            _endpoints.FrameUrl = url;
        }
    }

    /// <inheritdoc />
    public void SetWsUrl(string url)
    {
        if (!string.IsNullOrEmpty(url))
        {
            _endpoints.WsUrl = url;
        }
    }

    /// <inheritdoc />
    public void SetAppIdentity(string name, string version)
    {
        if (!string.IsNullOrEmpty(name))
        {
            _metadata.AppName = name;
        }

        if (!string.IsNullOrEmpty(version))
        {
            _metadata.AppVersion = version;
        }
    }

    /// <inheritdoc />
    public void SetWindowHandle(long hwnd) => _screenshot.TargetWindow = new IntPtr(hwnd);

    // ---- Lifecycle & transport ----------------------------------------------------------------------

    /// <inheritdoc />
    public void Initialize(string sdkKey)
    {
        if (_backend != null || string.IsNullOrEmpty(sdkKey))
        {
            return;
        }

        _channel = new OmnisWebViewChannel();
        _realtime = new GleapWebSocket();
        var backend = new ManagedBackend(new ManagedBackend.Dependencies
        {
            Http = new HttpTransport(),
            Json = new SystemTextJsonSerializer(),
            Store = _store,
            Channel = _channel,
            Endpoints = _endpoints,
            Metadata = _metadata,
            Screenshot = _screenshot,
            Realtime = _realtime,
            Platform = "windows",
            DeviceType = "desktop",
            SdkVersion = SdkVersion
        });
        _backend = backend;

        WireEvents(backend);
        Track(InitializeCoreAsync(backend, sdkKey));
    }

    private async Task InitializeCoreAsync(ManagedBackend backend, string sdkKey)
    {
        try
        {
            await backend.InitializeAsync(sdkKey, _cts.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            EnqueueAppEvent(new { type = "error", message = ex.Message });
        }
    }

    /// <inheritdoc />
    public bool IsReady() => _ready;

    /// <inheritdoc />
    public string BridgePagePath()
    {
        try
        {
            var dir = Path.GetDirectoryName(typeof(GleapOmnisClient).Assembly.Location);
            if (string.IsNullOrEmpty(dir))
            {
                return string.Empty;
            }

            var path = Path.Combine(dir!, "web", "gleap-omnis-bridge.html");
            return File.Exists(path) ? path : string.Empty;
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    /// <inheritdoc />
    public void PushMessage(string json) => _channel?.PushMessage(json);

    /// <inheritdoc />
    public string DequeueWidgetMessage() =>
        _channel != null && _channel.TryDequeueOutgoing(out var payload) ? payload! : string.Empty;

    /// <inheritdoc />
    public string DequeueAppEvent() => _appEvents.TryDequeue(out var evt) ? evt : string.Empty;

    /// <inheritdoc />
    public void CaptureScreenshot()
    {
        if (_backend != null)
        {
            Track(_backend.PrepareScreenshotAsync(_cts.Token));
        }
    }

    /// <inheritdoc />
    public void Shutdown()
    {
        try
        {
            _cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Already shut down.
        }

        _realtime?.Dispose();
    }

    /// <summary>Releases the realtime connection and cancellation source. Called by <see cref="Shutdown"/>
    /// and by the runtime if the COM object is finalized.</summary>
    public void Dispose()
    {
        Shutdown();
        _cts.Dispose();
    }

    // ---- Messenger navigation -----------------------------------------------------------------------

    /// <inheritdoc />
    public void Open() => _backend?.Open();

    /// <inheritdoc />
    public void Close() => _backend?.Close();

    /// <inheritdoc />
    public bool IsOpened() => _backend?.IsOpened() ?? false;

    /// <inheritdoc />
    public void StartConversation(bool showBackButton) => _backend?.StartConversation(showBackButton);

    /// <inheritdoc />
    public void StartBot(string botId, bool showBackButton) => _backend?.StartBot(botId, showBackButton);

    /// <inheritdoc />
    public void OpenConversation(string shareToken) => _backend?.OpenConversation(shareToken);

    /// <inheritdoc />
    public void OpenConversations(bool showBackButton) => _backend?.OpenConversations(showBackButton);

    /// <inheritdoc />
    public void OpenHelpCenter(bool showBackButton) => _backend?.OpenHelpCenter(showBackButton);

    /// <inheritdoc />
    public void OpenHelpCenterArticle(string articleId, bool showBackButton) =>
        _backend?.OpenHelpCenterArticle(articleId, showBackButton);

    /// <inheritdoc />
    public void OpenHelpCenterCollection(string collectionId, bool showBackButton) =>
        _backend?.OpenHelpCenterCollection(collectionId, showBackButton);

    /// <inheritdoc />
    public void SearchHelpCenter(string term, bool showBackButton) =>
        _backend?.SearchHelpCenter(term, showBackButton);

    /// <inheritdoc />
    public void OpenNews(bool showBackButton) => _backend?.OpenNews(showBackButton);

    /// <inheritdoc />
    public void OpenNewsArticle(string articleId, bool showBackButton) =>
        _backend?.OpenNewsArticle(articleId, showBackButton);

    /// <inheritdoc />
    public void OpenFeatureRequests(bool showBackButton) => _backend?.OpenFeatureRequests(showBackButton);

    /// <inheritdoc />
    public void OpenChecklists(bool showBackButton) => _backend?.OpenChecklists(showBackButton);

    /// <inheritdoc />
    public void OpenChecklist(string checklistId, bool showBackButton) =>
        _backend?.OpenChecklist(checklistId, showBackButton);

    /// <inheritdoc />
    public void StartChecklist(string outboundId, bool showBackButton) =>
        _backend?.StartChecklist(outboundId, showBackButton);

    /// <inheritdoc />
    public void ShowSurvey(string surveyId, int format) =>
        _backend?.ShowSurvey(surveyId, format == 1 ? SurveyFormat.SurveyFull : SurveyFormat.Survey);

    /// <inheritdoc />
    public void StartClassicForm(string formId, bool showBackButton) =>
        _backend?.StartClassicForm(formId, showBackButton);

    /// <inheritdoc />
    public void AskAI(string question, bool showBackButton) => _backend?.AskAI(question, showBackButton);

    // ---- Identity -----------------------------------------------------------------------------------

    /// <inheritdoc />
    public void IdentifyContact(string userId, string propertiesJson, string userHash)
    {
        if (_backend == null || string.IsNullOrEmpty(userId))
        {
            return;
        }

        var props = ParseUserProperty(propertiesJson);
        var hash = string.IsNullOrEmpty(userHash) ? null : userHash;
        Track(_backend.IdentifyContactAsync(userId, props, hash, _cts.Token));
    }

    /// <inheritdoc />
    public void UpdateContact(string propertiesJson)
    {
        var props = ParseUserProperty(propertiesJson);
        if (_backend != null && props != null)
        {
            Track(_backend.UpdateContactAsync(props, _cts.Token));
        }
    }

    /// <inheritdoc />
    public void ClearIdentity()
    {
        if (_backend != null)
        {
            Track(_backend.ClearIdentityAsync(_cts.Token));
        }
    }

    /// <inheritdoc />
    public bool IsUserIdentified() => _backend?.IsUserIdentified() ?? false;

    /// <inheritdoc />
    public string GetIdentityJson()
    {
        var identity = _backend?.GetIdentity();
        return identity == null ? string.Empty : JsonSerializer.Serialize(identity);
    }

    // ---- Data, logging & reports --------------------------------------------------------------------

    /// <inheritdoc />
    public void Log(string message, int level) =>
        _backend?.Log(message, (LogLevel)Clamp(level, 0, 2));

    /// <inheritdoc />
    public void TrackEvent(string name, string dataJson) =>
        _backend?.TrackEvent(name, ParseElementOrNull(dataJson));

    /// <inheritdoc />
    public void TrackPage(string pageName) => _backend?.TrackPage(pageName);

    /// <inheritdoc />
    public void SetCustomData(string key, string value) => _backend?.SetCustomData(key, value);

    /// <inheritdoc />
    public void AttachCustomData(string json)
    {
        var dict = ParseDictionary(json);
        if (_backend != null && dict != null)
        {
            _backend.AttachCustomData(dict);
        }
    }

    /// <inheritdoc />
    public void RemoveCustomDataForKey(string key) => _backend?.RemoveCustomDataForKey(key);

    /// <inheritdoc />
    public void ClearCustomData() => _backend?.ClearCustomData();

    /// <inheritdoc />
    public void SetTicketAttribute(string key, string value) => _backend?.SetTicketAttribute(key, value);

    /// <inheritdoc />
    public void UnsetTicketAttribute(string key) => _backend?.UnsetTicketAttribute(key);

    /// <inheritdoc />
    public void ClearTicketAttributes() => _backend?.ClearTicketAttributes();

    /// <inheritdoc />
    public void SetTags(string tagsJson) => _backend?.SetTags(ParseStringArray(tagsJson));

    /// <inheritdoc />
    public void AddAttachment(string base64File, string fileName) =>
        _backend?.AddAttachment(base64File, fileName);

    /// <inheritdoc />
    public void RemoveAllAttachments() => _backend?.RemoveAllAttachments();

    /// <inheritdoc />
    public void SetNetworkLogsBlacklist(string json) =>
        _backend?.SetNetworkLogsBlacklist(ParseStringArray(json));

    /// <inheritdoc />
    public void SetNetworkLogPropsToIgnore(string json) =>
        _backend?.SetNetworkLogPropsToIgnore(ParseStringArray(json));

    /// <inheritdoc />
    public void SendSilentCrashReport(string description, int severity, string excludeJson)
    {
        if (_backend == null)
        {
            return;
        }

        var exclude = ParseDictionary(excludeJson);
        Track(_backend.SendSilentCrashReportAsync(
            description, (Severity)Clamp(severity, 0, 2), exclude, _cts.Token));
    }

    /// <inheritdoc />
    public void SetLanguage(string language) => _backend?.SetLanguage(language);

    /// <inheritdoc />
    public void ShowFeedbackButton(bool visible) => _backend?.ShowFeedbackButton(visible);

    /// <inheritdoc />
    public bool IsFeedbackButtonVisible() => _backend?.IsFeedbackButtonVisible ?? false;

    /// <inheritdoc />
    public string FeedbackButtonPosition() => _backend?.FeedbackButtonPosition ?? string.Empty;

    /// <inheritdoc />
    public void SetDisableInAppNotifications(bool disable) => _backend?.SetDisableInAppNotifications(disable);

    /// <inheritdoc />
    public void PreFillForm(string formDataJson)
    {
        var form = ParseNullableDictionary(formDataJson);
        if (_backend != null && form != null)
        {
            _backend.PreFillForm(form);
        }
    }

    /// <inheritdoc />
    public void StartNetworkLogging() => _backend?.StartNetworkLogging();

    /// <inheritdoc />
    public void StopNetworkLogging() => _backend?.StopNetworkLogging();

    /// <inheritdoc />
    public void EnableDebugConsoleLog() => _backend?.EnableDebugConsoleLog();

    /// <inheritdoc />
    public void DisableConsoleLog() => _backend?.DisableConsoleLog();

    /// <inheritdoc />
    public void CheckOutbound()
    {
        if (_backend != null)
        {
            Track(_backend.PollOutboundOnceAsync(_cts.Token));
        }
    }

    /// <inheritdoc />
    public void SetAiTools(string toolsJson)
    {
        var tools = ParseArray<AITool>(toolsJson);
        if (_backend != null && tools != null)
        {
            _backend.SetAiTools(tools);
        }
    }

    // ---- Event wiring -------------------------------------------------------------------------------

    private void WireEvents(ManagedBackend backend)
    {
        backend.RegisterListener("initialized", _ =>
        {
            _ready = true;
            EnqueueAppEvent(new { type = "initialized" });
        });
        backend.RegisterListener("widgetOpened", _ => EnqueueAppEvent(new { type = "widgetOpened" }));
        backend.RegisterListener("widgetClosed", _ => EnqueueAppEvent(new { type = "widgetClosed" }));
        backend.RegisterListener("widgetHeightChanged", data =>
            EnqueueAppEvent(new { type = "widgetHeightChanged", height = data as double? ?? 0 }));
        backend.RegisterListener("feedbackFlowStarted", _ => EnqueueAppEvent(new { type = "feedbackFlowStarted" }));
        backend.RegisterListener("feedbackSent", _ => EnqueueAppEvent(new { type = "feedbackSent" }));
        backend.RegisterListener("toolExecution", _ => EnqueueAppEvent(new { type = "toolExecution" }));
        backend.RegisterListener("notificationCountUpdated", data =>
            EnqueueAppEvent(new { type = "notificationCount", count = data as int? ?? 0 }));
        backend.RegisterListener("feedbackButtonVisibilityUpdated", data =>
            EnqueueAppEvent(new { type = "feedbackButtonVisibility", visible = data as bool? ?? true }));
        backend.RegisterListener("customActionTriggered", OnCustomAction);
        backend.RegisterListener("openURL", OnOpenUrl);
        backend.RegisterListener("outboundSent", OnOutboundSent);
        // Live-checklist progress (arrives over realtime); the glue can update its own checklist UI.
        backend.RegisterListener("checklistUpdated", d => OnChecklistEvent("checklistUpdated", d));
        backend.RegisterListener("checklistStepCompleted", d => OnChecklistEvent("checklistStepCompleted", d));
        backend.RegisterListener("checklistCompleted", d => OnChecklistEvent("checklistCompleted", d));
    }

    private void OnCustomAction(object? data)
    {
        var name = string.Empty;
        string? shareToken = null;
        if (data is IReadOnlyDictionary<string, object?> map)
        {
            name = map.TryGetValue("name", out var n) ? n as string ?? string.Empty : string.Empty;
            shareToken = map.TryGetValue("shareToken", out var t) ? t as string : null;
        }

        EnqueueAppEvent(new { type = "customAction", name, shareToken });
    }

    /// <summary>Surfaces an outbound action (in-app notification, banner or modal) to the Omnis glue so it
    /// can render the native chrome — the <c>notification</c> preview card, a banner or a modal. Survey
    /// outbounds open in the widget themselves and do not need host rendering. <c>data</c> is the raw
    /// action JSON (or empty) the glue uses to populate the card.</summary>
    private void OnOutboundSent(object? data)
    {
        if (data is not IReadOnlyDictionary<string, object?> map)
        {
            return;
        }

        var actionType = map.TryGetValue("actionType", out var at) ? at as string ?? string.Empty : string.Empty;
        var outboundId = map.TryGetValue("outboundId", out var oi) ? oi as string ?? string.Empty : string.Empty;
        var actionData = map.TryGetValue("data", out var d) ? d as string : null;
        EnqueueAppEvent(new { type = "outbound", actionType, outboundId, data = actionData });
    }

    /// <summary>Surfaces a live-checklist progress event (<c>checklistUpdated</c> / <c>checklistStepCompleted</c>
    /// / <c>checklistCompleted</c>) to the Omnis glue. The event's fields (checklistId, outboundId, status,
    /// completedSteps, totalSteps, stepId/stepIndex/stepTitle) are nested under <c>data</c> as a JSON object.</summary>
    private void OnChecklistEvent(string type, object? data)
    {
        var map = data as IReadOnlyDictionary<string, object?>;
        EnqueueAppEvent(new { type, data = map });
    }

    private void OnOpenUrl(object? data)
    {
        if (data is not string url || string.IsNullOrEmpty(url))
        {
            return;
        }

        // http/https open in the system browser (help articles etc.); app-specific schemes are surfaced
        // to Omnis so the app can route its own deep links.
        if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            OpenExternalUrl(url);
        }
        else
        {
            EnqueueAppEvent(new { type = "openURL", url });
        }
    }

    private static void OpenExternalUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Debug.WriteLine("Gleap: failed to open URL: " + ex.Message);
        }
    }

    private void EnqueueAppEvent(object payload)
    {
        try
        {
            _appEvents.Enqueue(JsonSerializer.Serialize(payload));
        }
        catch (Exception ex)
        {
            Debug.WriteLine("Gleap: could not queue app event: " + ex.Message);
        }
    }

    // ---- Helpers ------------------------------------------------------------------------------------

    /// <summary>Fire-and-forget an async backend call without an <c>async void</c>; failures are logged,
    /// never thrown across the COM boundary (which would surface to Omnis as an HRESULT error).</summary>
    private static void Track(Task task) => _ = Observe(task);

    private static async Task Observe(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Debug.WriteLine("Gleap: async operation failed: " + ex.Message);
        }
    }

    private static int Clamp(int value, int min, int max) => value < min ? min : value > max ? max : value;

    private static GleapUserProperty? ParseUserProperty(string json) =>
        Deserialize<GleapUserProperty>(json);

    private static string[] ParseStringArray(string json) =>
        Deserialize<string[]>(json) ?? Array.Empty<string>();

    private static T[]? ParseArray<T>(string json) => Deserialize<T[]>(json);

    private static Dictionary<string, object>? ParseDictionary(string json) =>
        Deserialize<Dictionary<string, object>>(json);

    private static Dictionary<string, object?>? ParseNullableDictionary(string json) =>
        Deserialize<Dictionary<string, object?>>(json);

    private static object? ParseElementOrNull(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            // Not JSON — treat the raw string as the event value.
            return json;
        }
    }

    private static T? Deserialize<T>(string json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            Debug.WriteLine("Gleap: could not parse JSON argument: " + ex.Message);
            return null;
        }
    }
}
