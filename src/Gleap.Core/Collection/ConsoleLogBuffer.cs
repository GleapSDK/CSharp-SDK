using System.Collections.Generic;
using System.Globalization;
using GleapSDK.Models;
using GleapSDK.Time;

namespace GleapSDK.Collection;

/// <summary>Bounded buffer of console/log lines, timestamped via <see cref="IClock"/>.</summary>
public sealed class ConsoleLogBuffer
{
    /// <summary>Per-entry cap, matching iOS. One stack trace or dumped payload should not be able to
    /// dominate the report body.</summary>
    private const int MaxEntryLength = 10_000;
    private const string TruncationSuffix = " [truncated]";

    private readonly IClock _clock;
    private readonly RingBuffer<GleapLog> _buffer;

    public ConsoleLogBuffer(IClock clock, int capacity)
    {
        _clock = clock;
        _buffer = new RingBuffer<GleapLog>(capacity);
    }

    public bool Enabled { get; set; } = true;

    public void Add(string message, LogLevel level)
    {
        if (!Enabled)
        {
            return;
        }

        _buffer.Add(new GleapLog
        {
            Date = _clock.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture),
            Log = Truncate(message),
            Priority = Priority(level)
        });
    }

    public IReadOnlyList<GleapLog> Snapshot() => _buffer.Snapshot();

    public void Clear() => _buffer.Clear();

    private static string Truncate(string message) =>
        message.Length > MaxEntryLength
            ? message.Substring(0, MaxEntryLength) + TruncationSuffix
            : message;

    private static string Priority(LogLevel level) => level switch
    {
        LogLevel.Error => "ERROR",
        LogLevel.Warning => "WARNING",
        _ => "INFO"
    };
}
