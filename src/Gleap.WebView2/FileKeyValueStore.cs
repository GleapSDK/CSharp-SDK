using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using GleapSDK.Session;

namespace GleapSDK.WebView2;

/// <summary>
/// <see cref="IKeyValueStore"/> persisting session ids to a JSON file under
/// <c>%LOCALAPPDATA%\Gleap\session.json</c>. Tolerates a missing or corrupt file (treated as empty).
/// </summary>
public sealed class FileKeyValueStore : IKeyValueStore
{
    private readonly string _path;
    private readonly Dictionary<string, string> _data;

    public FileKeyValueStore(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Gleap", "session.json");
        _data = Load();
    }

    public string? Get(string key) => _data.TryGetValue(key, out var value) ? value : null;

    public void Set(string key, string value)
    {
        _data[key] = value;
        Save();
    }

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
                return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
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
                Directory.CreateDirectory(dir);
            }
            File.WriteAllText(_path, JsonSerializer.Serialize(_data));
        }
        catch (Exception ex)
        {
            Debug.WriteLine("Gleap: could not write session store: " + ex.Message);
        }
    }
}
