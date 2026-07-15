using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using GleapSDK.Outbound;

namespace GleapSDK.WebView2;

/// <summary>
/// Renders in-app notification preview cards above the launcher, mirroring the native iOS/JS SDKs: at most
/// two cards, de-duplicated by outbound id, newest nearest the launcher, a single dismiss (X) that clears
/// all, and a click that routes to the conversation/news/checklist. Chat, news and checklist card variants
/// are supported. Pure WPF (no WebView) — the card content is native, matching the messenger styling.
/// </summary>
internal sealed class GleapNotificationStack
{
    private const int MaxCards = 2;
    private const double CardWidth = 320;

    private readonly Panel _host;
    private readonly Action<GleapNotification> _onClick;
    private readonly StackPanel _container;
    private readonly Border _dismissButton;
    private readonly List<CardEntry> _cards = new();
    private Brush _accent = new SolidColorBrush(Color.FromRgb(0x48, 0x5B, 0xFF));
    private double _offsetX;
    private double _offsetY;
    private bool _left;

    private sealed class CardEntry
    {
        public string? Outbound { get; set; }
        public FrameworkElement Card { get; set; } = null!;
    }

    public GleapNotificationStack(Panel host, Action<GleapNotification> onClick)
    {
        _host = host;
        _onClick = onClick;

        _dismissButton = BuildDismissButton();

        _container = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            // Sits above the launcher (launcher zone ~76px + gap); shifted with the launcher via SetOffset.
            Margin = new Thickness(0, 0, 24, 88),
            Background = null,
            IsHitTestVisible = true
        };
        _container.Children.Add(_dismissButton);
        _host.Children.Add(_container);
    }

    /// <summary>Brand accent (the project button colour) used for avatar fallbacks and the progress bar.</summary>
    public void SetAccent(Brush accent) => _accent = accent;

    /// <summary>Shifts the stack to track the configured launcher offset (buttonX/buttonY).</summary>
    public void SetOffset(double x, double y)
    {
        _offsetX = x;
        _offsetY = y;
        ApplyLayout();
    }

    /// <summary>Keeps the cards on the same side as the launcher (<c>feedbackButtonPosition</c>).</summary>
    public void SetAlignment(bool left)
    {
        _left = left;
        ApplyLayout();
    }

    private void ApplyLayout()
    {
        var side = _left ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        _container.HorizontalAlignment = side;
        _container.Margin = _left
            ? new Thickness(24 + _offsetX, 0, 0, 88 + _offsetY)
            : new Thickness(0, 0, 24 + _offsetX, 88 + _offsetY);
        _dismissButton.HorizontalAlignment = side;
        _dismissButton.Margin = _left ? new Thickness(4, 0, 0, 8) : new Thickness(0, 0, 4, 8);
    }

    /// <summary>Shows (or replaces, by outbound id) a notification card. Caps the stack at two, newest at
    /// the bottom (nearest the launcher).</summary>
    public void Show(GleapNotification notification)
    {
        // De-dup: replace an existing card for the same outbound.
        if (!string.IsNullOrEmpty(notification.Outbound))
        {
            var existing = _cards.Find(c => c.Outbound == notification.Outbound);
            if (existing != null)
            {
                _container.Children.Remove(existing.Card);
                _cards.Remove(existing);
            }
        }

        // Cap: drop the oldest card.
        while (_cards.Count >= MaxCards)
        {
            var oldest = _cards[0];
            _container.Children.Remove(oldest.Card);
            _cards.RemoveAt(0);
        }

        var card = BuildCard(notification);
        _cards.Add(new CardEntry { Outbound = notification.Outbound, Card = card });
        _container.Children.Add(card);   // appended -> below existing cards (nearest the launcher)
        _dismissButton.Visibility = Visibility.Visible;

        AnimateIn(card);

        if (notification.Sound)
        {
            try { System.Media.SystemSounds.Asterisk.Play(); } catch { /* audio is best-effort */ }
        }
    }

    /// <summary>Removes all cards (on widget open, dispose, or the dismiss button).</summary>
    public void Clear()
    {
        foreach (var c in _cards)
        {
            _container.Children.Remove(c.Card);
        }
        _cards.Clear();
        _dismissButton.Visibility = Visibility.Collapsed;
    }

    private static void AnimateIn(UIElement card)
    {
        card.Opacity = 0;
        card.RenderTransform = new TranslateTransform(0, 12);
        var dur = new Duration(TimeSpan.FromMilliseconds(220));
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        card.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, dur) { EasingFunction = ease });
        ((TranslateTransform)card.RenderTransform).BeginAnimation(
            TranslateTransform.YProperty, new DoubleAnimation(12, 0, dur) { EasingFunction = ease });
    }

    private Border BuildDismissButton()
    {
        var x = new Grid { Width = 12, Height = 12 };
        x.Children.Add(MakeBar(45));
        x.Children.Add(MakeBar(-45));
        var btn = new Border
        {
            Width = 24,
            Height = 24,
            CornerRadius = new CornerRadius(12),
            Background = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3A)),
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 0, 4, 8),
            Cursor = System.Windows.Input.Cursors.Hand,
            Visibility = Visibility.Collapsed,
            Effect = new DropShadowEffect { BlurRadius = 8, ShadowDepth = 0, Opacity = 0.25 },
            Child = x
        };
        btn.MouseLeftButtonUp += (_, _) => Clear();
        return btn;
    }

    private static Rectangle MakeBar(double angle) => new()
    {
        Width = 12,
        Height = 1.8,
        RadiusX = 0.9,
        RadiusY = 0.9,
        Fill = Brushes.White,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        RenderTransformOrigin = new Point(0.5, 0.5),
        RenderTransform = new RotateTransform(angle)
    };

    private Border BuildCard(GleapNotification n)
    {
        Panel content = n.Kind switch
        {
            GleapNotificationKind.News => BuildNewsContent(n),
            GleapNotificationKind.Checklist => BuildChecklistContent(n),
            _ => BuildMessageContent(n)
        };

        var card = new Border
        {
            Width = CardWidth,
            MaxWidth = CardWidth,
            CornerRadius = new CornerRadius(14),
            Background = Brushes.White,
            Margin = new Thickness(0, 8, 0, 0),
            Cursor = System.Windows.Input.Cursors.Hand,
            Effect = new DropShadowEffect { BlurRadius = 20, ShadowDepth = 1, Opacity = 0.18 },
            Child = content
        };
        card.MouseLeftButtonUp += (_, _) => _onClick(n);
        return card;
    }

    private Grid BuildMessageContent(GleapNotification n)
    {
        var grid = new Grid { Margin = new Thickness(14) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var avatar = BuildAvatar(n.Sender, 36);
        Grid.SetColumn(avatar, 0);
        grid.Children.Add(avatar);

        var stack = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        if (!string.IsNullOrEmpty(n.Sender?.Name))
        {
            stack.Children.Add(new TextBlock
            {
                Text = n.Sender!.Name,
                FontWeight = FontWeights.SemiBold,
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A)),
                Margin = new Thickness(0, 0, 0, 2)
            });
        }
        stack.Children.Add(new TextBlock
        {
            Text = n.Text ?? "",
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)),
            TextWrapping = TextWrapping.Wrap,
            MaxHeight = 40,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        Grid.SetColumn(stack, 1);
        grid.Children.Add(stack);
        return grid;
    }

    private StackPanel BuildNewsContent(GleapNotification n)
    {
        var outer = new StackPanel();

        var cover = TryLoadImage(n.CoverImageUrl);
        if (cover != null)
        {
            outer.Children.Add(new Border
            {
                Height = 120,
                CornerRadius = new CornerRadius(14, 14, 0, 0),
                Background = new ImageBrush(cover) { Stretch = Stretch.UniformToFill },
                ClipToBounds = true
            });
        }

        var body = BuildMessageContent(n);   // sender/name + text, reused
        outer.Children.Add(body);
        return outer;
    }

    private Grid BuildChecklistContent(GleapNotification n)
    {
        var grid = new Grid { Margin = new Thickness(14) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var title = new TextBlock
        {
            Text = n.ChecklistNextStepTitle ?? n.Text ?? "Checklist",
            FontWeight = FontWeights.SemiBold,
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A)),
            TextWrapping = TextWrapping.Wrap,
            MaxHeight = 40,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetRow(title, 0);
        grid.Children.Add(title);

        double fraction = n.ChecklistTotalSteps > 0
            ? Math.Max(0, Math.Min(1, (double)n.ChecklistCurrentStep / n.ChecklistTotalSteps))
            : 0;
        var track = new Border
        {
            Height = 6,
            CornerRadius = new CornerRadius(3),
            Background = new SolidColorBrush(Color.FromRgb(0xEA, 0xEA, 0xEA)),
            Margin = new Thickness(0, 10, 0, 6)
        };
        // Fill the track proportionally with a two-column star grid (no fixed pixel width needed).
        var progressGrid = new Grid();
        progressGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(fraction, GridUnitType.Star) });
        progressGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1 - fraction, GridUnitType.Star) });
        var fill = new Border { Height = 6, CornerRadius = new CornerRadius(3), Background = _accent };
        Grid.SetColumn(fill, 0);
        progressGrid.Children.Add(fill);
        track.Child = progressGrid;
        Grid.SetRow(track, 1);
        grid.Children.Add(track);

        var steps = new TextBlock
        {
            Text = string.Format(CultureInfo.InvariantCulture, "Step {0} of {1}",
                n.ChecklistCurrentStep, n.ChecklistTotalSteps),
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(0x8A, 0x8A, 0x8A))
        };
        Grid.SetRow(steps, 2);
        grid.Children.Add(steps);
        return grid;
    }

    private Grid BuildAvatar(GleapNotificationSender? sender, double size)
    {
        var grid = new Grid { Width = size, Height = size, VerticalAlignment = VerticalAlignment.Top };

        // Fallback: accent circle with initials.
        grid.Children.Add(new Ellipse { Fill = _accent });
        grid.Children.Add(new TextBlock
        {
            Text = Initials(sender?.Name),
            Foreground = Brushes.White,
            FontSize = size * 0.36,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        });

        var img = TryLoadImage(sender?.ProfileImageUrl);
        if (img != null)
        {
            grid.Children.Add(new Ellipse
            {
                Fill = new ImageBrush(img) { Stretch = Stretch.UniformToFill }
            });
        }
        return grid;
    }

    private static string Initials(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "";
        }
        var parts = name!.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return "";
        }
        var first = parts[0].Substring(0, 1).ToUpperInvariant();
        return parts.Length > 1 ? first + parts[^1].Substring(0, 1).ToUpperInvariant() : first;
    }

    private static BitmapImage? TryLoadImage(string? url)
    {
        if (string.IsNullOrEmpty(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return null;
        }
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = uri;                       // WPF downloads remote images asynchronously
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            return bmp;
        }
        catch (Exception)
        {
            // Bad URL / decode failure — the avatar/cover falls back to initials/none.
            return null;
        }
    }
}
