using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using GleapSDK.Outbound;

namespace GleapSDK.WebView2;

/// <summary>
/// A drop-in WPF control that reproduces the JavaScript-SDK experience on the desktop: a round
/// floating launcher button that slides the Gleap messenger in as a rounded bottom-right overlay,
/// and slides it back out on close. Host apps place this control on top of their own UI and set
/// <see cref="SdkKey"/> — nothing else is required.
///
/// <para>The messenger is hosted in a <see cref="Microsoft.Web.WebView2.Wpf.WebView2CompositionControl"/>
/// (airspace-free composition rendering), so it supports true rounded corners, a smooth slide/fade
/// animation and correct input — matching the web widget rather than a windowed overlay. The session
/// is preloaded (invisibly) when the control loads, so the first open is instant and the unread badge
/// is live before opening.</para>
///
/// <code>
/// var messenger = new GleapMessenger { SdkKey = "YOUR_SDK_KEY" };
/// rootGrid.Children.Add(messenger);
/// </code>
/// After load the static <see cref="Gleap"/> facade drives the same messenger
/// (<c>Gleap.StartBot("")</c>, <c>Gleap.OpenHelpCenter()</c>, …).
/// </summary>
public class GleapMessenger : Grid, IDisposable
{
    private static readonly Duration SlideDuration = new(TimeSpan.FromMilliseconds(280));
    private const double CornerRadius = 16;
    private const double SlideDistance = 44;

    private readonly Microsoft.Web.WebView2.Wpf.WebView2CompositionControl _webView;
    private readonly Grid _overlay;
    private readonly Border _shadowLayer;
    private readonly TranslateTransform _panelSlide;
    private readonly Border _launcher;
    private readonly ScaleTransform _launcherScale;
    private readonly Viewbox _chatIcon;
    private readonly FrameworkElement _closeIcon;
    private readonly Border _badge;
    private readonly TextBlock _badgeText;
    private DispatcherTimer? _pollTimer;
    private DispatcherTimer? _replayTimer;
    private DispatcherTimer? _pageTimer;
    private DispatcherTimer? _initRetryTimer;
    private int _initAttempts;
    private string? _lastPageName;

    private const double InitRetryBaseSeconds = 5;
    private const double InitRetryCeilingSeconds = 60;
    private ManagedBackend? _backend;
    private bool _initializing;
    private bool _isOpen;
    private bool _disposed;
    private GleapSDK.Http.GleapEndpoints _endpoints = GleapSDK.Http.GleapEndpoints.Default;
    private GleapOutboundSurface? _banner;
    private GleapOutboundSurface? _modal;
    private GleapNotificationStack? _notifications;

    // Held so Dispose can Gleap.RemoveListener them: the dispatcher removes by delegate reference,
    // and these lambdas capture `this`, so leaving them registered leaks the disposed control.
    private Action<object?>? _onWidgetOpened;
    private Action<object?>? _onWidgetClosed;
    private Action<object?>? _onFeedbackButtonVisibilityChanged;
    private Action<object?>? _onNotificationCountUpdated;
    private Action<object?>? _onOutboundSent;

    /// <summary>Project SDK key. Set before the control loads (e.g. in the host's constructor).</summary>
    public string? SdkKey { get; set; }

    /// <summary>Width of the messenger overlay panel (default 400).</summary>
    public double PanelWidth { get; set; } = 400;

    /// <summary>Height of the messenger overlay panel (default 680; clamped to the window).</summary>
    public double PanelHeight { get; set; } = 680;

    /// <summary>Launcher fill colour (default Gleap blue). Customize to match your brand.</summary>
    public Brush LauncherBackground { get; set; } = new SolidColorBrush(Color.FromRgb(0x48, 0x5B, 0xFF));

    /// <summary>Poll <c>/sessions/ping</c> on a timer so the unread badge stays live (default true).</summary>
    public bool EnableOutboundPolling { get; set; } = true;

    /// <summary>Outbound poll interval (default 5s).</summary>
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Automatically track the active window as a <c>pageView</c> event (default true), mirroring
    /// the native SDKs' screen tracking. Set false to report screens yourself via
    /// <see cref="Gleap.TrackPage"/>.</summary>
    public bool EnablePageTracking { get; set; } = true;

    /// <summary>The backend created on preload, or null until then.</summary>
    public ManagedBackend? Backend => _backend;

    /// <summary>True while the messenger overlay is shown.</summary>
    public bool IsMessengerVisible => _isOpen;

    /// <summary>Raised once the Gleap session is initialized and the <see cref="Gleap"/> facade is ready
    /// (e.g. to <c>IdentifyContact</c> or <c>TrackEvent</c>). Fires on the UI thread.</summary>
    public event EventHandler? Ready;

    public GleapMessenger()
    {
        _webView = new Microsoft.Web.WebView2.Wpf.WebView2CompositionControl
        {
            Width = PanelWidth,
            Height = PanelHeight,
            // Airspace-free composition rendering honours this clip, giving true rounded corners.
            Clip = new RectangleGeometry(new Rect(0, 0, PanelWidth, PanelHeight), CornerRadius, CornerRadius)
        };

        _panelSlide = new TranslateTransform(0, SlideDistance);
        // The shadow lives on its own rounded white layer (NOT the WebView2 host): a DropShadowEffect
        // over a composition control samples the full rectangle and yields a square shadow, so we
        // cast it from this plain rounded Border instead. The WebView (rounded clip) sits on top and
        // covers it, leaving only the rounded glow bleeding out around the edges.
        _shadowLayer = new Border
        {
            CornerRadius = new CornerRadius(CornerRadius),
            Background = Brushes.White,
            Effect = new DropShadowEffect { BlurRadius = 22, ShadowDepth = 0, Opacity = 0.20 }
        };
        _overlay = new Grid
        {
            Width = PanelWidth,
            Height = PanelHeight,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 28, 84),   // sits above the launcher; room for the glow
            Opacity = 0,
            IsHitTestVisible = false,
            RenderTransform = _panelSlide,
            Children = { _shadowLayer, _webView }
        };

        _chatIcon = MakeIcon(new Path
        {
            Fill = Brushes.White,
            Data = Geometry.Parse("M6,3 H22 A4,4 0 0 1 26,7 V17 A4,4 0 0 1 22,21 H14 L9,26 V21 H6 A4,4 0 0 1 2,17 V7 A4,4 0 0 1 6,3 Z")
        }, 26);
        _closeIcon = MakeCloseIcon();
        _closeIcon.Opacity = 0;

        _launcherScale = new ScaleTransform(1, 1);
        _launcher = new Border
        {
            Background = LauncherBackground,
            CornerRadius = new CornerRadius(26),
            Width = 52,
            Height = 52,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 28, 24),
            Cursor = Cursors.Hand,
            Effect = new DropShadowEffect { BlurRadius = 14, ShadowDepth = 2, Opacity = 0.28 },
            RenderTransform = _launcherScale,
            RenderTransformOrigin = new Point(0.5, 0.5),
            Child = new Grid { Children = { _chatIcon, _closeIcon } }
        };
        _launcher.MouseLeftButtonUp += (_, _) => Toggle();
        _launcher.MouseEnter += (_, _) => AnimateLauncherScale(1.08);
        _launcher.MouseLeave += (_, _) => AnimateLauncherScale(1.0);

        _badgeText = new TextBlock
        {
            Foreground = Brushes.White,
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(5, 0, 5, 0)
        };
        _badge = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0xE5, 0x3E, 0x3E)),
            CornerRadius = new CornerRadius(10),
            MinWidth = 20,
            Height = 20,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 22, 54),
            Visibility = Visibility.Collapsed,
            Child = _badgeText,
            IsHitTestVisible = false
        };

        Children.Add(_overlay);
        Children.Add(_launcher);
        Children.Add(_badge);

        Loaded += OnLoaded;
        SizeChanged += (_, _) => ResizePanel();   // shrink to fit when the window is short
    }

    private static Viewbox MakeIcon(UIElement child, double size) => new()
    {
        Width = size,
        Height = size,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        Child = child
    };

    // An "X" built from two centred, rotated bars — guaranteed dead-centre regardless of geometry
    // bounds (a stroked Path in a Viewbox mis-centres once you scale/rotate it).
    private static Grid MakeCloseIcon()
    {
        var g = new Grid
        {
            Width = 20,
            Height = 20,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        g.Children.Add(MakeBar(45));
        g.Children.Add(MakeBar(-45));
        return g;
    }

    private static Rectangle MakeBar(double angle) => new()
    {
        Width = 18,
        Height = 2.2,
        RadiusX = 1.1,
        RadiusY = 1.1,
        Fill = Brushes.White,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        RenderTransformOrigin = new Point(0.5, 0.5),
        RenderTransform = new RotateTransform(angle)
    };

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_backend == null && !_initializing && !string.IsNullOrEmpty(SdkKey))
        {
            await TryInitializeAsync().ConfigureAwait(true);
        }
    }

    /// <summary>Initializes, and on a transient failure schedules a retry. Desktop apps run for hours, so
    /// being offline for the few seconds around startup must not disable Gleap for the whole process
    /// lifetime — which is what happened before, since nothing ever retried.</summary>
    private async Task TryInitializeAsync()
    {
        try
        {
            await InitializeAsync().ConfigureAwait(true);
            _initRetryTimer?.Stop();
            _initRetryTimer = null;
            _initAttempts = 0;
        }
        catch (GleapSDK.Http.GleapApiException ex) when (ex.StatusCode == 429)
        {
            // Rate limited: retrying only makes it worse. The next open() still tries again.
        }
        catch (GleapSDK.Http.GleapApiException ex) when (ex.StatusCode is >= 400 and < 500)
        {
            // Bad key / rejected project — retrying cannot fix it.
            Debug.WriteLine("Gleap: initialization rejected (" + ex.StatusCode + "); not retrying.");
        }
        catch
        {
            ScheduleInitRetry();   // offline or a transient server error
        }
    }

    /// <summary>Retries initialization with exponential backoff (5s doubling to a 60s ceiling).</summary>
    private void ScheduleInitRetry()
    {
        if (_disposed || _backend != null)
        {
            return;
        }
        _initAttempts++;
        var seconds = Math.Min(InitRetryCeilingSeconds, InitRetryBaseSeconds * Math.Pow(2, _initAttempts - 1));

        _initRetryTimer?.Stop();
        _initRetryTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
        _initRetryTimer.Tick += async (_, _) =>
        {
            _initRetryTimer?.Stop();
            if (_disposed || _backend != null || _initializing)
            {
                return;
            }
            await TryInitializeAsync().ConfigureAwait(true);
        };
        _initRetryTimer.Start();
    }

    /// <summary>Toggles the messenger open/closed (what the launcher button does).</summary>
    public void Toggle()
    {
        if (_isOpen)
        {
            HideMessenger();
            Gleap.Close();
        }
        else
        {
            ShowMessenger();
        }
    }

    /// <summary>Opens the messenger overlay (initializing the session if preload has not finished).</summary>
    public void ShowMessenger() => _ = OpenAsync();

    private async Task OpenAsync()
    {
        if (_disposed || _isOpen || string.IsNullOrEmpty(SdkKey))
        {
            return;
        }
        try
        {
            if (_backend == null)
            {
                await InitializeAsync().ConfigureAwait(true);
            }
        }
        catch
        {
            return;
        }
        if (_backend == null)
        {
            return;
        }

        // Capture the app surface for the report flow while the overlay is still hidden.
        try
        {
            await _backend.PrepareScreenshotAsync().ConfigureAwait(true);
        }
        catch
        {
            // Screenshot capture is best-effort; never block opening the messenger.
        }

        _isOpen = true;
        _badge.Visibility = Visibility.Collapsed;
        _notifications?.Clear();   // in-app notification cards clear when the messenger opens (native behavior)
        _overlay.IsHitTestVisible = true;
        AnimatePanel(open: true);
        CrossfadeLauncher(open: true);
        Gleap.Open();
    }

    /// <summary>Hides the messenger overlay and returns the launcher to its chat state.</summary>
    public void HideMessenger()
    {
        if (!_isOpen)
        {
            return;
        }
        _isOpen = false;
        _overlay.IsHitTestVisible = false;
        AnimatePanel(open: false);
        CrossfadeLauncher(open: false);
    }

    /// <summary>
    /// Creates the messenger session against <see cref="SdkKey"/> and wires it to this control.
    /// Idempotent; called automatically on load to preload the widget invisibly.
    /// </summary>
    public async Task InitializeAsync(GleapSDK.Http.GleapEndpoints? endpoints = null)
    {
        if (_backend != null || _initializing)
        {
            return;
        }
        if (string.IsNullOrEmpty(SdkKey))
        {
            throw new InvalidOperationException("GleapMessenger.SdkKey must be set before initialization.");
        }

        _initializing = true;
        try
        {
            _endpoints = endpoints ?? GleapSDK.Http.GleapEndpoints.Default;
            // Composition rendering + Opacity 0 keeps the preload invisible (no flash).
            _backend = await GleapWebView2Host.AttachAsync(_webView, SdkKey!, endpoints).ConfigureAwait(true);
            _notifications = new GleapNotificationStack(this, OnNotificationClicked);
            ApplyLauncherStyle();

            // Reveal the panel whenever anything opens the messenger — including the facade's navigation
            // API (Gleap.OpenHelpCenter(), StartBot(), …), which otherwise would only send a bridge
            // command to a widget the user never sees. ShowMessenger -> OpenAsync is a no-op once open,
            // so the launcher path (which opens first, then calls Gleap.Open()) does not recurse.
            _onWidgetOpened = _ => OnUi(ShowMessenger);
            _onWidgetClosed = _ => OnUi(HideMessenger);
            _onNotificationCountUpdated = count => OnUi(() => UpdateBadge(count));
            _onOutboundSent = d => OnUi(() => OnOutbound(d));
            _onFeedbackButtonVisibilityChanged = v => OnUi(() => SetLauncherVisible(v is true));
            Gleap.RegisterListener("widgetOpened", _onWidgetOpened);
            Gleap.RegisterListener("widgetClosed", _onWidgetClosed);
            Gleap.RegisterListener("feedbackButtonVisibilityChanged", _onFeedbackButtonVisibilityChanged);
            Gleap.RegisterListener("notificationCountUpdated", _onNotificationCountUpdated);
            Gleap.RegisterListener("outboundSent", _onOutboundSent);

            if (EnableOutboundPolling)
            {
                StartPolling();
            }

            StartReplayCaptureIfEnabled();
            StartPageTracking();

            Ready?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            _initializing = false;
        }
    }

    private void AnimatePanel(bool open)
    {
        var slide = MakeDouble(open ? SlideDistance : 0, open ? 0 : SlideDistance, open ? EasingMode.EaseOut : EasingMode.EaseIn);
        var fade = MakeDouble(open ? 0 : 1, open ? 1 : 0, EasingMode.EaseOut);
        _overlay.BeginAnimation(OpacityProperty, fade);
        _panelSlide.BeginAnimation(TranslateTransform.YProperty, slide);
    }

    private void CrossfadeLauncher(bool open)
    {
        _chatIcon.BeginAnimation(OpacityProperty, MakeDouble(open ? 1 : 0, open ? 0 : 1, EasingMode.EaseOut));
        _closeIcon.BeginAnimation(OpacityProperty, MakeDouble(open ? 0 : 1, open ? 1 : 0, EasingMode.EaseOut));
    }

    private void AnimateLauncherScale(double target)
    {
        var a = new DoubleAnimation(target, new Duration(TimeSpan.FromMilliseconds(120)))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        _launcherScale.BeginAnimation(ScaleTransform.ScaleXProperty, a);
        _launcherScale.BeginAnimation(ScaleTransform.ScaleYProperty, a);
    }

    private static DoubleAnimation MakeDouble(double from, double to, EasingMode mode) =>
        new(from, to, SlideDuration) { EasingFunction = new CubicEase { EasingMode = mode } };

    private void StartPolling()
    {
        _pollTimer = new DispatcherTimer { Interval = PollingInterval };
        _pollTimer.Tick += async (_, _) =>
        {
            try
            {
                await Gleap.CheckOutboundAsync().ConfigureAwait(true);
            }
            catch
            {
                // Polling failures are non-fatal (offline, transient server errors, …).
            }
        };
        _pollTimer.Start();
    }

    /// <summary>Starts the session-replay capture timer when the project enabled replays (flowConfig
    /// <c>enableReplays</c>/<c>replaysInterval</c>). Each tick captures one app frame into the replay ring;
    /// the frames are uploaded and referenced by URL on the next submitted report. Mirrors the native SDKs,
    /// which only run the replay timer when replays are turned on — otherwise this is a no-op.</summary>
    private void StartReplayCaptureIfEnabled()
    {
        var intervalMs = _backend?.ReplayIntervalMs;
        if (intervalMs == null)
        {
            return;
        }
        _replayTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(intervalMs.Value) };
        _replayTimer.Tick += async (_, _) =>
        {
            // Don't record the Gleap widget itself: iOS pauses replay capture while the messenger is open,
            // otherwise the ring fills with frames of our own UI and evicts the ones that matter.
            if (_isOpen)
            {
                return;
            }
            try
            {
                await _backend!.CaptureReplayFrameAsync().ConfigureAwait(true);
            }
            catch
            {
                // Replay capture failures are non-fatal (transient render/capture errors).
            }
        };
        _replayTimer.Start();
    }

    /// <summary>Polls the active window once a second and logs a <c>pageView</c> event whenever it changes,
    /// mirroring the native SDKs' 1s screen-tracking timer. Like iOS, samples are skipped while the
    /// messenger is open so the Gleap widget itself is never reported as the user's screen — which also
    /// keeps the last tracked page equal to the screen the user was on before opening it.</summary>
    private void StartPageTracking()
    {
        if (!EnablePageTracking)
        {
            return;
        }
        _pageTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _pageTimer.Tick += (_, _) => TrackCurrentPage();
        _pageTimer.Start();
        TrackCurrentPage();   // sample immediately, like the native SDKs' initial call
    }

    private void TrackCurrentPage()
    {
        if (_disposed || _isOpen)
        {
            return;
        }
        var name = CurrentScreenName();
        if (string.IsNullOrEmpty(name) || name == _lastPageName)
        {
            return;   // unchanged -> no event (native dedupe)
        }
        _lastPageName = name;
        try
        {
            Gleap.TrackPage(name!);
        }
        catch
        {
            // Never let screen tracking break the host app.
        }
    }

    /// <summary>The active window's title, falling back to its type name — the WPF equivalent of iOS's
    /// "top view controller title, else class name".</summary>
    private static string? CurrentScreenName()
    {
        var app = Application.Current;
        if (app == null)
        {
            return null;
        }
        var window = app.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? app.MainWindow;
        if (window == null)
        {
            return null;
        }
        return !string.IsNullOrWhiteSpace(window.Title) ? window.Title : window.GetType().Name;
    }

    /// <summary>Keeps the messenger at its fixed <see cref="PanelHeight"/> (it scrolls internally, like the
    /// web/native messenger), only shrinking when the window itself is too short to fit it. The widget's
    /// per-screen content height is deliberately NOT used here — that would collapse screens like the bug
    /// form whose initial content is short; content-height sizing is a survey/banner concern, not the
    /// main messenger's.</summary>
    private void ResizePanel()
    {
        double available = ActualHeight > 0 ? ActualHeight - 84 /*launcher zone*/ - 24 /*top gap*/ : PanelHeight;
        double h = Math.Round(Math.Max(220, Math.Min(PanelHeight, available)));
        if (h == _overlay.Height)
        {
            return;
        }
        _overlay.Height = h;
        _webView.Height = h;
        _webView.Clip = new RectangleGeometry(new Rect(0, 0, PanelWidth, h), CornerRadius, CornerRadius);
    }

    /// <summary>Renders an outbound banner/modal/in-app-notification (matching the native SDKs) when the
    /// poll surfaces one. Skipped while the messenger is open, mirroring the native dispatch guard.</summary>
    private void OnOutbound(object? payload)
    {
        if (_disposed || _isOpen || _backend == null || payload is not IReadOnlyDictionary<string, object?> dict)
        {
            return;
        }
        var actionType = dict.TryGetValue("actionType", out var at) ? at as string : null;
        var dataJson = (dict.TryGetValue("data", out var dj) ? dj as string : null) ?? "{}";
        var flowConfig = _backend.FlowConfigJson;

        if (actionType == "banner")
        {
            _banner?.Close();
            _banner = new GleapOutboundSurface(this, isModal: false, dataJson, flowConfig, _endpoints, () => _banner = null, ShowMessenger);
        }
        else if (actionType == "modal")
        {
            _modal?.Close();
            _modal = new GleapOutboundSurface(this, isModal: true, dataJson, flowConfig, _endpoints, () => _modal = null, ShowMessenger);
        }
        else if (actionType == "notification")
        {
            var n = GleapNotification.FromActionJson(dataJson, Gleap.GetIdentity()?.Name);
            if (n == null)
            {
                return;
            }
            // A checklist configured to pop in the widget opens it directly instead of showing a card
            // (matches the native `popupType == "widget"` behavior).
            if (n.Kind == GleapNotificationKind.Checklist && n.ChecklistPopupType == "widget"
                && !string.IsNullOrEmpty(n.ChecklistId))
            {
                Gleap.OpenChecklist(n.ChecklistId!);
                ShowMessenger();
                return;
            }
            // Gleap.SetDisableInAppNotifications(true) suppresses the card — but not the checklist above,
            // which still opens in the widget (native behaviour).
            if (_backend.InAppNotificationsDisabled)
            {
                return;
            }
            _notifications?.Show(n);
        }
    }

    /// <summary>Tapping a preview card clears the stack and opens its target (conversation / news article /
    /// checklist), then reveals the messenger — mirroring the native SDKs' notification routing.</summary>
    private void OnNotificationClicked(GleapNotification n)
    {
        _notifications?.Clear();
        if (!string.IsNullOrEmpty(n.ConversationShareToken))
        {
            Gleap.OpenConversation(n.ConversationShareToken!);
        }
        else if (!string.IsNullOrEmpty(n.NewsId))
        {
            Gleap.OpenNewsArticle(n.NewsId!);
        }
        else if (!string.IsNullOrEmpty(n.ChecklistId))
        {
            Gleap.OpenChecklist(n.ChecklistId!);
        }
        ShowMessenger();
    }

    /// <summary>Recolours the launcher to the project's configured <c>buttonColor</c> so it matches the
    /// widget style set in Gleap, instead of the built-in default.</summary>
    private void ApplyLauncherStyle()
    {
        try
        {
            using var doc = JsonDocument.Parse(_backend!.FlowConfigJson);
            var root = doc.RootElement;

            if (root.TryGetProperty("buttonColor", out var bc) && bc.ValueKind == JsonValueKind.String
                && ColorConverter.ConvertFromString(bc.GetString()) is Color color)
            {
                _launcher.Background = new SolidColorBrush(color);
            }

            // Configured button logo replaces the generic chat glyph (matches the web / native SDKs).
            if (root.TryGetProperty("buttonLogo", out var bl) && bl.ValueKind == JsonValueKind.String
                && Uri.TryCreate(bl.GetString(), UriKind.Absolute, out var logoUri))
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = logoUri;
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                _chatIcon.Child = new Image { Source = bmp, Stretch = Stretch.Uniform };
            }

            // Configured position offset from the corner.
            double bx = ReadDouble(root, "buttonX"), by = ReadDouble(root, "buttonY");
            if (bx != 0 || by != 0)
            {
                _launcher.Margin = new Thickness(0, 0, 28 + bx, 24 + by);
                _overlay.Margin = new Thickness(0, 0, 28 + bx, 84 + by);
                _badge.Margin = new Thickness(0, 0, 22 + bx, 54 + by);
                _notifications?.SetOffset(bx, by);
            }

            ApplyButtonPosition(root.TryGetProperty("feedbackButtonPosition", out var pos)
                && pos.ValueKind == JsonValueKind.String ? pos.GetString() : null);
        }
        catch
        {
            // Keep the built-in launcher styling if the config is missing/unparsable.
        }

        // Match the preview cards' avatar/progress accent to the (possibly recoloured) launcher.
        _notifications?.SetAccent(_launcher.Background);
    }

    /// <summary>
    /// Applies the project's configured <c>feedbackButtonPosition</c>. <c>BUTTON_NONE</c> hides the
    /// launcher; the <c>*_LEFT</c> variants move it to the bottom-left; everything else stays bottom-right.
    /// <para>The web SDK's CLASSIC variants render a vertical text tab pinned to the page edge — a browser
    /// idiom with no desktop equivalent, so we honour only their side and keep the bubble.</para>
    /// </summary>
    private void ApplyButtonPosition(string? position)
    {
        if (position == "BUTTON_NONE")
        {
            SetLauncherVisible(false);
            return;
        }

        var left = position is "BOTTOM_LEFT" or "BUTTON_CLASSIC_LEFT";
        var side = left ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        _launcher.HorizontalAlignment = side;
        _overlay.HorizontalAlignment = side;
        _badge.HorizontalAlignment = side;
        if (left)
        {
            // Mirror the corner insets so the launcher hugs the left edge instead of the right.
            _launcher.Margin = new Thickness(_launcher.Margin.Right, 0, 0, _launcher.Margin.Bottom);
            _overlay.Margin = new Thickness(_overlay.Margin.Right, 0, 0, _overlay.Margin.Bottom);
            _badge.Margin = new Thickness(_badge.Margin.Right, 0, 0, _badge.Margin.Bottom);
        }
    }

    /// <summary>Shows/hides the launcher and its unread badge (<see cref="Gleap.ShowFeedbackButton"/> and
    /// the <c>BUTTON_NONE</c> config).</summary>
    private void SetLauncherVisible(bool visible)
    {
        _launcher.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (!visible)
        {
            _badge.Visibility = Visibility.Collapsed;
        }
    }

    private static double ReadDouble(JsonElement root, string name) =>
        root.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.Number && e.TryGetDouble(out var v) ? v : 0;

    private void UpdateBadge(object? count)
    {
        var n = count switch
        {
            int i => i,
            long l => (int)l,
            _ => int.TryParse(count?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var p) ? p : 0
        };

        if (n > 0 && !_isOpen)
        {
            _badgeText.Text = n > 99 ? "99+" : n.ToString(CultureInfo.InvariantCulture);
            _badge.Visibility = Visibility.Visible;
        }
        else
        {
            _badge.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>Run an action on the UI thread (backend events may arrive on a pool thread after polling).</summary>
    private void OnUi(Action action)
    {
        if (Dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            _ = Dispatcher.BeginInvoke(action);
        }
    }

    /// <summary>Stops polling and releases the hosted WebView2.</summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }
        if (disposing)
        {
            _pollTimer?.Stop();
            _pollTimer = null;
            _replayTimer?.Stop();
            _replayTimer = null;
            _pageTimer?.Stop();
            _pageTimer = null;
            _initRetryTimer?.Stop();
            _initRetryTimer = null;

            // Unhook the facade listeners so the disposed control isn't kept alive by the backend.
            if (_onWidgetOpened != null)
            {
                Gleap.RemoveListener("widgetOpened", _onWidgetOpened);
                _onWidgetOpened = null;
            }
            if (_onWidgetClosed != null)
            {
                Gleap.RemoveListener("widgetClosed", _onWidgetClosed);
                _onWidgetClosed = null;
            }
            if (_onNotificationCountUpdated != null)
            {
                Gleap.RemoveListener("notificationCountUpdated", _onNotificationCountUpdated);
                _onNotificationCountUpdated = null;
            }
            if (_onOutboundSent != null)
            {
                Gleap.RemoveListener("outboundSent", _onOutboundSent);
                _onOutboundSent = null;
            }
            if (_onFeedbackButtonVisibilityChanged != null)
            {
                Gleap.RemoveListener("feedbackButtonVisibilityChanged", _onFeedbackButtonVisibilityChanged);
                _onFeedbackButtonVisibilityChanged = null;
            }

            _notifications?.Clear();
            _banner?.Dispose();
            _modal?.Dispose();
            _webView.Dispose();
        }
        _disposed = true;
    }
}
