using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GleapSDK.Models;
using GleapSDK.Outbound;
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
        if (!string.IsNullOrEmpty(gleapId))
        {
            h["Gleap-Id"] = gleapId!;
        }

        if (!string.IsNullOrEmpty(gleapHash))
        {
            h["Gleap-Hash"] = gleapHash!;
        }

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

        if (!res.IsSuccess)
        {
            throw new GleapApiException(res.StatusCode, $"Session request failed with status {res.StatusCode}");
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(res.Body);
        }
        catch (JsonException)
        {
            throw new GleapApiException(res.StatusCode, "Session response body was not valid JSON");
        }

        using (doc)
        {
            var root = doc.RootElement;
            return new SessionResult
            {
                GleapId = root.TryGetProperty("gleapId", out var i) ? i.GetString() ?? "" : "",
                GleapHash = root.TryGetProperty("gleapHash", out var hsh) ? hsh.GetString() ?? "" : ""
            };
        }
    }

    /// <summary>Returns the raw config JSON body for ConfigManager to split.</summary>
    public async Task<string> LoadConfigAsync(string lang, CancellationToken ct)
    {
        var url = _endpoints.ApiUrl + "/config/" + _sdkKey + "?lang=" + lang;
        var res = await _http.SendAsync("GET", url, null, BaseHeaders(null, null), ct)
            .ConfigureAwait(false);

        if (!res.IsSuccess)
        {
            throw new GleapApiException(res.StatusCode, $"Config request failed with status {res.StatusCode}");
        }

        return res.Body;
    }

    private static Dictionary<string, object?> ToDict(GleapUserProperty p) => new()
    {
        ["userId"] = p.UserId,
        ["name"] = p.Name,
        ["email"] = p.Email,
        ["phone"] = p.Phone,
        ["plan"] = p.Plan,
        ["companyName"] = p.CompanyName,
        ["companyId"] = p.CompanyId,
        ["avatar"] = p.Avatar,
        ["lang"] = p.Lang,
        ["value"] = p.Value,
        ["sla"] = p.Sla,
        ["customData"] = p.CustomData
    };

    /// <summary>POST /sessions/identify. Returns the (possibly upgraded) session ids.</summary>
    public async Task<SessionResult> IdentifyAsync(
        string userId, GleapUserProperty data, string? userHash,
        string? gleapId, string? gleapHash, CancellationToken ct)
    {
        var payload = ToDict(data);
        payload["userId"] = userId;
        if (!string.IsNullOrEmpty(userHash))
        {
            payload["userHash"] = userHash;
        }

        var res = await _http.SendAsync("POST", _endpoints.ApiUrl + "/sessions/identify",
            _json.Serialize(payload), BaseHeaders(gleapId, gleapHash), ct).ConfigureAwait(false);

        if (!res.IsSuccess)
        {
            throw new GleapApiException(res.StatusCode, $"Identify failed with status {res.StatusCode}");
        }

        using var doc = JsonDocument.Parse(res.Body);
        var root = doc.RootElement;
        return new SessionResult
        {
            GleapId = root.TryGetProperty("gleapId", out var i) ? i.GetString() ?? "" : "",
            GleapHash = root.TryGetProperty("gleapHash", out var h) ? h.GetString() ?? "" : ""
        };
    }

    /// <summary>POST /sessions/partialupdate.</summary>
    public async Task UpdateContactAsync(
        GleapUserProperty data, string? gleapId, string? gleapHash, CancellationToken ct)
    {
        var body = _json.Serialize(new Dictionary<string, object?>
        {
            ["data"] = ToDict(data),
            ["type"] = "windows",
            ["sdkVersion"] = "0.1.0"
        });
        var res = await _http.SendAsync("POST", _endpoints.ApiUrl + "/sessions/partialupdate",
            body, BaseHeaders(gleapId, gleapHash), ct).ConfigureAwait(false);
        if (!res.IsSuccess)
        {
            throw new GleapApiException(res.StatusCode, $"Update contact failed with status {res.StatusCode}");
        }
    }

    /// <summary>POST /bugs/v2 with the assembled report body. Returns the raw response JSON.</summary>
    public async Task<string> SubmitBugAsync(
        IReadOnlyDictionary<string, object?> body, string? gleapId, string? gleapHash, CancellationToken ct)
    {
        var res = await _http.SendAsync("POST", _endpoints.ApiUrl + "/bugs/v2",
            _json.Serialize(body), BaseHeaders(gleapId, gleapHash), ct).ConfigureAwait(false);
        if (!res.IsSuccess)
        {
            throw new GleapApiException(res.StatusCode, $"Bug submission failed with status {res.StatusCode}");
        }
        return res.Body;
    }

    /// <summary>POST /sessions/ping. Flushes buffered events and returns outbound actions + unread count.</summary>
    public async Task<PingResponse> PingAsync(
        long time, IReadOnlyList<object?> events, bool opened,
        string? gleapId, string? gleapHash, CancellationToken ct)
    {
        var body = _json.Serialize(new Dictionary<string, object?>
        {
            ["time"] = time,
            ["events"] = events,
            ["opened"] = opened,
            ["ws"] = false,
            ["type"] = "windows",
            ["sdkVersion"] = "0.1.0"
        });
        var res = await _http.SendAsync("POST", _endpoints.ApiUrl + "/sessions/ping",
            body, BaseHeaders(gleapId, gleapHash), ct).ConfigureAwait(false);
        if (!res.IsSuccess)
        {
            throw new GleapApiException(res.StatusCode, $"Ping failed with status {res.StatusCode}");
        }

        using var doc = JsonDocument.Parse(res.Body);
        var root = doc.RootElement;
        var actions = new List<OutboundAction>();
        if (root.TryGetProperty("a", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in arr.EnumerateArray())
            {
                var actionType = item.TryGetProperty("actionType", out var at) && at.ValueKind == JsonValueKind.String
                    ? at.GetString()! : "";
                var outboundId = item.TryGetProperty("outbound", out var ob) && ob.ValueKind == JsonValueKind.String
                    ? ob.GetString() : null;
                actions.Add(new OutboundAction(actionType, outboundId, item.Clone()));
            }
        }
        var unread = root.TryGetProperty("u", out var u) && u.ValueKind == JsonValueKind.Number ? u.GetInt32() : 0;
        return new PingResponse(actions, unread);
    }
}
