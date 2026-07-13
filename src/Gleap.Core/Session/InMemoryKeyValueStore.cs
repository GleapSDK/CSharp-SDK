using System.Collections.Generic;

namespace GleapSDK.Session;

public sealed class InMemoryKeyValueStore : IKeyValueStore
{
    private readonly Dictionary<string, string> _data = new();

    public string? Get(string key) => _data.TryGetValue(key, out var v) ? v : null;
    public void Set(string key, string value) => _data[key] = value;
    public void Remove(string key) => _data.Remove(key);
}
