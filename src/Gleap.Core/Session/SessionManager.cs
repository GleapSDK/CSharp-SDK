using System.Threading;
using System.Threading.Tasks;
using GleapSDK.Http;
using GleapSDK.Models;

namespace GleapSDK.Session;

public sealed class SessionManager
{
    private const string KeyId = "gleapId";
    private const string KeyHash = "gleapHash";

    private readonly ApiClient _api;
    private readonly IKeyValueStore _store;

    public SessionManager(ApiClient api, IKeyValueStore store)
    {
        _api = api;
        _store = store;
    }

    public string GleapId { get; private set; } = "";
    public string GleapHash { get; private set; } = "";
    public bool HasSession => !string.IsNullOrEmpty(GleapId);
    public bool IsIdentified { get; private set; }
    public GleapUserProperty? Identity { get; private set; }

    public async Task StartAsync(string lang, string deviceType, CancellationToken ct)
    {
        var guestId = _store.Get(KeyId);
        var guestHash = _store.Get(KeyHash);

        var res = await _api.CreateSessionAsync(lang, deviceType, guestId, guestHash, ct)
            .ConfigureAwait(false);

        if (!string.IsNullOrEmpty(res.GleapId))
        {
            GleapId = res.GleapId;
            GleapHash = res.GleapHash;
            _store.Set(KeyId, GleapId);
            _store.Set(KeyHash, GleapHash);
        }
    }

    /// <summary>
    /// POST /sessions/identify, upgrading the anonymous session to an identified user. Skips the call when
    /// the same user is already identified with the same data — apps commonly call identifyContact on every
    /// screen, and the endpoint is rate-limited server-side. Both references gate this the same way
    /// (iOS sessionUpgradeWithDataNeeded, JS checkIfSessionNeedsUpdate).
    /// </summary>
    public async Task IdentifyAsync(string userId, GleapUserProperty data, string? userHash, CancellationToken ct)
    {
        data.UserId = userId;
        if (IsIdentified && !IdentityChanged(data))
        {
            return;
        }
        var res = await _api.IdentifyAsync(userId, data, userHash, GleapId, GleapHash, ct).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(res.GleapId))
        {
            GleapId = res.GleapId;
            GleapHash = res.GleapHash;
            _store.Set(KeyId, GleapId);
            _store.Set(KeyHash, GleapHash);
        }

        Identity = data;
        IsIdentified = true;
    }

    /// <summary>Whether <paramref name="data"/> differs from the identity we already sent. Compared
    /// field-by-field rather than by reference: callers typically build a fresh GleapUserProperty each
    /// time.</summary>
    private bool IdentityChanged(GleapUserProperty data)
    {
        var current = Identity;
        if (current == null)
        {
            return true;
        }
        return current.UserId != data.UserId
            || current.Name != data.Name
            || current.Email != data.Email
            || current.Phone != data.Phone
            || current.Plan != data.Plan
            || current.CompanyName != data.CompanyName
            || current.CompanyId != data.CompanyId
            || current.Avatar != data.Avatar
            || current.Lang != data.Lang
            || current.Value != data.Value
            || current.Sla != data.Sla
            // customData is an open bag; compare by content rather than assuming reference equality.
            || !SameCustomData(current.CustomData, data.CustomData);
    }

    private static bool SameCustomData(
        System.Collections.Generic.Dictionary<string, object>? a,
        System.Collections.Generic.Dictionary<string, object>? b)
    {
        if (a == null || b == null)
        {
            return a == null && b == null;
        }
        if (a.Count != b.Count)
        {
            return false;
        }
        foreach (var kv in a)
        {
            if (!b.TryGetValue(kv.Key, out var other) || !Equals(kv.Value, other))
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>POST /sessions/partialupdate for the current identified (or guest) session.</summary>
    public async Task UpdateContactAsync(GleapUserProperty data, CancellationToken ct)
    {
        await _api.UpdateContactAsync(data, GleapId, GleapHash, ct).ConfigureAwait(false);
        Identity = data;
    }

    public void ClearIdentity()
    {
        GleapId = "";
        GleapHash = "";
        _store.Remove(KeyId);
        _store.Remove(KeyHash);
        Identity = null;
        IsIdentified = false;
    }
}
