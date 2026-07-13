using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GleapSDK.Serialization;

namespace GleapSDK.Http;

/// <summary>Typed access to the Gleap REST endpoints used during bootstrap.</summary>
public sealed class ApiClient
{
    private readonly IHttpTransport _http;
    private readonly IJsonSerializer _json;
    private readonly GleapEndpoints _endpoints;
    private readonly string _sdkKey;

    public ApiClient(IHttpTransport http, IJsonSerializer json, GleapEndpoints endpoints, string sdkKey)
    {
        _http = http;
        _json = json;
        _endpoints = endpoints;
        _sdkKey = sdkKey;
    }

    private Dictionary<string, string> BaseHeaders(string? gleapId, string? gleapHash)
    {
        var h = new Dictionary<string, string> { ["Api-Token"] = _sdkKey };
        if (!string.IsNullOrEmpty(gleapId)) h["Gleap-Id"] = gleapId!;
        if (!string.IsNullOrEmpty(gleapHash)) h["Gleap-Hash"] = gleapHash!;
        return h;
    }

    public async Task<SessionResult> CreateSessionAsync(
        string lang, string deviceType, string? guestId, string? guestHash, CancellationToken ct)
    {
        var body = _json.Serialize(new Dictionary<string, object>
        {
            ["lang"] = lang,
            ["platform"] = "windows",   // TODO SP-0 Part 2: platform per runtime (see spec §8)
            ["deviceType"] = deviceType
        });
        var res = await _http.SendAsync("POST", _endpoints.ApiUrl + "/sessions", body,
            BaseHeaders(guestId, guestHash), ct).ConfigureAwait(false);

        using var doc = JsonDocument.Parse(res.Body);
        var root = doc.RootElement;
        return new SessionResult
        {
            GleapId = root.TryGetProperty("gleapId", out var i) ? i.GetString() ?? "" : "",
            GleapHash = root.TryGetProperty("gleapHash", out var hsh) ? hsh.GetString() ?? "" : ""
        };
    }

    /// <summary>Returns the raw config JSON body for ConfigManager to split.</summary>
    public async Task<string> LoadConfigAsync(string lang, CancellationToken ct)
    {
        var url = _endpoints.ApiUrl + "/config/" + _sdkKey + "?lang=" + lang;
        var res = await _http.SendAsync("GET", url, null, BaseHeaders(null, null), ct)
            .ConfigureAwait(false);
        return res.Body;
    }
}
