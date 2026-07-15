using System.Collections.Generic;
using GleapSDK.Collection;

namespace GleapSDK.Capture;

/// <summary>One captured replay frame: the image plus the context the replay viewer shows alongside it.</summary>
public sealed class ReplayFrame
{
    /// <summary>The captured image as a base64 data URI.</summary>
    public string Base64 { get; set; } = "";

    /// <summary>Screen the user was on when this frame was captured.</summary>
    public string ScreenName { get; set; } = "";

    /// <summary>Capture timestamp.</summary>
    public string Date { get; set; } = "";
}

/// <summary>
/// Bounded ring of replay frames the platform pushes on a timer. Frames carry their screen name and
/// timestamp, not just the image, because the report's <c>replay.frames</c> entries are objects
/// (<c>{screenname, url, date, interactions}</c>) — the shape the native SDKs send and the replay viewer
/// renders.
/// </summary>
public sealed class ReplayBuffer
{
    private readonly RingBuffer<ReplayFrame> _frames;

    public ReplayBuffer(int intervalMs, int capacity)
    {
        IntervalMs = intervalMs;
        _frames = new RingBuffer<ReplayFrame>(capacity);
    }

    public int IntervalMs { get; }

    public void AddFrame(string base64, string screenName = "", string date = "") =>
        _frames.Add(new ReplayFrame { Base64 = base64, ScreenName = screenName, Date = date });

    public IReadOnlyList<ReplayFrame> Snapshot() => _frames.Snapshot();
}
