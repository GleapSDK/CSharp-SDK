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

    public SessionDataCollector(
        ConsoleLogBuffer consoleLog,
        EventBuffer eventLog,
        NetworkLogBuffer networkLog,
        CustomDataStore customData,
        TicketAttributeStore ticketAttributes,
        TagStore tags,
        IMetadataProvider metadata)
    {
        _consoleLog = consoleLog;
        _eventLog = eventLog;
        _networkLog = networkLog;
        _customData = customData;
        _ticketAttributes = ticketAttributes;
        _tags = tags;
        _metadata = metadata;
    }

    public IReadOnlyDictionary<string, object?> BuildTicketData() => new Dictionary<string, object?>
    {
        ["customData"] = _customData.Snapshot(),
        ["formData"] = _ticketAttributes.Snapshot(),
        ["metaData"] = _metadata.Collect(),
        ["consoleLog"] = _consoleLog.Snapshot(),
        ["networkLogs"] = _networkLog.Snapshot(),
        ["customEventLog"] = _eventLog.Snapshot(),
        ["tags"] = _tags.Snapshot()
    };
}
