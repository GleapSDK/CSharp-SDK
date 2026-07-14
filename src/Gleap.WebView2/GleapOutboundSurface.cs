using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using GleapSDK.Http;

namespace GleapSDK.WebView2;

/// <summary>
/// Hosts one Gleap outbound <b>banner</b> or <b>modal</b> in a composition WebView2, speaking the
/// <c>outboundmedia.gleap.io</c> protocol exactly as the native iOS/Android SDKs do:
/// host→page via <c>appMessage(...)</c>; page→host via <c>GleapBannerJSBridge.gleapBannerCallback</c> /
/// <c>GleapModalJSBridge.gleapModalCallback</c>. Banner pins to the top full-width; modal is a centred
/// card over a 50% scrim. Action triggers drive the messenger via the <see cref="Gleap"/> facade.
/// </summary>
internal sealed class GleapOutboundSurface : IDisposable
{
    private readonly bool _isModal;
    private readonly string _actionJson;      // raw outbound action (banner-data is forwarded verbatim)
    private readonly string _flowConfigJson;  // project config → modal primaryColor/backgroundColor
    private readonly Panel _host;
    private readonly Action _onClosed;
    private readonly Action _openMessenger;
    private readonly WebView2Channel _channel;
    private readonly Microsoft.Web.WebView2.Wpf.WebView2CompositionControl _webView;
    private readonly Border _container;
    private readonly Border? _scrim;
    private bool _shown;
    private bool _disposed;

    private const double ModalWidth = 400;
    private const double ModalCorner = 18;

    public GleapOutboundSurface(Panel host, bool isModal, string actionJson, string flowConfigJson,
        GleapEndpoints endpoints, Action onClosed, Action openMessenger)
    {
        _host = host;
        _isModal = isModal;
        _actionJson = string.IsNullOrWhiteSpace(actionJson) ? "{}" : actionJson;
        _flowConfigJson = string.IsNullOrWhiteSpace(flowConfigJson) ? "{}" : flowConfigJson;
        _onClosed = onClosed;
        _openMessenger = openMessenger;

        _webView = new Microsoft.Web.WebView2.Wpf.WebView2CompositionControl();

        if (isModal)
        {
            _scrim = new Border { Background = new SolidColorBrush(Color.FromArgb(0x80, 0, 0, 0)), Opacity = 0 };
            _scrim.MouseLeftButtonDown += (_, _) => { if (ShowCloseButton()) { Close(); } };
            _container = new Border
            {
                Width = ModalWidth,
                Height = 480,
                MaxHeight = 680,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                CornerRadius = new CornerRadius(ModalCorner),
                Background = Brushes.White,
                Opacity = 0,
                Effect = new DropShadowEffect { BlurRadius = 30, ShadowDepth = 0, Opacity = 0.28 },
                Child = _webView
            };
            _webView.Clip = new RectangleGeometry(new Rect(0, 0, ModalWidth, 480), ModalCorner, ModalCorner);
        }
        else
        {
            // Banner: pinned to the top, full width; height driven by banner-height.
            _container = new Border
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Top,
                Height = 72,
                Opacity = 0,
                Child = _webView
            };
        }

        var (bridgeObject, callback) = isModal
            ? ("GleapModalJSBridge", "gleapModalCallback")
            : ("GleapBannerJSBridge", "gleapBannerCallback");
        _channel = new WebView2Channel(_webView, bridgeObject, callback);
        _channel.MessageReceived += OnMessage;

        if (_scrim != null)
        {
            host.Children.Add(_scrim);
        }
        host.Children.Add(_container);

        var url = isModal ? endpoints.OutboundUrl.TrimEnd('/') + "/modal" : endpoints.OutboundUrl;
        _ = InitAsync(url);
    }

    private async Task InitAsync(string url)
    {
        await _channel.InitializeAsync().ConfigureAwait(true);
        _channel.Navigate(url);
    }

    private void OnMessage(string raw)
    {
        string? name;
        JsonElement data;
        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            name = root.TryGetProperty("name", out var n) ? n.GetString() : null;
            data = root.TryGetProperty("data", out var d) ? d.Clone() : default;
        }
        catch
        {
            return;
        }

        switch (name)
        {
            case "banner-loaded":
            case "modal-loaded":
                SendData();
                break;
            case "banner-data-set":
            case "modal-data-set":
                AnimateIn();
                break;
            case "banner-height":
            case "modal-height":
                if (data.ValueKind == JsonValueKind.Object && data.TryGetProperty("height", out var h) && h.TryGetDouble(out var height))
                {
                    SetHeight(height);
                    AnimateIn();   // some builds reveal via height rather than *-data-set
                }
                break;
            case "banner-close":
            case "modal-close":
                Close();
                break;
            case "open-url":
                if (data.ValueKind == JsonValueKind.String)
                {
                    GleapWebView2Host.OpenExternalUrl(data.GetString());
                }
                break;
            default:
                if (!string.IsNullOrEmpty(name))
                {
                    HandleAction(name!, data);
                }
                break;
        }
    }

    private void SendData()
    {
        string dataJson = _actionJson;   // banner forwards the whole action verbatim
        if (_isModal)
        {
            try
            {
                var action = JsonNode.Parse(_actionJson) as JsonObject;
                var config = (action?["config"] as JsonObject)?.DeepClone() as JsonObject
                    ?? (action?.DeepClone() as JsonObject) ?? new JsonObject();
                var cfg = JsonNode.Parse(_flowConfigJson) as JsonObject;
                config["primaryColor"] = (cfg?["color"]?.DeepClone()) ?? JsonValue.Create("#485BFF");
                config["backgroundColor"] = (cfg?["backgroundColor"]?.DeepClone()) ?? JsonValue.Create("#FFFFFF");
                dataJson = config.ToJsonString();
            }
            catch
            {
                dataJson = _actionJson;
            }
        }
        var msgName = _isModal ? "modal-data" : "banner-data";
        _channel.ExecuteJavaScript("appMessage({\"name\":\"" + msgName + "\",\"data\":" + dataJson + "});");
    }

    private void SetHeight(double reported)
    {
        if (_isModal)
        {
            var max = 0.9 * (_host.ActualHeight > 0 ? _host.ActualHeight : 800);
            var hh = Math.Min(reported, max);
            _container.Height = hh;
            _webView.Clip = new RectangleGeometry(new Rect(0, 0, ModalWidth, hh), ModalCorner, ModalCorner);
        }
        else
        {
            _container.Height = Math.Max(reported, 40);
        }
    }

    private void AnimateIn()
    {
        if (_shown || _disposed)
        {
            return;
        }
        _shown = true;
        var fade = new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(250)))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        _container.BeginAnimation(UIElement.OpacityProperty, fade);
        _scrim?.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    private void HandleAction(string name, JsonElement data)
    {
        string? Str(string key) =>
            data.ValueKind == JsonValueKind.Object && data.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String
                ? v.GetString() : null;

        var handled = true;
        switch (name)
        {
            case "start-conversation": Gleap.StartBot(Str("botId") ?? ""); break;
            case "show-form": Gleap.StartClassicForm(Str("formId") ?? ""); break;
            case "show-survey": Gleap.ShowSurvey(Str("formId") ?? "", Str("surveyFormat") == "survey_full" ? SurveyFormat.SurveyFull : SurveyFormat.Survey); break;
            case "show-news-article": Gleap.OpenNewsArticle(Str("articleId") ?? ""); break;
            case "show-help-article": Gleap.OpenHelpCenterArticle(Str("articleId") ?? ""); break;
            case "show-checklist": Gleap.StartChecklist(Str("checklistId") ?? ""); break;
            case "start-custom-action": handled = false; break; // app-defined; surfaced as a no-op here
            default: handled = false; break;
        }

        if (handled)
        {
            _openMessenger();
        }
        if (_isModal)
        {
            Close();   // the modal self-dismisses on an action; the banner stays (native behavior)
        }
    }

    private bool ShowCloseButton()
    {
        try
        {
            var action = JsonNode.Parse(_actionJson) as JsonObject;
            var config = action?["config"] as JsonObject ?? action;
            return config?["showCloseButton"]?.GetValue<bool>() ?? true;
        }
        catch
        {
            return true;
        }
    }

    public void Close() => Dispose();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _host.Children.Remove(_container);
        if (_scrim != null)
        {
            _host.Children.Remove(_scrim);
        }
        _webView.Dispose();
        _onClosed();
    }
}
