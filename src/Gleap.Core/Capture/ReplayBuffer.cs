using System.Collections.Generic;
using GleapSDK.Collection;

namespace GleapSDK.Capture;

/// <summary>Bounded ring of replay frames (base64 PNGs) the platform pushes on a timer.</summary>
public sealed class ReplayBuffer
{
    private readonly RingBuffer<string> _frames;

    public ReplayBuffer(int intervalMs, int capacity)
    {
        IntervalMs = intervalMs;
        _frames = new RingBuffer<string>(capacity);
    }

    public int IntervalMs { get; }

    public void AddFrame(string base64) => _frames.Add(base64);

    public IReadOnlyList<string> Snapshot() => _frames.Snapshot();

    public IReadOnlyDictionary<string, object?> BuildReplay() => new Dictionary<string, object?>
    {
        ["interval"] = IntervalMs,
        ["frames"] = _frames.Snapshot()
    };
}
