using UnityEngine;

namespace GleapSDK.Unity
{
    /// <summary>
    /// Optional MonoBehaviour that forwards Unity console output into Gleap's console log so it is
    /// attached to reports. Add it to a persistent GameObject after calling <c>GleapUnity.AttachAsync</c>.
    /// </summary>
    public sealed class GleapUnityLogHook : MonoBehaviour
    {
        private void OnEnable()
        {
            Application.logMessageReceived += OnLog;
        }

        private void OnDisable()
        {
            Application.logMessageReceived -= OnLog;
        }

        private static void OnLog(string message, string stackTrace, LogType type)
        {
            var level = type == LogType.Error || type == LogType.Exception || type == LogType.Assert
                ? LogLevel.Error
                : type == LogType.Warning
                    ? LogLevel.Warning
                    : LogLevel.Info;

            try
            {
                Gleap.Log(message, level);
            }
            catch
            {
                // Never let logging capture crash the game.
            }
        }
    }
}
