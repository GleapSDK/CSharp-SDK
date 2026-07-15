using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GleapSDK.Capture;

namespace GleapSDK.WebView2;

/// <summary>
/// <see cref="IScreenshotProvider"/> that renders a WPF element (the app window by default) to a
/// PNG data URI. Capture is marshalled to the UI thread (WPF rendering is UI-thread-only).
/// Elements marked with <see cref="GleapCensor"/> are masked out before the image is encoded, so
/// sensitive UI never leaves the device — in screenshots and, since replay frames go through this same
/// provider, in session replays too.
/// </summary>
public sealed class WindowsScreenshotProvider : IScreenshotProvider, IReplayFrameProvider
{
    /// <summary>Replay frames are downscaled and JPEG-compressed: a report carries up to 60 of them, and
    /// full-size lossless captures of a desktop screen add up to a huge upload. The bug-report screenshot
    /// deliberately stays lossless PNG — it is the artefact a developer zooms into to read a stack trace or
    /// a form value, where JPEG ringing around text costs more than the bytes save. (iOS compresses both,
    /// but its screens are a fraction of a desktop's and it optimizes for mobile bandwidth.)</summary>
    private const double ReplayScale = 0.5;
    private const int ReplayJpegQuality = 90;

    private static readonly Brush MaskBrush = CreateMaskBrush();

    private readonly Func<FrameworkElement?> _target;

    public WindowsScreenshotProvider(Func<FrameworkElement?>? target = null)
        => _target = target ?? (() => Application.Current?.MainWindow);

    private static SolidColorBrush CreateMaskBrush()
    {
        var brush = new SolidColorBrush(Color.FromRgb(0x2B, 0x2B, 0x2B));
        brush.Freeze();   // frozen so it is safe to reuse across captures
        return brush;
    }

    public Task<string?> CaptureScreenshotAsync(CancellationToken ct) =>
        OnUiThread(() => Capture(scale: 1.0, asJpeg: false));

    /// <inheritdoc />
    public Task<string?> CaptureReplayFrameAsync(CancellationToken ct) =>
        OnUiThread(() => Capture(ReplayScale, asJpeg: true));

    private static Task<string?> OnUiThread(Func<string?> capture)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null)
        {
            return Task.FromResult<string?>(null);
        }
        return dispatcher.CheckAccess()
            ? Task.FromResult(capture())
            : dispatcher.InvokeAsync(capture).Task;
    }

    private string? Capture(double scale, bool asJpeg)
    {
        var element = _target();
        if (element == null || element.ActualWidth < 1 || element.ActualHeight < 1)
        {
            return null;
        }

        var width = (int)element.ActualWidth;
        var height = (int)element.ActualHeight;
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);

        var censored = FindCensoredBounds(element);
        if (censored.Count > 0)
        {
            bitmap = ApplyMask(bitmap, censored, width, height);
        }

        // Downscale after masking, so a censored region can never be reconstructed from a sharper source.
        BitmapSource output = bitmap;
        if (scale < 1.0)
        {
            bitmap.Freeze();
            output = new TransformedBitmap(bitmap, new ScaleTransform(scale, scale));
        }

        BitmapEncoder encoder = asJpeg
            ? new JpegBitmapEncoder { QualityLevel = ReplayJpegQuality }
            : new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(output));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        var mime = asJpeg ? "image/jpeg" : "image/png";
        return $"data:{mime};base64," + Convert.ToBase64String(stream.ToArray());
    }

    /// <summary>Re-composes the capture with opaque rectangles over the censored regions. Masking after
    /// the render (rather than hiding the elements first) keeps the layout intact — nothing reflows, so
    /// the screenshot still shows the user's real screen.</summary>
    private static RenderTargetBitmap ApplyMask(
        RenderTargetBitmap source, List<Rect> censored, int width, int height)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawImage(source, new Rect(0, 0, width, height));
            foreach (var rect in censored)
            {
                dc.DrawRectangle(MaskBrush, null, rect);
            }
        }
        var masked = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        masked.Render(visual);
        return masked;
    }

    /// <summary>Bounds (in <paramref name="root"/>'s coordinates) of every visible element marked with
    /// <see cref="GleapCensor.IsCensoredProperty"/>.</summary>
    private static List<Rect> FindCensoredBounds(FrameworkElement root)
    {
        var bounds = new List<Rect>();
        if (GleapCensor.GetIsCensored(root))
        {
            bounds.Add(new Rect(0, 0, root.ActualWidth, root.ActualHeight));
            return bounds;   // the whole capture is censored
        }
        Collect(root, root, bounds);
        return bounds;
    }

    private static void Collect(DependencyObject node, FrameworkElement root, List<Rect> bounds)
    {
        var count = VisualTreeHelper.GetChildrenCount(node);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(node, i);
            if (child is FrameworkElement fe && GleapCensor.GetIsCensored(fe))
            {
                if (TryGetBounds(fe, root, out var rect))
                {
                    bounds.Add(rect);
                    continue;   // the whole subtree is covered by this rect
                }
            }
            Collect(child, root, bounds);
        }
    }

    private static bool TryGetBounds(FrameworkElement element, FrameworkElement root, out Rect bounds)
    {
        bounds = default;
        if (!element.IsVisible || element.ActualWidth < 1 || element.ActualHeight < 1)
        {
            return false;
        }
        try
        {
            var transform = element.TransformToAncestor(root);
            bounds = transform.TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
            return true;
        }
        catch (InvalidOperationException)
        {
            // Not in the same visual tree (e.g. detached mid-capture) — nothing to mask.
            return false;
        }
    }
}
