using System;
using System.Diagnostics;
using System.Threading.Tasks;
using GleapSDK.Bridge;
using Microsoft.Web.WebView2.Core;

namespace GleapSDK.WebView2;

/// <summary>
/// <see cref="IWebViewChannel"/> over a WPF <see cref="Microsoft.Web.WebView2.Wpf.WebView2"/> control.
/// Injects the <c>GleapJSBridge</c> shim so the Gleap widget's <c>/appnew</c> wrapper recognizes
/// this host as native, forwards page-&gt;host messages from <c>chrome.webview.postMessage</c>, and
/// runs host-&gt;page scripts on the UI thread.
/// </summary>
public sealed class WebView2Channel : IWebViewChannel
{
    // Installed before the page loads (AddScriptToExecuteOnDocumentCreatedAsync). appnew.html calls
    // GleapJSBridge.gleapCallback(jsonString); we forward that to the C# WebMessageReceived handler.
    private const string BridgeShim =
        "window.GleapJSBridge = { gleapCallback: function (s) { window.chrome.webview.postMessage(s); } };";

    private readonly Microsoft.Web.WebView2.Wpf.WebView2 _webView;
    private CoreWebView2? _core;

    public WebView2Channel(Microsoft.Web.WebView2.Wpf.WebView2 webView) => _webView = webView;

    /// <inheritdoc />
    public event Action<string>? MessageReceived;

    /// <summary>Ensures the CoreWebView2 exists, installs the bridge shim, and wires the receive event.
    /// Call once before <see cref="Navigate"/>.</summary>
    public async Task InitializeAsync()
    {
        await _webView.EnsureCoreWebView2Async().ConfigureAwait(true);
        _core = _webView.CoreWebView2;
        await _core.AddScriptToExecuteOnDocumentCreatedAsync(BridgeShim).ConfigureAwait(true);
        _core.WebMessageReceived += OnWebMessageReceived;
    }

    /// <summary>Navigates the control to the widget frame URL.</summary>
    public void Navigate(string url) => _core!.Navigate(url);

    /// <inheritdoc />
    public void ExecuteJavaScript(string script)
    {
        if (_webView.Dispatcher.CheckAccess())
        {
            RunScript(script);
        }
        else
        {
            _webView.Dispatcher.BeginInvoke(new Action(() => RunScript(script)));
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
