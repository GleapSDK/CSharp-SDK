using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using GleapSDK.Session;

namespace GleapSDK.Omnis;

/// <summary>
/// <see cref="IKeyValueStore"/> persisting the Gleap session ids to
/// <c>%LOCALAPPDATA%\Gleap\omnis-session.json</c> so a returning user keeps their identity and conversation
/// history across app restarts. A missing or corrupt file is treated as empty (never throws).
/// </summary>
public sealed class OmnisFileKeyValueStore : IKeyValueStore
{
    private readonly string _path;
    private readonly Dictionary<string, string> _data;

    /// <param name="path">Override the store location; defaults to the per-user LocalAppData path.</param>
    public OmnisFileKeyValueStore(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Gleap", "omnis-session.json");
        _data = Load();
    }

    /// <inheritdoc />
    public string? Get(string key) => _data.TryGetValue(key, out var value) ? value : null;

    /// <inheritdoc />
    public void Set(string key, string value)
    {
        _data[key] = value;
        Save();
    }

    /// <inheritdoc />
    public void Remove(string key)
    {
        if (_data.Remove(key))
        {
            Save();
        }
    }

    private Dictionary<string, string> Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                var json = File.ReadAllText(_path);
                return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                    ?? new Dictionary<string, string>();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine("Gleap: could not read session store: " + ex.Message);
        }

        return new Dictionary<string, string>();
    }

    private void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir!);
            }

            File.WriteAllText(_path, JsonSerializer.Serialize(_data));
        }
        catch (Exception ex)
        {
            Debug.WriteLine("Gleap: could not write session store: " + ex.Message);
        }
    }
}
