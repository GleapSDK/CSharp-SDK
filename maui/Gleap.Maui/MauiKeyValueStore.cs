using GleapSDK.Session;
using Microsoft.Maui.Storage;

namespace GleapSDK.Maui;

/// <summary>
/// <see cref="IKeyValueStore"/> backed by MAUI <see cref="Preferences"/> so the gleapId/gleapHash
/// persist across launches on every MAUI platform.
/// </summary>
public sealed class MauiKeyValueStore : IKeyValueStore
{
    private const string Prefix = "gleap_";

    public string? Get(string key)
    {
        var k = Prefix + key;
        return Preferences.Default.ContainsKey(k) ? Preferences.Default.Get(k, string.Empty) : null;
    }

    public void Set(string key, string value) => Preferences.Default.Set(Prefix + key, value);

    public void Remove(string key) => Preferences.Default.Remove(Prefix + key);
}
