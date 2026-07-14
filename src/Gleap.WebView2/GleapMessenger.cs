using System;
using System.Globalization;
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
    private ManagedBackend? _backend;
    private bool _initializing;
    private bool _isOpen;
    private bool _disposed;
    private double? _contentHeight;

    /// <summary>Project SDK key. Set before the control loads (e.g. in the host's constructor).</summary>
    public string? SdkKey { get; set; }

    /// <summary>Width of the messenger overlay panel (default 380).</summary>
    public double PanelWidth { get; set; } = 380;

    /// <summary>Height of the messenger overlay panel (default 512).</summary>
    public double PanelHeight { get; set; } = 512;

    /// <summary>Launcher fill colour (default Gleap blue). Customize to match your brand.</summary>
    public Brush LauncherBackground { get; set; } = new SolidColorBrush(Color.FromRgb(0x48, 0x5B, 0xFF));

    /// <summary>Poll <c>/sessions/ping</c> on a timer so the unread badge stays live (default true).</summary>
    public bool EnableOutboundPolling { get; set; } = true;

    /// <summary>Outbound poll interval (default 5s).</summary>
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>The backend created on preload, or null until then.</summary>
    public ManagedBackend? Backend => _backend;

    /// <summary>True while the messenger overlay is shown.</summary>
    public bool IsMessengerVisible => _isOpen;

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
            try
            {
                await InitializeAsync().ConfigureAwait(true);
            }
            catch
            {
                // Offline / bad key — the launcher stays; opening will surface the failure again.
            }
        }
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
            // Composition rendering + Opacity 0 keeps the preload invisible (no flash).
            _backend = await GleapWebView2Host.AttachAsync(_webView, SdkKey!, endpoints).ConfigureAwait(true);
            ApplyLauncherStyle();

            Gleap.RegisterListener("widgetClosed", _ => OnUi(HideMessenger));
            Gleap.RegisterListener("notificationCountUpdated", count => OnUi(() => UpdateBadge(count)));
            Gleap.RegisterListener("widgetHeightChanged", h => OnUi(() => OnWidgetHeight(h)));

            if (EnableOutboundPolling)
            {
                StartPolling();
            }
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

    private void OnWidgetHeight(object? value)
    {
        var h = value switch
        {
            double d => d,
            int i => i,
            long l => l,
            _ => double.TryParse(value?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var p) ? p : 0
        };
        if (h > 0)
        {
            _contentHeight = h;
            ResizePanel();
        }
    }

    /// <summary>Sizes the panel to the widget's content height, clamped to <see cref="PanelHeight"/> and to
    /// the available vertical space, so it shrinks to fit when the window is short (web-parity behaviour).</summary>
    private void ResizePanel()
    {
        double available = ActualHeight > 0 ? ActualHeight - 84 /*launcher zone*/ - 24 /*top gap*/ : PanelHeight;
        double target = _contentHeight is { } c ? Math.Min(c, PanelHeight) : PanelHeight;
        double h = Math.Round(Math.Max(220, Math.Min(target, available)));
        if (h == _overlay.Height)
        {
            return;
        }
        _overlay.Height = h;
        _webView.Height = h;
        _webView.Clip = new RectangleGeometry(new Rect(0, 0, PanelWidth, h), CornerRadius, CornerRadius);
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

            // Configured position offset from the bottom-right corner.
            double bx = ReadDouble(root, "buttonX"), by = ReadDouble(root, "buttonY");
            if (bx != 0 || by != 0)
            {
                _launcher.Margin = new Thickness(0, 0, 28 + bx, 24 + by);
                _overlay.Margin = new Thickness(0, 0, 28 + bx, 84 + by);
                _badge.Margin = new Thickness(0, 0, 22 + bx, 54 + by);
            }
        }
        catch
        {
            // Keep the built-in launcher styling if the config is missing/unparsable.
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
            _webView.Dispose();
        }
        _disposed = true;
    }
}
