using System;

namespace GleapSDK.Bridge;

/// <summary>
/// Transport between native/managed and the hosted web widget.
/// A platform WebView host (WebView2, Unity plugin) implements this;
/// tests use a fake. Native side runs "sendMessage(&lt;json&gt;)" and receives
/// raw JSON strings the page posts back.
/// </summary>
public interface IWebViewChannel
{
    void ExecuteJavaScript(string script);
    event Action<string> MessageReceived;
}
