using System;
using System.Collections.Generic;
using GleapSDK.Bridge;

namespace Gleap.Core.Tests.Fakes;

public sealed class FakeWebViewChannel : IWebViewChannel
{
    public List<string> ExecutedScripts { get; } = new();

    public void ExecuteJavaScript(string script) => ExecutedScripts.Add(script);

    public event Action<string>? MessageReceived;

    /// <summary>Simulate the web page posting a message to native.</summary>
    public void SimulateIncoming(string json) => MessageReceived?.Invoke(json);
}
