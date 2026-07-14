using System;
using System.Collections.Concurrent;
using System.Threading;
using UnityEngine;

namespace GleapSDK.Unity
{
    /// <summary>
    /// Marshals actions onto the Unity main thread. Gleap.Core awaits with
    /// <c>ConfigureAwait(false)</c>, so continuations after a network round-trip run on the ThreadPool —
    /// but Unity APIs (<c>PlayerPrefs</c>, <c>SystemInfo</c>, <c>Screen</c>, …) must only be touched on the
    /// main thread. Work that has to hit those APIs off-thread is queued here and pumped in
    /// <see cref="Update"/>. Created once, on the main thread, from <see cref="GleapUnity.AttachAsync"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GleapUnityMainThread : MonoBehaviour
    {
        private static readonly ConcurrentQueue<Action> Queue = new ConcurrentQueue<Action>();
        private static GleapUnityMainThread _instance;
        private static int _mainThreadId;

        /// <summary>Creates the pump (idempotent). Call on the Unity main thread before any background work
        /// that needs to marshal back — i.e. during <see cref="GleapUnity.AttachAsync"/>.</summary>
        public static void Ensure()
        {
            if (_instance != null)
            {
                return;
            }

            _mainThreadId = Thread.CurrentThread.ManagedThreadId;
            var go = new GameObject("GleapUnityMainThread")
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<GleapUnityMainThread>();
        }

        /// <summary>Runs <paramref name="action"/> on the main thread: inline when already on it (or before the
        /// pump exists), otherwise on the next <see cref="Update"/> tick.</summary>
        public static void Run(Action action)
        {
            if (action == null)
            {
                return;
            }

            if (_instance == null || Thread.CurrentThread.ManagedThreadId == _mainThreadId)
            {
                action();
            }
            else
            {
                Queue.Enqueue(action);
            }
        }

        private void Update()
        {
            while (Queue.TryDequeue(out var action))
            {
                try
                {
                    action();
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
        }
    }
}
