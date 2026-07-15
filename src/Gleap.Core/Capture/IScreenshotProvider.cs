using System.Threading;
using System.Threading.Tasks;

namespace GleapSDK.Capture;

/// <summary>Platform hook that captures the current screen as a base64 PNG data URI
/// (e.g. "data:image/png;base64,....") or returns null if capture is unavailable.</summary>
public interface IScreenshotProvider
{
    Task<string?> CaptureScreenshotAsync(CancellationToken ct);
}

/// <summary>
/// Optional companion to <see cref="IScreenshotProvider"/> for hosts that can produce a cheaper frame for
/// session replay. A report ships up to 60 replay frames, so full-size lossless captures add up fast;
/// providers implementing this return a downscaled/compressed frame instead. Backends fall back to
/// <see cref="IScreenshotProvider.CaptureScreenshotAsync"/> when a provider does not implement it.
/// </summary>
/// <remarks>Deliberately a separate interface rather than a second method on
/// <see cref="IScreenshotProvider"/>: this library targets netstandard2.0, which has no default interface
/// members, so adding a method would break every existing implementer.</remarks>
public interface IReplayFrameProvider
{
    Task<string?> CaptureReplayFrameAsync(CancellationToken ct);
}
