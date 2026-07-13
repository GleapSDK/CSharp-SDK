using GleapSDK.Session;
using UnityEngine;

namespace GleapSDK.Unity
{
    /// <summary>
    /// <see cref="IKeyValueStore"/> backed by Unity <see cref="PlayerPrefs"/> so the gleapId/gleapHash
    /// survive across sessions on every Unity platform (incl. WebGL/consoles).
    /// </summary>
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
            PlayerPrefs.SetString(Prefix + key, value);
            PlayerPrefs.Save();
        }

        public void Remove(string key)
        {
            PlayerPrefs.DeleteKey(Prefix + key);
            PlayerPrefs.Save();
        }
    }
}
