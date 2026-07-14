using System;
using GleapSDK.Bridge;

namespace GleapSDK.Unity
{
    /// <summary>
    /// A plugin-agnostic <see cref="IWebViewChannel"/> for Unity. Unity has no built-in WebView, so you
    /// bring your own (Vuplex / 3D WebView on standalone+mobile, a <c>.jslib</c> iframe on WebGL) and glue
    /// it to Gleap with two calls:
    /// <list type="number">
    /// <item>construct with a <paramref name="runJavaScript"/> delegate that runs a script string in your
    /// WebView (host→widget uses <c>window.sendMessage({...})</c>);</item>
    /// <item>inject <see cref="BridgeShim"/> into the page <b>before it loads</b>, wired so the widget's
    /// <c>GleapJSBridge.gleapCallback(jsonString)</c> ends up calling <see cref="ReceiveFromWidget"/>.</item>
    /// </list>
    /// Then load <c>GleapEndpoints.Default.FrameUrl</c> (<c>https://messenger-app.gleap.io/appnew</c>).
    /// The channel only moves bytes — the ping handshake is answered by Gleap.Core's WidgetBootstrapper.
    /// </summary>
    /// <example>
    /// With Vuplex 3D WebView:
    /// <code>
    /// var web = /* IWebView */;
    /// var channel = new GleapUnityWebViewChannel(js => web.ExecuteJavaScript(js));
    /// web.MessageEmitted += (s, e) => channel.ReceiveFromWidget(e.Value); // if you post via window.vuplex
    /// // or inject GleapUnityWebViewChannel.BridgeShim with your own postMessage hook that calls ReceiveFromWidget.
    /// await web.LoadUrl(GleapEndpoints.Default.FrameUrl);
    /// await GleapUnity.AttachAsync(channel, "YOUR_SDK_KEY");
    /// </code>
    /// </example>
    public sealed class GleapUnityWebViewChannel : IWebViewChannel
    {
        /// <summary>
        /// The bridge shim the widget's <c>/appnew</c> wrapper expects, to inject before page load. The
        /// <c>&lt;POST s TO UNITY&gt;</c> placeholder must route the string to <see cref="ReceiveFromWidget"/>
        /// (e.g. via your WebView plugin's page→native message channel, or <c>window.vuplex.postMessage</c>).
        /// </summary>
        public const string BridgeShim =
            "window.GleapJSBridge = { gleapCallback: function (s) { /* <POST s TO UNITY> */ } };";

        private readonly Action<string> _runJavaScript;

        public GleapUnityWebViewChannel(Action<string> runJavaScript)
            => _runJavaScript = runJavaScript ?? throw new ArgumentNullException(nameof(runJavaScript));

        /// <inheritdoc />
        public event Action<string>? MessageReceived;

        /// <inheritdoc />
        public void ExecuteJavaScript(string script) => _runJavaScript(script);

        /// <summary>Call this from your WebView's page→native bridge with the raw JSON string the widget
        /// posted via <c>GleapJSBridge.gleapCallback</c>.</summary>
        public void ReceiveFromWidget(string json) => MessageReceived?.Invoke(json);
    }
}
