using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GleapSDK.Bridge;
using GleapSDK.Collection;
using GleapSDK.Data;
using GleapSDK.Http;
using GleapSDK.Metadata;
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
    }

    private readonly Dependencies _d;
    private WebViewBridge _bridge = null!;
    private SessionManager _session = null!;
    private ConfigManager _config = null!;
    private string _token = "";
    private readonly ConsoleLogBuffer _consoleLog;
    private readonly EventBuffer _eventLog;
    private readonly NetworkLogBuffer _networkLog;
    private readonly CustomDataStore _customData = new();
    private readonly TicketAttributeStore _ticketAttributes = new();
    private readonly TagStore _tags = new();
    private readonly AttachmentStore _attachments = new();
    private readonly SessionDataCollector _collector;

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
            new DefaultMetadataProvider("NET/Windows", "0.1.0"));
    }

    private WebViewBridge Bridge => _bridge ?? throw new System.InvalidOperationException(
        "Gleap is not initialized. Call InitializeAsync before using the messenger.");

    public async Task InitializeAsync(string token, CancellationToken ct)
    {
        _token = token;
        var api = new ApiClient(_d.Http, _d.Json, _d.Endpoints, token);
        _session = new SessionManager(api, _d.Store);
        _config = new ConfigManager(api);

        _bridge = new WebViewBridge(_d.Channel, _d.Json);
        _ = new WidgetBootstrapper(_bridge, BuildSnapshot);

        _bridge.CollectTicketDataRequested += () =>
            _bridge.Send(new GleapBridgeMessage { Name = "collect-ticket-data", Data = _collector.BuildTicketData() });

        await _session.StartAsync("en", "desktop", ct).ConfigureAwait(false);
        await _config.LoadAsync("en", ct).ConfigureAwait(false);
    }

    private SessionSnapshot BuildSnapshot() => new()
    {
        SdkKey = _token,
        ApiUrl = _d.Endpoints.ApiUrl,
        GleapId = _session.GleapId,
        GleapHash = _session.GleapHash,
        FlowConfigJson = _config.FlowConfigJson,
        ProjectActionsJson = _config.ProjectActionsJson,
        Language = "en"
    };

    public void Open() => Bridge.Send(new GleapBridgeMessage
    {
        Name = "widget-status-update",
        Data = new System.Collections.Generic.Dictionary<string, object> { ["isWidgetOpen"] = true }
    });

    public void Close() => Bridge.Send(new GleapBridgeMessage
    {
        Name = "widget-status-update",
        Data = new System.Collections.Generic.Dictionary<string, object> { ["isWidgetOpen"] = false }
    });

    public void StartConversation(bool showBackButton) => Bridge.Send(WidgetCommands.StartConversation(showBackButton));
    public void StartBot(string botId, bool showBackButton) => Bridge.Send(WidgetCommands.StartBot(botId, showBackButton));
    public void OpenConversation(string shareToken) => Bridge.Send(WidgetCommands.OpenConversation(shareToken));
    public void OpenHelpCenter(bool showBackButton) => Bridge.Send(WidgetCommands.OpenHelpCenter(showBackButton));
    public void OpenNews(bool showBackButton) => Bridge.Send(WidgetCommands.OpenNews(showBackButton));
    public void ShowSurvey(string surveyId, SurveyFormat format) => Bridge.Send(WidgetCommands.StartSurvey(surveyId, format));

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
}
