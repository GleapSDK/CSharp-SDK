using System;
using System.Collections.Generic;
using GleapSDK.Data;
using GleapSDK.Metadata;

namespace GleapSDK.Collection;

/// <summary>
/// Aggregates every collected source into the payload the web widget requests via the
/// <c>collect-ticket-data</c> bridge message (spec §5):
/// { customData, formData, metaData, consoleLog, networkLogs, customEventLog, tags }.
/// </summary>
public sealed class SessionDataCollector
{
    private readonly ConsoleLogBuffer _consoleLog;
    private readonly EventBuffer _eventLog;
    private readonly NetworkLogBuffer _networkLog;
    private readonly CustomDataStore _customData;
    private readonly TicketAttributeStore _ticketAttributes;
    private readonly TagStore _tags;
    private readonly IMetadataProvider _metadata;
    private readonly Func<string?>? _lastScreenName;

    public SessionDataCollector(
        ConsoleLogBuffer consoleLog,
        EventBuffer eventLog,
        NetworkLogBuffer networkLog,
        CustomDataStore customData,
        TicketAttributeStore ticketAttributes,
        TagStore tags,
        IMetadataProvider metadata,
        Func<string?>? lastScreenName = null)
    {
        _consoleLog = consoleLog;
        _eventLog = eventLog;
        _networkLog = networkLog;
        _customData = customData;
        _ticketAttributes = ticketAttributes;
        _tags = tags;
        _metadata = metadata;
        _lastScreenName = lastScreenName;
    }

    public IReadOnlyDictionary<string, object?> BuildTicketData() => new Dictionary<string, object?>
    {
        ["customData"] = _customData.Snapshot(),
        ["formData"] = _ticketAttributes.Snapshot(),
        ["metaData"] = BuildMetaData(),
        ["consoleLog"] = _consoleLog.Snapshot(),
        ["networkLogs"] = _networkLog.Snapshot(),
        ["customEventLog"] = _eventLog.Snapshot(),
        ["tags"] = _tags.Snapshot()
    };

    /// <summary>The provider's metadata plus <c>lastScreenName</c> — the last screen tracked before the
    /// widget opened. The native SDKs report this alongside the device fields; it lives here rather than in
    /// a platform provider so every host gets it from <see cref="ManagedBackend.TrackPage"/> for free.
    /// Always present (empty when no screen has been tracked), matching iOS.</summary>
    private Dictionary<string, object?> BuildMetaData()
    {
        var meta = new Dictionary<string, object?>();
        foreach (var kv in _metadata.Collect())
        {
            meta[kv.Key] = kv.Value;
        }
        meta["lastScreenName"] = _lastScreenName?.Invoke() ?? "";
        return meta;
    }
}
