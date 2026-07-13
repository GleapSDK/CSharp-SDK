using GleapSDK.Time;

namespace Gleap.Core.Tests.Fakes;

public sealed class FakeClock : IClock
{
    public DateTimeOffset UtcNow { get; set; } = new(2026, 7, 13, 10, 0, 0, TimeSpan.Zero);
}
