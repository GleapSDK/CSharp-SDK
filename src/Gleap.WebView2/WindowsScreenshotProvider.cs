using System;
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
/// </summary>
public sealed class WindowsScreenshotProvider : IScreenshotProvider
{
    private readonly Func<FrameworkElement?> _target;

    public WindowsScreenshotProvider(Func<FrameworkElement?>? target = null)
        => _target = target ?? (() => Application.Current?.MainWindow);

    public Task<string?> CaptureScreenshotAsync(CancellationToken ct)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null)
        {
            return Task.FromResult<string?>(null);
        }
        return dispatcher.CheckAccess()
            ? Task.FromResult(Capture())
            : dispatcher.InvokeAsync(Capture).Task;
    }

    private string? Capture()
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

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return "data:image/png;base64," + Convert.ToBase64String(stream.ToArray());
    }
}
