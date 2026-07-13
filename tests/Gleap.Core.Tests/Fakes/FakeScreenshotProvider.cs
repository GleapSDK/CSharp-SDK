using GleapSDK.Capture;

namespace Gleap.Core.Tests.Fakes;

public sealed class FakeScreenshotProvider : IScreenshotProvider
{
    private readonly string? _screenshot;

    public FakeScreenshotProvider(string? screenshot) => _screenshot = screenshot;

    public Task<string?> CaptureScreenshotAsync(CancellationToken ct) => Task.FromResult(_screenshot);
}
