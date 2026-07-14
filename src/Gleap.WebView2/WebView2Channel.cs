using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows.Threading;
using GleapSDK.Bridge;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace GleapSDK.WebView2;

/// <summary>
/// <see cref="IWebViewChannel"/> over any WPF WebView2 control (<see cref="IWebView2"/> — either the
/// windowed <see cref="Microsoft.Web.WebView2.Wpf.WebView2"/> or the airspace-free
/// <see cref="WebView2CompositionControl"/>). Injects the <c>GleapJSBridge</c> shim so the Gleap
/// widget's <c>/appnew</c> wrapper recognizes this host as native, forwards page-&gt;host messages
/// from <c>chrome.webview.postMessage</c>, and runs host-&gt;page scripts on the UI thread.
/// </summary>
public sealed class WebView2Channel : IWebViewChannel
{
    // Installed before the page loads (AddScriptToExecuteOnDocumentCreatedAsync). appnew.html calls
    // GleapJSBridge.gleapCallback(jsonString); we forward that to the C# WebMessageReceived handler.
    private const string BridgeShim =
        "window.GleapJSBridge = { gleapCallback: function (s) { window.chrome.webview.postMessage(s); } };";

    private readonly IWebView2 _webView;
    private readonly Dispatcher _dispatcher;
    private CoreWebView2? _core;

    public WebView2Channel(IWebView2 webView)
    {
        _webView = webView;
        _dispatcher = ((DispatcherObject)webView).Dispatcher;
    }

    /// <inheritdoc />
    public event Action<string>? MessageReceived;

    /// <summary>Ensures the CoreWebView2 exists, installs the bridge shim, and wires the receive event.
    /// Call once before <see cref="Navigate"/>.</summary>
    public async Task InitializeAsync()
    {
        await _webView.EnsureCoreWebView2Async(null).ConfigureAwait(true);
        _core = _webView.CoreWebView2;
        await _core.AddScriptToExecuteOnDocumentCreatedAsync(BridgeShim).ConfigureAwait(true);
        _core.WebMessageReceived += OnWebMessageReceived;
    }

    /// <summary>Navigates the control to the widget frame URL.</summary>
    public void Navigate(string url) => _core!.Navigate(url);

    /// <inheritdoc />
    public void ExecuteJavaScript(string script)
    {
        if (_dispatcher.CheckAccess())
        {
            RunScript(script);
        }
        else
        {
            _dispatcher.BeginInvoke(new Action(() => RunScript(script)));
        }
    }

    private void RunScript(string script)
    {
        try
        {
            _ = _core!.ExecuteScriptAsync(script);
        }
        catch (Exception ex)
        {
            Debug.WriteLine("Gleap: ExecuteScriptAsync failed: " + ex.Message);
        }
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        string json;
        try
        {
            json = e.TryGetWebMessageAsString();
        }
        catch (Exception ex)
        {
            Debug.WriteLine("Gleap: could not read web message: " + ex.Message);
            return;
        }
        MessageReceived?.Invoke(json);
    }
}
