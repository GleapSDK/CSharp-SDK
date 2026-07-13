using System.Threading;
using System.Threading.Tasks;

namespace GleapSDK.Capture;

/// <summary>Platform hook that captures the current screen as a base64 PNG data URI
/// (e.g. "data:image/png;base64,....") or returns null if capture is unavailable.</summary>
public interface IScreenshotProvider
{
    Task<string?> CaptureScreenshotAsync(CancellationToken ct);
}
