namespace GleapSDK.Session;

/// <summary>Persistence for session ids. Platform packages back this with
/// NSUserDefaults / SharedPreferences / registry / file; Core uses in-memory in tests.</summary>
public interface IKeyValueStore
{
    string? Get(string key);
    void Set(string key, string value);
    void Remove(string key);
}
