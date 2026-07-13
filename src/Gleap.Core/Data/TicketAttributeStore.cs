using System.Collections.Generic;

namespace GleapSDK.Data;

/// <summary>Ticket attributes prefilled on the next created ticket.</summary>
public sealed class TicketAttributeStore
{
    private readonly Dictionary<string, object> _data = new();

    public void Set(string key, object value) => _data[key] = value;

    public void Unset(string key) => _data.Remove(key);

    public void Clear() => _data.Clear();

    public IReadOnlyDictionary<string, object> Snapshot() => new Dictionary<string, object>(_data);
}
