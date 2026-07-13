using System.Threading;
using System.Threading.Tasks;
using GleapSDK.Http;

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

    public void ClearIdentity()
    {
        GleapId = "";
        GleapHash = "";
        _store.Remove(KeyId);
        _store.Remove(KeyHash);
    }
}
