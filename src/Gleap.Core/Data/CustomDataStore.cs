using System.Collections.Generic;

namespace GleapSDK.Data;

/// <summary>Arbitrary key/value custom data attached to the session/report.</summary>
public sealed class CustomDataStore
{
    private readonly Dictionary<string, object> _data = new();

    public void Set(string key, object value) => _data[key] = value;

    public void Merge(IReadOnlyDictionary<string, object> values)
    {
        foreach (var kv in values)
        {
            _data[kv.Key] = kv.Value;
        }
    }

    public void Remove(string key) => _data.Remove(key);

    public void Clear() => _data.Clear();

    public IReadOnlyDictionary<string, object> Snapshot() => new Dictionary<string, object>(_data);
}
