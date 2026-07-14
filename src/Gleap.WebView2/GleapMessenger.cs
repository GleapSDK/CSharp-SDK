using System;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace GleapSDK.WebView2;

/// <summary>
/// A drop-in WPF control that reproduces the JavaScript-SDK experience on the desktop:
/// a round floating launcher button that slides the Gleap messenger in as a bottom-right
/// overlay, and slides it back out when the widget is closed. Host apps place this control
/// on top of their own UI and set <see cref="SdkKey"/> — nothing else is required.
///
/// <para>A native desktop app owns its own window, so — unlike the web JS SDK — the SDK
/// cannot inject a launcher over arbitrary app UI; this control is how a WPF app opts into
/// that experience. The messenger session is created lazily on the first open, so startup
/// shows only the launcher (no flashing WebView).</para>
///
/// <code>
/// var messenger = new GleapMessenger { SdkKey = "YOUR_SDK_KEY" };
/// rootGrid.Children.Add(messenger);
/// </code>
/// After the first open the static <see cref="Gleap"/> facade drives the same messenger
/// (<c>Gleap.StartBot("")</c>, <c>Gleap.OpenHelpCenter()</c>, …).
/// </summary>
public class GleapMessenger : Grid, IDisposable
{
    private static readonly Duration SlideDuration = new(TimeSpan.FromMilliseconds(300));

    private readonly Microsoft.Web.WebView2.Wpf.WebView2 _webView;
    private readonly Border _panelHost;
    private readonly TranslateTransform _panelSlide;
    private readonly Border _launcher;
    private readonly ScaleTransform _launcherScale;
    private readonly Border _badge;
    private readonly TextBlock _badgeText;
    private DispatcherTimer? _pollTimer;
    private ManagedBackend? _backend;
    private bool _initializing;
    private bool _disposed;

    /// <summary>Project SDK key. Set before the first open (e.g. in the host's constructor).</summary>
    public string? SdkKey { get; set; }

    /// <summary>Width of the messenger overlay panel (default 400).</summary>
    public double PanelWidth { get; set; } = 400;

    /// <summary>Height of the messenger overlay panel (default 640).</summary>
    public double PanelHeight { get; set; } = 640;

    /// <summary>Launcher fill colour (default Gleap blue). Customize to match your brand.</summary>
    public Brush LauncherBackground { get; set; } = new SolidColorBrush(Color.FromRgb(0x48, 0x5B, 0xFF));

    /// <summary>Poll <c>/sessions/ping</c> on a timer so the unread badge stays live (default true).</summary>
    public bool EnableOutboundPolling { get; set; } = true;

    /// <summary>Outbound poll interval (default 5s).</summary>
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>The backend created on first open, or null until then.</summary>
    public ManagedBackend? Backend => _backend;

    /// <summary>True while the messenger overlay is shown.</summary>
    public bool IsMessengerVisible => _panelHost.Visibility == Visibility.Visible;

    private double HiddenOffset => PanelHeight + 40;

    public GleapMessenger()
    {
        _webView = new Microsoft.Web.WebView2.Wpf.WebView2();

        _panelSlide = new TranslateTransform(0, HiddenOffset);
        _panelHost = new Border
        {
            Width = PanelWidth,
            Height = PanelHeight,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 24, 24),
            Visibility = Visibility.Collapsed,
            RenderTransform = _panelSlide,
            Background = Brushes.Transparent,
            Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 4, Opacity = 0.22 },
            Child = _webView
        };

        _launcherScale = new ScaleTransform(1, 1);
        _launcher = new Border
        {
            Background = LauncherBackground,
            CornerRadius = new CornerRadius(30),
            Width = 60,
            Height = 60,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 24, 24),
            Cursor = Cursors.Hand,
            Effect = new DropShadowEffect { BlurRadius = 16, ShadowDepth = 2, Opacity = 0.3 },
            RenderTransform = _launcherScale,
            RenderTransformOrigin = new Point(0.5, 0.5),
            Child = new Viewbox
            {
                Width = 26,
                Height = 26,
                Child = new Path
                {
                    Fill = Brushes.White,
                    // A simple rounded speech bubble.
                    Data = Geometry.Parse("M6,3 H22 A4,4 0 0 1 26,7 V17 A4,4 0 0 1 22,21 H14 L9,26 V21 H6 A4,4 0 0 1 2,17 V7 A4,4 0 0 1 6,3 Z")
                }
            }
        };
        _launcher.MouseLeftButtonUp += (_, _) => ShowMessenger();
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
            Margin = new Thickness(0, 0, 20, 66),
            Visibility = Visibility.Collapsed,
            Child = _badgeText,
            IsHitTestVisible = false
        };

        Children.Add(_panelHost);
        Children.Add(_launcher);
        Children.Add(_badge);
    }

    /// <summary>Opens the messenger overlay (initializing the session on first use).</summary>
    public void ShowMessenger() => _ = OpenAsync();

    private async Task OpenAsync()
    {
        if (_disposed || string.IsNullOrEmpty(SdkKey))
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
            return; // init failure (offline, bad key) — leave the launcher in place
        }
        if (_backend == null)
        {
            return;
        }

        _badge.Visibility = Visibility.Collapsed;
        _panelHost.Visibility = Visibility.Visible;
        AnimatePanel(open: true);
        FadeLauncher(visible: false);
        Gleap.Open();
    }

    /// <summary>Hides the messenger overlay and returns to the launcher.</summary>
    public void HideMessenger()
    {
        if (_panelHost.Visibility == Visibility.Visible)
        {
            AnimatePanel(open: false);
        }
        FadeLauncher(visible: true);
    }

    /// <summary>
    /// Creates the messenger session against <see cref="SdkKey"/> and wires it to this control.
    /// Idempotent. Called automatically on the first open; call it yourself only to preload.
    /// The WebView is shown off-screen during CoreWebView2 startup (a collapsed WebView2 will
    /// not create its HWND) and left hidden afterwards.
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
            _panelSlide.Y = HiddenOffset;                 // keep it off-screen during startup
            _panelHost.Visibility = Visibility.Visible;   // required for the CoreWebView2 HWND
            _backend = await GleapWebView2Host.AttachAsync(_webView, SdkKey!, endpoints).ConfigureAwait(true);

            Gleap.RegisterListener("widgetClosed", _ => OnUi(HideMessenger));
            Gleap.RegisterListener("notificationCountUpdated", count => OnUi(() => UpdateBadge(count)));

            _panelHost.Visibility = Visibility.Collapsed; // resting (hidden) state
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
        var slide = MakeDouble(open ? HiddenOffset : 0, open ? 0 : HiddenOffset, open ? EasingMode.EaseOut : EasingMode.EaseIn);
        var fade = MakeDouble(open ? 0 : 1, open ? 1 : 0, EasingMode.EaseOut);
        if (!open)
        {
            fade.Completed += (_, _) => _panelHost.Visibility = Visibility.Collapsed;
        }
        _panelHost.BeginAnimation(OpacityProperty, fade);
        _panelSlide.BeginAnimation(TranslateTransform.YProperty, slide);
    }

    private void FadeLauncher(bool visible)
    {
        if (visible)
        {
            _launcher.Visibility = Visibility.Visible;
        }
        var fade = MakeDouble(visible ? 0 : 1, visible ? 1 : 0, EasingMode.EaseOut);
        var scale = MakeDouble(visible ? 0.6 : 1, visible ? 1 : 0.6, EasingMode.EaseOut);
        if (!visible)
        {
            fade.Completed += (_, _) => _launcher.Visibility = Visibility.Collapsed;
        }
        _launcher.BeginAnimation(OpacityProperty, fade);
        _launcherScale.BeginAnimation(ScaleTransform.ScaleXProperty, scale);
        _launcherScale.BeginAnimation(ScaleTransform.ScaleYProperty, scale);
    }

    private void AnimateLauncherScale(double target)
    {
        if (_launcher.Visibility != Visibility.Visible)
        {
            return;
        }
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

    private void UpdateBadge(object? count)
    {
        var n = count switch
        {
            int i => i,
            long l => (int)l,
            _ => int.TryParse(count?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var p) ? p : 0
        };

        if (n > 0 && _launcher.Visibility == Visibility.Visible)
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
