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
using GleapSDK.Outbound;
using GleapSDK.Realtime;
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

        /// <summary>Optional real-time channel (WebSocket). When set, the backend receives outbound/unread
        /// pushes instantly and marks its pings <c>ws:true</c> so the server doesn't also push over the
        /// poll. When null (e.g. in tests), the backend relies on the outbound poll alone.</summary>
        public IRealtimeChannel? Realtime { get; set; }

        /// <summary>Runtime identifier reported to the API as <c>platform</c>/<c>type</c> (e.g.
        /// <c>"windows"</c>, <c>"unity"</c>, <c>"android"</c>, <c>"ios"</c>). Platform hosts override
        /// this; the managed default is desktop/Windows.</summary>
        public string Platform { get; set; } = "windows";

        /// <summary>Device class reported to the API on session start (e.g. <c>"desktop"</c>,
        /// <c>"mobile"</c>). Platform hosts override this.</summary>
        public string DeviceType { get; set; } = "desktop";

        /// <summary>SDK version reported to the API on ping/contact updates.</summary>
        public string SdkVersion { get; set; } = "0.1.0";
    }

    private readonly Dependencies _d;
    private readonly SystemClock _clock;
    private WebViewBridge _bridge = null!;
    private ApiClient _api = null!;
    private SessionManager _session = null!;
    private ConfigManager _config = null!;
    private WidgetBootstrapper _bootstrapper = null!;
    private string _token = "";
    private readonly ConsoleLogBuffer _consoleLog;

    /// <summary>Events for the ticket's <c>customEventLog</c>. Deliberately separate from
    /// <see cref="_streamedEventLog"/>: this one is never drained by the outbound ping, so a report carries
    /// the session's whole event history rather than only what happened since the last 5s tick. The native
    /// SDKs keep the same two arrays.</summary>
    private readonly EventBuffer _eventLog;

    /// <summary>Queue of events still to be flushed to the server on the next ping; drained on success.</summary>
    private readonly EventBuffer _streamedEventLog;

    private readonly NetworkLogBuffer _networkLog;
    private readonly CustomDataStore _customData = new();
    private readonly TicketAttributeStore _ticketAttributes = new();
    private readonly TagStore _tags = new();
    private readonly AttachmentStore _attachments = new();
    private readonly ReplayBuffer _replay = new(intervalMs: 1000, capacity: 60);
    private readonly SessionDataCollector _collector;
    private readonly GleapEventDispatcher _events = new();
    private bool _widgetOpen;
    private string? _lastScreenName;
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
        _clock = new SystemClock(); // concrete field avoids CA1859 (interface-typed local)
        _consoleLog = new ConsoleLogBuffer(_clock, capacity: 1000);   // iOS caps its console log at 1000
        _eventLog = new EventBuffer(_clock, capacity: 1000);          // iOS caps its report event log at 1000
        _streamedEventLog = new EventBuffer(_clock, capacity: 1000);
        _networkLog = new NetworkLogBuffer(capacity: 20);
        _collector = new SessionDataCollector(
            _consoleLog, _eventLog, _networkLog,
            _customData, _ticketAttributes, _tags,
            _d.Metadata,
            () => _lastScreenName);
    }

    private WebViewBridge Bridge => _bridge ?? throw new System.InvalidOperationException(
        "Gleap is not initialized. Call InitializeAsync before using the messenger.");

    public void RegisterListener(string eventName, System.Action<object?> handler) => _events.Register(eventName, handler);
    public void RemoveListener(string eventName, System.Action<object?> handler) => _events.Unregister(eventName, handler);

    public async Task InitializeAsync(string token, CancellationToken ct)
    {
        _token = token;
        _api = new ApiClient(_d.Http, _d.Json, _d.Endpoints, token, _d.Platform, _d.SdkVersion);
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

        await _session.StartAsync(_language, _d.DeviceType, ct).ConfigureAwait(false);
        LogEvent("sessionStarted", null);   // per session establishment, matching the native SDKs
        await _config.LoadAsync(_language, ct).ConfigureAwait(false);

        // Real-time channel (optional): once the session exists, connect the WebSocket so outbound
        // actions and the unread count arrive instantly instead of on the next poll tick.
        if (_d.Realtime != null)
        {
            _d.Realtime.MessageReceived += OnRealtimeMessage;
            _d.Realtime.Connect(BuildRealtimeUrl());
        }

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
            string? dataUri = null;
            if (!excludeKeys.Contains("screenshot"))
            {
                dataUri = _editedScreenshot;   // user-annotated version from the widget's editor, if any
                if (dataUri == null && _d.Screenshot != null)
                {
                    try
                    {
                        dataUri = await _d.Screenshot.CaptureScreenshotAsync(default).ConfigureAwait(false);
                    }
                    catch (System.Exception)
                    {
                        dataUri = null;
                    }
                }
            }
            var screenshotUrl = await UploadScreenshotAsync(dataUri, default).ConfigureAwait(false);
            var attachments = await UploadAttachmentsAsync(excludeKeys, default).ConfigureAwait(false);
            var replay = await BuildReplayAsync(excludeKeys, default).ConfigureAwait(false);

            var body = FeedbackAssembler.Build(
                _collector.BuildTicketData(), formData, type, null, false, excludeKeys, attachments,
                screenshotUrl: screenshotUrl, replay: replay);
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

        string? dataUri = null;
        if (_d.Screenshot != null && !excludeKeys.Contains("screenshot"))
        {
            try
            {
                dataUri = await _d.Screenshot.CaptureScreenshotAsync(ct).ConfigureAwait(false);
            }
            catch (System.Exception)
            {
                dataUri = null;
            }
        }
        var screenshotUrl = await UploadScreenshotAsync(dataUri, ct).ConfigureAwait(false);
        var attachments = await UploadAttachmentsAsync(excludeKeys, ct).ConfigureAwait(false);
        var replay = await BuildReplayAsync(excludeKeys, ct).ConfigureAwait(false);

        var body = FeedbackAssembler.Build(
            _collector.BuildTicketData(), formData, "CRASH", priority, true, excludeKeys, attachments,
            screenshotUrl: screenshotUrl, replay: replay);
        await _api.SubmitBugAsync(body, _session.GleapId, _session.GleapHash, ct).ConfigureAwait(false);
    }

    /// <summary>Pushes a periodically captured screenshot into the bounded replay ring
    /// (platform host owns the timer cadence).</summary>
    public void AddReplayFrame(string base64) => _replay.AddFrame(base64);

    /// <summary>Captures one replay frame from the app surface (via the screenshot provider) and pushes it
    /// into the replay ring. The platform host drives the cadence from <see cref="ReplayIntervalMs"/>.
    /// No-op when no screenshot provider is configured.</summary>
    public async Task CaptureReplayFrameAsync(CancellationToken ct = default)
    {
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
            _replay.AddFrame(shot);
        }
    }

    /// <summary>Replay capture interval in milliseconds when the project enabled session replays
    /// (flowConfig <c>enableReplays</c>), else null. The platform host uses this to decide whether and
    /// how often to capture replay frames — mirroring the native SDKs, which only run the replay timer
    /// when the project turned replays on.</summary>
    public int? ReplayIntervalMs
    {
        get
        {
            try
            {
                using var doc = JsonDocument.Parse(_config?.FlowConfigJson ?? "{}");
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object
                    || !root.TryGetProperty("enableReplays", out var enabled)
                    || enabled.ValueKind != JsonValueKind.True)
                {
                    return null;
                }
                var seconds = root.TryGetProperty("replaysInterval", out var iv) && iv.TryGetInt32(out var s) && s > 0
                    ? s
                    : 5;
                return seconds * 1000;
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }

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

    /// <summary>Uploads a screenshot data-URI to /uploads/sdk and returns its URL (reports reference the
    /// image by URL, like the native SDKs — not inline base64). Returns null if there is nothing to upload
    /// or the upload fails, so a report is never blocked by it.</summary>
    private async Task<string?> UploadScreenshotAsync(string? dataUri, CancellationToken ct)
    {
        var decoded = DecodeDataUri(dataUri);
        if (decoded == null)
        {
            return null;
        }
        try
        {
            return await _api.UploadImageAsync(
                decoded.Value.Bytes, decoded.Value.FileName, decoded.Value.ContentType,
                _session.GleapId, _session.GleapHash, ct).ConfigureAwait(false);
        }
        catch (System.Exception)
        {
            return null;
        }
    }

    private static (byte[] Bytes, string FileName, string ContentType)? DecodeDataUri(string? dataUri)
    {
        const string prefix = "data:";
        if (dataUri == null)
        {
            return null;
        }
        var comma = dataUri.IndexOf(',');
        if (comma < 0 || !dataUri.StartsWith(prefix, System.StringComparison.Ordinal))
        {
            return null;
        }
        var contentType = dataUri.Substring(prefix.Length, comma - prefix.Length).Split(';')[0];
        if (string.IsNullOrEmpty(contentType))
        {
            contentType = "image/png";
        }
        byte[] bytes;
        try
        {
            bytes = System.Convert.FromBase64String(dataUri.Substring(comma + 1));
        }
        catch (System.FormatException)
        {
            return null;
        }
        var extension = contentType == "image/jpeg" ? "jpg" : "png";
        return (bytes, "screenshot." + extension, contentType);
    }

    /// <summary>Uploads the pending custom attachments to <c>/uploads/attachments</c> and returns
    /// <c>{url, name, type}</c> entries (inline data stripped), like the native SDKs. Null when excluded,
    /// empty, or the upload fails, so a report is never blocked by it.</summary>
    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>?> UploadAttachmentsAsync(
        HashSet<string> excludeKeys, CancellationToken ct)
    {
        if (excludeKeys.Contains("attachments"))
        {
            return null;
        }
        var pending = _attachments.Snapshot();
        if (pending.Count == 0)
        {
            return null;
        }

        var files = new List<UploadFile>();
        foreach (var att in pending)
        {
            var decoded = DecodeUpload(att.Base64File, att.FileName, MimeFromName(att.FileName));
            if (decoded != null)
            {
                files.Add(decoded);
            }
        }
        if (files.Count == 0)
        {
            return null;
        }

        IReadOnlyList<string> urls;
        try
        {
            urls = await _api.UploadImagesAsync("attachments", files, _session.GleapId, _session.GleapHash, ct).ConfigureAwait(false);
        }
        catch (System.Exception)
        {
            return null;
        }

        var result = new List<IReadOnlyDictionary<string, object?>>();
        for (var i = 0; i < files.Count && i < urls.Count; i++)
        {
            result.Add(new Dictionary<string, object?>
            {
                ["url"] = urls[i],
                ["name"] = files[i].FileName,
                ["type"] = files[i].ContentType
            });
        }
        return result.Count > 0 ? result : null;
    }

    /// <summary>Uploads the captured replay frames to <c>/uploads/sdksteps</c> and builds
    /// <c>{interval, frames:[url…]}</c> (URLs, not inline base64), like the native SDKs. Null when excluded,
    /// empty, or the upload fails.</summary>
    private async Task<IReadOnlyDictionary<string, object?>?> BuildReplayAsync(
        HashSet<string> excludeKeys, CancellationToken ct)
    {
        if (excludeKeys.Contains("replays"))
        {
            return null;
        }
        var frames = _replay.Snapshot();
        if (frames.Count == 0)
        {
            return null;
        }

        var files = new List<UploadFile>();
        foreach (var frame in frames)
        {
            var decoded = DecodeUpload(frame, "replay.png", "image/png");
            if (decoded != null)
            {
                files.Add(decoded);
            }
        }
        if (files.Count == 0)
        {
            return null;
        }

        IReadOnlyList<string> urls;
        try
        {
            urls = await _api.UploadImagesAsync("sdksteps", files, _session.GleapId, _session.GleapHash, ct).ConfigureAwait(false);
        }
        catch (System.Exception)
        {
            return null;
        }
        if (urls.Count == 0)
        {
            return null;
        }

        return new Dictionary<string, object?>
        {
            ["interval"] = ReplayIntervalMs ?? _replay.IntervalMs,
            ["frames"] = urls
        };
    }

    /// <summary>Decodes a data-URI or raw base64 string into an <see cref="UploadFile"/>, deriving the
    /// content type from the data-URI header when present. Returns null on malformed base64.</summary>
    private static UploadFile? DecodeUpload(string? content, string fileName, string fallbackContentType)
    {
        if (string.IsNullOrEmpty(content))
        {
            return null;
        }
        const string prefix = "data:";
        if (content!.StartsWith(prefix, System.StringComparison.Ordinal))
        {
            var comma = content.IndexOf(',');
            if (comma < 0)
            {
                return null;
            }
            var contentType = content.Substring(prefix.Length, comma - prefix.Length).Split(';')[0];
            if (string.IsNullOrEmpty(contentType))
            {
                contentType = fallbackContentType;
            }
            var payload = DecodeBase64(content.Substring(comma + 1));
            return payload == null ? null : new UploadFile(payload, fileName, contentType);
        }

        var raw = DecodeBase64(content);
        return raw == null ? null : new UploadFile(raw, fileName, fallbackContentType);
    }

    private static byte[]? DecodeBase64(string value)
    {
        try
        {
            return System.Convert.FromBase64String(value);
        }
        catch (System.FormatException)
        {
            return null;
        }
    }

    private static string MimeFromName(string fileName)
    {
        var dot = fileName.LastIndexOf('.');
        var ext = dot >= 0 ? fileName.Substring(dot + 1).ToUpperInvariant() : string.Empty;
        return ext switch
        {
            "PNG" => "image/png",
            "JPG" or "JPEG" => "image/jpeg",
            "GIF" => "image/gif",
            "WEBP" => "image/webp",
            "PDF" => "application/pdf",
            "TXT" or "LOG" => "text/plain",
            "JSON" => "application/json",
            "CSV" => "text/csv",
            _ => "application/octet-stream"
        };
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
        Phone = _session.Identity?.Phone,
        Plan = _session.Identity?.Plan,
        CompanyName = _session.Identity?.CompanyName,
        CompanyId = _session.Identity?.CompanyId,
        Avatar = _session.Identity?.Avatar,
        Value = _session.Identity?.Value,
        Sla = _session.Identity?.Sla,
        PreFillFormData = _prefill,
        AiTools = _aiTools
    };

    /// <summary>Opens the messenger. Idempotent: a no-op when already open, so the navigation methods and
    /// the platform host's own open path can both call it without double-firing <c>widgetOpened</c>.</summary>
    public void Open()
    {
        if (_widgetOpen)
        {
            return;
        }
        Bridge.Send(new GleapBridgeMessage
        {
            Name = "widget-status-update",
            Data = new System.Collections.Generic.Dictionary<string, object> { ["isWidgetOpen"] = true }
        });
        _widgetOpen = true;
        _events.Emit("widgetOpened");
    }

    /// <summary>Closes the messenger. Unlike <see cref="Open"/> this is not guarded: closing an
    /// already-closed widget still reports <c>widgetClosed</c>, and there is no re-entrant path here (the
    /// host's close handler only hides its panel; it does not call back into Close).</summary>
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
        var queued = _streamedEventLog.Snapshot();
        var events = new List<object?>();
        foreach (var e in queued)
        {
            events.Add(new Dictionary<string, object?> { ["name"] = e.Name, ["data"] = e.Data, ["date"] = e.Date });
        }

        var time = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var ws = _d.Realtime?.IsConnected == true;
        var response = await _api.PingAsync(time, events, _widgetOpen, ws, _session.GleapId, _session.GleapHash, ct)
            .ConfigureAwait(false);
        // Retire exactly what was sent, and only once the ping actually succeeded: an exception above
        // leaves the queue intact so the next cycle retries instead of losing events silently, and
        // dropping just the sent prefix keeps anything tracked while the request was in flight.
        _streamedEventLog.RemoveFirst(queued.Count);

        // In ws mode the server deliberately answers with an empty body — outbound actions and the unread
        // count are pushed over the socket instead. Parsing that empty response would yield unreadCount 0
        // and clobber the count the socket just delivered, so the ping is events-only here (iOS guards the
        // same way with `if (!self.webSocketEnabled)`).
        if (!ws)
        {
            ProcessUpdate(response);
        }
    }

    /// <summary>Dispatches an outbound update (from the poll response or a WebSocket <c>update</c> frame):
    /// raises the unread count and each outbound action, and auto-starts survey/feedback-flow actions.
    /// Banner/modal/notification actions are surfaced via <c>outboundSent</c> for the host to render.</summary>
    private void ProcessUpdate(PingResponse response)
    {
        // The unread count always applies, even while the widget is open (iOS updates it outside its gate).
        _events.Emit("notificationCountUpdated", response.UnreadCount);

        // Outbound actions are only dispatched while the widget is closed, matching iOS, which wraps its
        // whole action loop in `if (![Gleap isOpened])`. Without this an outbound survey could hijack an
        // open conversation.
        if (_widgetOpen)
        {
            return;
        }

        foreach (var action in response.Actions)
        {
            _events.Emit("outboundSent", new Dictionary<string, object?>
            {
                ["actionType"] = action.ActionType,
                ["outboundId"] = action.OutboundId,
                // Raw action JSON so the platform host can answer banner-data / modal-data / notification.
                ["data"] = action.Data.ValueKind == JsonValueKind.Undefined ? null : action.Data.GetRawText()
            });

            switch (action.ActionType)
            {
                case "":
                    break;
                case "notification":
                case "banner":
                case "modal":
                    // Surfaced via outboundSent above; the platform host renders these.
                    break;
                case "tour":
                    // Product tours are rendered by the web SDK and are out of scope here. Explicitly
                    // ignored so they never fall through and get started as a survey (which iOS does).
                    break;
                default:
                    // Everything else IS a survey / feedback flow. There is no "survey" actionType: the
                    // server puts the flow's own action id in actionType and the format alongside it
                    // (Server outboundactions/controller.ts). Both references dispatch this the same way,
                    // as an else-fallthrough passing actionType as the flow id.
                    Bridge.Send(WidgetCommands.StartSurvey(action.ActionType, ReadSurveyFormat(action.Data)));
                    break;
            }
        }
    }

    /// <summary>Reads the outbound action's <c>format</c> (a sibling of <c>actionType</c>, not part of
    /// <c>data</c>); anything other than <c>survey_full</c> is the standard survey card.</summary>
    private static SurveyFormat ReadSurveyFormat(JsonElement action) =>
        action.TryGetProperty("format", out var f) && f.ValueKind == JsonValueKind.String
            && f.GetString() == "survey_full"
                ? SurveyFormat.SurveyFull
                : SurveyFormat.Survey;

    /// <summary>Parses a WebSocket frame and dispatches <c>update</c> frames through <see cref="ProcessUpdate"/>.
    /// Runs on the realtime channel's background thread; event handlers marshal to their own thread.</summary>
    private void OnRealtimeMessage(string rawJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return;
            }
            var name = root.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String
                ? n.GetString() : null;
            if (name == "update" && root.TryGetProperty("data", out var data))
            {
                ProcessUpdate(PingResponse.Parse(data));
            }
            else if (name == "checklist" && root.TryGetProperty("data", out var checklistData))
            {
                ProcessChecklistUpdate(GleapChecklistUpdate.FromFrameData(checklistData));
            }
        }
        catch (JsonException)
        {
            // Ignore malformed / non-JSON frames (e.g. keepalive echoes).
        }
    }

    /// <summary>Raises the live-checklist events for a server <c>checklist</c> frame: the raw progress
    /// update, one event per step completed by this update, and a completion event once every step is done.
    /// Not gated on the widget being open (matching the server/JS behaviour).</summary>
    private void ProcessChecklistUpdate(GleapChecklistUpdate? update)
    {
        if (update == null)
        {
            return;
        }

        _events.Emit("checklistUpdated", new Dictionary<string, object?>
        {
            ["checklistId"] = update.ChecklistId,
            ["outboundId"] = update.OutboundId,
            ["status"] = update.Status,
            ["completedSteps"] = update.CompletedSteps,
            ["totalSteps"] = update.Steps.Count
        });

        foreach (var step in update.NewlyCompletedSteps())
        {
            _events.Emit("checklistStepCompleted", new Dictionary<string, object?>
            {
                ["checklistId"] = update.ChecklistId,
                ["outboundId"] = update.OutboundId,
                ["stepId"] = step.Id,
                ["stepIndex"] = step.Index,
                ["stepTitle"] = step.Title,
                ["completedSteps"] = update.CompletedSteps,
                ["status"] = update.Status
            });
        }

        if (update.IsCompleted)
        {
            _events.Emit("checklistCompleted", new Dictionary<string, object?>
            {
                ["checklistId"] = update.ChecklistId,
                ["outboundId"] = update.OutboundId,
                ["completedSteps"] = update.CompletedSteps,
                ["status"] = update.Status
            });
        }
    }

    /// <summary>Builds the realtime connection URL with the current session identity (matches the native
    /// SDKs' query-param auth).</summary>
    private string BuildRealtimeUrl()
    {
        static string Esc(string? s) => System.Uri.EscapeDataString(s ?? "");
        return $"{_d.Endpoints.WsUrl}?gleapId={Esc(_session.GleapId)}&gleapHash={Esc(_session.GleapHash)}"
            + $"&apiKey={Esc(_token)}&sdkVersion={Esc(_d.SdkVersion)}";
    }

    // Every navigation command also reveals the messenger, mirroring the reference SDKs where each of
    // these ends in showWidget() (JS) / a widget presentation (iOS). Without it the host would send the
    // command to a widget the user cannot see. Open() is idempotent, so the host's own open path (which
    // calls Open() itself) does not double-fire.
    private void Navigate(GleapBridgeMessage command)
    {
        Bridge.Send(command);
        Open();
    }

    public void StartConversation(bool showBackButton) => Navigate(WidgetCommands.StartConversation(showBackButton));
    public void StartBot(string botId, bool showBackButton) => Navigate(WidgetCommands.StartBot(botId, showBackButton));
    public void OpenConversation(string shareToken) => Navigate(WidgetCommands.OpenConversation(shareToken));
    public void OpenHelpCenter(bool showBackButton) => Navigate(WidgetCommands.OpenHelpCenter(showBackButton));
    public void OpenNews(bool showBackButton) => Navigate(WidgetCommands.OpenNews(showBackButton));
    public void ShowSurvey(string surveyId, SurveyFormat format) => Navigate(WidgetCommands.StartSurvey(surveyId, format));
    public void OpenConversations(bool showBackButton) => Navigate(WidgetCommands.OpenConversations(showBackButton));
    public void StartClassicForm(string formId, bool showBackButton) => Navigate(WidgetCommands.StartClassicForm(formId, showBackButton));
    public void OpenHelpCenterArticle(string articleId, bool showBackButton) => Navigate(WidgetCommands.OpenHelpCenterArticle(articleId, showBackButton));
    public void OpenHelpCenterCollection(string collectionId, bool showBackButton) => Navigate(WidgetCommands.OpenHelpCenterCollection(collectionId, showBackButton));
    public void SearchHelpCenter(string term, bool showBackButton) => Navigate(WidgetCommands.SearchHelpCenter(term, showBackButton));
    public void OpenNewsArticle(string articleId, bool showBackButton) => Navigate(WidgetCommands.OpenNewsArticle(articleId, showBackButton));
    public void OpenFeatureRequests(bool showBackButton) => Navigate(WidgetCommands.OpenFeatureRequests(showBackButton));
    public void OpenChecklists(bool showBackButton) => Navigate(WidgetCommands.OpenChecklists(showBackButton));
    public void OpenChecklist(string checklistId, bool showBackButton) => Navigate(WidgetCommands.OpenChecklist(checklistId, showBackButton));
    public void StartChecklist(string outboundId, bool showBackButton) => Navigate(WidgetCommands.StartChecklist(outboundId, showBackButton));
    public void AskAI(string question, bool showBackButton) => Navigate(WidgetCommands.AskAI(question, showBackButton));

    public async Task IdentifyContactAsync(string userId, GleapUserProperty? properties, string? userHash, CancellationToken ct)
    {
        await _session.IdentifyAsync(userId, properties ?? new GleapUserProperty(), userHash, ct).ConfigureAwait(false);
        _bootstrapper.SendSessionUpdate();
        _d.Realtime?.Connect(BuildRealtimeUrl());   // reconnect with the identified session
    }

    public async Task UpdateContactAsync(GleapUserProperty properties, CancellationToken ct)
    {
        await _session.UpdateContactAsync(properties, ct).ConfigureAwait(false);
        _bootstrapper.SendSessionUpdate();
    }

    public async Task ClearIdentityAsync(CancellationToken ct)
    {
        _session.ClearIdentity();
        await _session.StartAsync(_language, _d.DeviceType, ct).ConfigureAwait(false);
        LogEvent("sessionStarted", null);   // a fresh guest session is a new session
        _bootstrapper.SendSessionUpdate();
        _d.Realtime?.Connect(BuildRealtimeUrl());   // reconnect with the fresh guest session
    }

    public bool IsUserIdentified() => _session.IsIdentified;
    public GleapUserProperty? GetIdentity() => _session.Identity;

    /// <summary>The raw <c>flowConfig</c> JSON from <c>/config</c> (widget appearance: colors, logo, …),
    /// or <c>"{}"</c> before initialization. Platform hosts use it to style native chrome such as the
    /// launcher button to match the project's configured widget.</summary>
    public string FlowConfigJson => _config?.FlowConfigJson ?? "{}";

    public void Log(string message, LogLevel level) => _consoleLog.Add(message, level);
    /// <summary>Records an event into both the report log (kept for the whole session) and the outbound
    /// queue (drained by the next ping).</summary>
    private void LogEvent(string name, object? data)
    {
        _eventLog.Add(name, data);
        _streamedEventLog.Add(name, data);
    }

    public void TrackEvent(string name, object? data) => LogEvent(name, data);

    public void TrackPage(string pageName)
    {
        // Doubles as the report's metaData.lastScreenName (hosts skip tracking while the widget is open,
        // so this stays the screen the user was on before opening it — the iOS semantics).
        _lastScreenName = pageName;
        LogEvent("pageView", new Dictionary<string, object> { ["page"] = pageName });
    }
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

    /// <summary>
    /// Returns a <see cref="System.Net.Http.DelegatingHandler"/> that records outbound HTTP into the
    /// Gleap network log so it appears on submitted tickets. Route your app's HttpClient through it —
    /// <c>new HttpClient(gleap.CreateNetworkLoggingHandler())</c>, or pass it to
    /// <c>IHttpClientBuilder.AddHttpMessageHandler</c>. Managed .NET has no global HTTP interception,
    /// so only traffic sent through this handler is captured (the native SDKs capture automatically).
    /// </summary>
    public System.Net.Http.DelegatingHandler CreateNetworkLoggingHandler(
        System.Net.Http.HttpMessageHandler? innerHandler = null)
        => new GleapHttpHandler(_networkLog, _clock)
        {
            InnerHandler = innerHandler ?? new System.Net.Http.HttpClientHandler()
        };

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
