using GleapSDK.Session;
using UnityEngine;

namespace GleapSDK.Unity
{
    /// <summary>
    /// <see cref="IKeyValueStore"/> backed by Unity <see cref="PlayerPrefs"/> so the gleapId/gleapHash
    /// survive across sessions on every Unity platform (incl. WebGL/consoles).
    /// </summary>
    /// <remarks>
    /// Reads happen on the session-start synchronous prefix (main thread), so <see cref="Get"/> is a plain
    /// call. Writes happen in post-network continuations that Core runs off-thread
    /// (<c>ConfigureAwait(false)</c>), and <c>PlayerPrefs</c> is main-thread-only, so <see cref="Set"/> and
    /// <see cref="Remove"/> are marshalled through <see cref="GleapUnityMainThread"/>.
    /// </remarks>
    public sealed class UnityKeyValueStore : IKeyValueStore
    {
        private const string Prefix = "gleap_";

        public string Get(string key)
        {
            var k = Prefix + key;
            return PlayerPrefs.HasKey(k) ? PlayerPrefs.GetString(k) : null;
        }

        public void Set(string key, string value)
        {
            var k = Prefix + key;
            GleapUnityMainThread.Run(() =>
            {
                PlayerPrefs.SetString(k, value);
                PlayerPrefs.Save();
            });
        }

        public void Remove(string key)
        {
            var k = Prefix + key;
            GleapUnityMainThread.Run(() =>
            {
                PlayerPrefs.DeleteKey(k);
                PlayerPrefs.Save();
            });
        }
    }
}
