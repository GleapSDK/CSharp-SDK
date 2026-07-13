using System;

namespace GleapSDK.Time;

/// <summary>Time source, injected so buffers/timestamps are deterministic in tests.</summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
