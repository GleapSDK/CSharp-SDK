using System;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace GleapSDK.WebView2;

/// <summary>
/// A drop-in WPF control that reproduces the JavaScript-SDK experience on the desktop:
/// a floating launcher button that opens the Gleap messenger as an overlay, and hides it
/// again when the widget is closed. Host apps place this control on top of their own UI
/// (it fills its cell; the launcher and messenger panel anchor to the bottom-right).
///
/// <para>Because a native desktop app — unlike a web page — owns its own window, the SDK
/// cannot inject a launcher over arbitrary app UI the way the JS SDK does; this control is
/// how a WPF app opts into that behaviour with a single line.</para>
///
/// <para>Usage:
/// <code>
/// var messenger = new GleapMessenger { SdkKey = "YOUR_SDK_KEY" };
/// rootGrid.Children.Add(messenger);
/// await messenger.InitializeAsync();   // or set SdkKey in XAML and let it self-init on load
/// </code>
/// After initialization the static <see cref="Gleap"/> facade drives the same messenger
/// (<c>Gleap.StartBot("")</c>, <c>Gleap.OpenHelpCenter()</c>, …); call
/// <see cref="ShowMessenger"/> to reveal the overlay from code.</para>
/// </summary>
public class GleapMessenger : Grid, IDisposable
{
    private readonly Microsoft.Web.WebView2.Wpf.WebView2 _webView;
    private readonly Border _launcher;
    private readonly Border _badge;
    private readonly TextBlock _badgeText;
    private DispatcherTimer? _pollTimer;
    private ManagedBackend? _backend;
    private bool _initializing;
    private bool _disposed;

    /// <summary>Project SDK key. Set before <see cref="InitializeAsync"/> (or before the control loads for self-init).</summary>
    public string? SdkKey { get; set; }

    /// <summary>Width of the messenger overlay panel (default 400).</summary>
    public double PanelWidth { get; set; } = 400;

    /// <summary>Height of the messenger overlay panel (default 640).</summary>
    public double PanelHeight { get; set; } = 640;

    /// <summary>Launcher fill colour (default Gleap blue). Customize to match your brand.</summary>
    public Brush LauncherBackground { get; set; } = new SolidColorBrush(Color.FromRgb(0x48, 0x5B, 0xFF));

    /// <summary>Glyph shown on the launcher button (default a chat bubble).</summary>
    public string LauncherGlyph { get; set; } = "💬";

    /// <summary>Poll <c>/sessions/ping</c> on a timer so the unread badge stays live (default true).</summary>
    public bool EnableOutboundPolling { get; set; } = true;

    /// <summary>Outbound poll interval (default 5s).</summary>
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>The backend created on initialization, or null until <see cref="InitializeAsync"/> succeeds.</summary>
    public ManagedBackend? Backend => _backend;

    /// <summary>True while the messenger overlay is shown.</summary>
    public bool IsMessengerVisible => _webView.Visibility == Visibility.Visible;

    public GleapMessenger()
    {
        _webView = new Microsoft.Web.WebView2.Wpf.WebView2
        {
            Visibility = Visibility.Collapsed,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 24, 24),
            Width = PanelWidth,
            Height = PanelHeight
        };

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
            Margin = new Thickness(0, 0, 20, 62),
            Visibility = Visibility.Collapsed,
            Child = _badgeText,
            IsHitTestVisible = false
        };

        _launcher = new Border
        {
            Background = LauncherBackground,
            CornerRadius = new CornerRadius(28),
            Width = 56,
            Height = 56,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 24, 24),
            Cursor = Cursors.Hand,
            Effect = new DropShadowEffect { BlurRadius = 12, ShadowDepth = 2, Opacity = 0.35 },
            Child = new TextBlock
            {
                Text = LauncherGlyph,
                FontSize = 24,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        _launcher.MouseLeftButtonUp += (_, _) => ShowMessenger();

        Children.Add(_webView);
        Children.Add(_launcher);
        Children.Add(_badge);

        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Self-init only when a key was supplied declaratively; otherwise the host drives InitializeAsync().
        if (_backend == null && !_initializing && !string.IsNullOrEmpty(SdkKey))
        {
            await InitializeAsync().ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Creates the messenger session against <see cref="SdkKey"/> and wires it to this control.
    /// Idempotent. The WebView is shown during CoreWebView2 startup (a collapsed WebView2 will
    /// not create its HWND) and then hidden behind the launcher until opened.
    /// </summary>
    public async Task InitializeAsync(GleapSDK.Http.GleapEndpoints? endpoints = null)
    {
        if (_backend != null || _initializing)
        {
            return;
        }
        if (string.IsNullOrEmpty(SdkKey))
        {
            throw new InvalidOperationException("GleapMessenger.SdkKey must be set before InitializeAsync().");
        }

        _initializing = true;
        try
        {
            _webView.Visibility = Visibility.Visible;   // ensure the HWND exists for CoreWebView2 startup
            _backend = await GleapWebView2Host.AttachAsync(_webView, SdkKey!, endpoints).ConfigureAwait(true);

            Gleap.RegisterListener("widgetClosed", _ => OnUi(HideMessenger));
            Gleap.RegisterListener("notificationCountUpdated", count => OnUi(() => UpdateBadge(count)));

            _webView.Visibility = Visibility.Collapsed;  // hide until the launcher is clicked
            _launcher.Visibility = Visibility.Visible;

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

    /// <summary>Reveals the messenger overlay and tells the widget it is open.</summary>
    public void ShowMessenger()
    {
        if (_backend == null)
        {
            return;
        }
        _webView.Visibility = Visibility.Visible;
        _launcher.Visibility = Visibility.Collapsed;
        _badge.Visibility = Visibility.Collapsed;
        Gleap.Open();
    }

    /// <summary>Hides the messenger overlay and shows the launcher again.</summary>
    public void HideMessenger()
    {
        _webView.Visibility = Visibility.Collapsed;
        _launcher.Visibility = Visibility.Visible;
    }

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

        // Only show the badge while the messenger is closed (the launcher is what carries it).
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
