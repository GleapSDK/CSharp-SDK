using System;

namespace GleapSDK.Time;

/// <summary>Real wall-clock time.</summary>
public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
