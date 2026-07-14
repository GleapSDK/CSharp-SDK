using GleapSDK.Realtime;

namespace Gleap.Core.Tests.Fakes;

/// <summary>Test double for <see cref="IRealtimeChannel"/>: records Connect URLs, lets a test simulate
/// inbound frames via <see cref="Emit"/>, and exposes a settable <see cref="IsConnected"/>.</summary>
public sealed class FakeRealtimeChannel : IRealtimeChannel
{
    public event Action<string>? MessageReceived;

    public List<string> ConnectUrls { get; } = new();
    public bool IsConnected { get; set; }
    public bool Disposed { get; private set; }

    public void Connect(string url) => ConnectUrls.Add(url);
    public void Disconnect() { }
    public void Dispose() => Disposed = true;

    /// <summary>Simulate an inbound frame from the server.</summary>
    public void Emit(string json) => MessageReceived?.Invoke(json);
}
