using System.Threading;
using System.Threading.Tasks;
using GleapSDK.Bridge;
using GleapSDK.Http;
using GleapSDK.Serialization;
using GleapSDK.Session;

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

    public ManagedBackend(Dependencies dependencies) => _d = dependencies;

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
}
