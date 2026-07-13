using System.Collections.Generic;
using System.Globalization;
using GleapSDK.Models;
using GleapSDK.Time;

namespace GleapSDK.Collection;

/// <summary>Bounded buffer of tracked events, timestamped via <see cref="IClock"/>.</summary>
public sealed class EventBuffer
{
    private readonly IClock _clock;
    private readonly RingBuffer<GleapEvent> _buffer;

    public EventBuffer(IClock clock, int capacity)
    {
        _clock = clock;
        _buffer = new RingBuffer<GleapEvent>(capacity);
    }

    public void Add(string name, object? data)
    {
        _buffer.Add(new GleapEvent
        {
            Name = name,
            Data = data,
            Date = _clock.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture)
        });
    }

    public IReadOnlyList<GleapEvent> Snapshot() => _buffer.Snapshot();

    public void Clear() => _buffer.Clear();
}
