using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GleapSDK.Http;

namespace GleapSDK.Session;

/// <summary>
/// Loads project config and splits it into flowConfig (widget config) and
/// projectActions, kept as raw JSON to forward verbatim in the "config-update" message.
/// </summary>
public sealed class ConfigManager
{
    private readonly ApiClient _api;

    public ConfigManager(ApiClient api) => _api = api;

    public bool IsLoaded { get; private set; }
    public string FlowConfigJson { get; private set; } = "{}";
    public string ProjectActionsJson { get; private set; } = "{}";

    public async Task LoadAsync(string lang, CancellationToken ct)
    {
        var raw = await _api.LoadConfigAsync(lang, ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(raw);
        var root = doc.RootElement;

        FlowConfigJson = root.TryGetProperty("flowConfig", out var f) ? f.GetRawText() : "{}";
        ProjectActionsJson = root.TryGetProperty("projectActions", out var a) ? a.GetRawText() : "{}";
        IsLoaded = true;
    }
}
