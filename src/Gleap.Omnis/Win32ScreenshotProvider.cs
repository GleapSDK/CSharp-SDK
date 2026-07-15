using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using GleapSDK.Capture;

namespace GleapSDK.Omnis;

/// <summary>
/// Captures the native Omnis application window as a base64 PNG data URI, so bug reports carry a real
/// screenshot of the app (the hosted web widget can only ever see its own DOM, never the Omnis UI behind
/// it). The Omnis glue sets <see cref="TargetWindow"/> to its top-level window handle; capture then uses
/// <c>PrintWindow</c> with <c>PW_RENDERFULLCONTENT</c> so composed/GPU-accelerated content is included.
/// When no handle is set, it falls back to the primary screen. Returns <c>null</c> if capture fails, which
/// Gleap.Core treats as "no screenshot" rather than an error.
/// </summary>
public sealed class Win32ScreenshotProvider : IScreenshotProvider
{
    private const int PwRenderFullContent = 0x00000002;

    /// <summary>
    /// The Omnis top-level window handle to capture. Set by the glue via the COM facade
    /// (<c>SetWindowHandle</c>). <see cref="IntPtr.Zero"/> falls back to a full primary-screen capture.
    /// </summary>
    public IntPtr TargetWindow { get; set; }

    /// <inheritdoc />
    public Task<string?> CaptureScreenshotAsync(CancellationToken ct)
    {
        try
        {
            using var bitmap = Capture();
            if (bitmap == null)
            {
                return Task.FromResult<string?>(null);
            }

            using var stream = new MemoryStream();
            bitmap.Save(stream, ImageFormat.Png);
            var dataUri = "data:image/png;base64," + Convert.ToBase64String(stream.ToArray());
            return Task.FromResult<string?>(dataUri);
        }
        catch (Exception)
        {
            // A failed capture must never break report submission — the report just goes without a shot.
            return Task.FromResult<string?>(null);
        }
    }

    private Bitmap? Capture()
    {
        var hwnd = TargetWindow;
        return hwnd != IntPtr.Zero && IsWindow(hwnd) ? CaptureWindow(hwnd) : CapturePrimaryScreen();
    }

    private static Bitmap? CaptureWindow(IntPtr hwnd)
    {
        if (!GetWindowRect(hwnd, out var rect))
        {
            return null;
        }

        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        if (width <= 0 || height <= 0)
        {
            return null;
        }

        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        try
        {
            using var graphics = Graphics.FromImage(bitmap);
            var hdc = graphics.GetHdc();
            try
            {
                if (!PrintWindow(hwnd, hdc, PwRenderFullContent))
                {
                    // Older windows may not honour PW_RENDERFULLCONTENT; retry a plain print.
                    PrintWindow(hwnd, hdc, 0);
                }
            }
            finally
            {
                graphics.ReleaseHdc(hdc);
            }

            return bitmap;
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
    }

    private static Bitmap? CapturePrimaryScreen()
    {
        var width = GetSystemMetrics(SmCxScreen);
        var height = GetSystemMetrics(SmCyScreen);
        if (width <= 0 || height <= 0)
        {
            return null;
        }

        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        try
        {
            using var graphics = Graphics.FromImage(bitmap);
            graphics.CopyFromScreen(0, 0, 0, 0, new Size(width, height), CopyPixelOperation.SourceCopy);
            return bitmap;
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
    }

    private const int SmCxScreen = 0;
    private const int SmCyScreen = 1;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, int nFlags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hwnd, out Rect lpRect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
