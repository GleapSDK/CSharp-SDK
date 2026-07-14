using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GleapSDK.Realtime;

/// <summary>
/// Real-time channel to <c>wss://ws.gleap.io</c>, matching the native iOS/JS SDKs: connects with the
/// session identity in the query string, keeps the connection alive with a periodic <c>PING</c>, and
/// auto-reconnects after a drop. Each inbound frame's raw JSON is raised via <see cref="MessageReceived"/>;
/// parsing/dispatch is the backend's job. A single background loop owns the socket; <see cref="Connect"/>
/// can be called again (e.g. on identity change) to reconnect with a fresh URL.
/// </summary>
public sealed class GleapWebSocket : IRealtimeChannel
{
    private static readonly TimeSpan KeepAliveInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(5);
    private const int ReceiveBufferSize = 8192;

    private readonly object _gate = new object();
    private string? _url;
    private CancellationTokenSource? _masterCts;
    private CancellationTokenSource? _connectionCts;
    private Task? _loop;
    private volatile bool _connected;
    private bool _disposed;

    /// <inheritdoc />
    public event Action<string>? MessageReceived;

    /// <inheritdoc />
    public bool IsConnected => _connected;

    /// <inheritdoc />
    public void Connect(string url)
    {
        if (string.IsNullOrEmpty(url))
        {
            return;
        }
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }
            _url = url;
            if (_loop == null)
            {
                _masterCts = new CancellationTokenSource();
                var token = _masterCts.Token;
                _loop = Task.Run(() => RunAsync(token));
            }
            else
            {
                // Already looping — drop the current connection so it reconnects to the new URL.
                CancelConnection();
            }
        }
    }

    /// <inheritdoc />
    public void Disconnect()
    {
        lock (_gate)
        {
            _url = null;
            CancelMaster();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            _url = null;
            CancelMaster();
        }
    }

    private void CancelConnection()
    {
        try { _connectionCts?.Cancel(); }
        catch (ObjectDisposedException) { /* already gone */ }
    }

    private void CancelMaster()
    {
        try { _masterCts?.Cancel(); }
        catch (ObjectDisposedException) { /* already gone */ }
    }

    private async Task RunAsync(CancellationToken masterCt)
    {
        while (!masterCt.IsCancellationRequested)
        {
            string? url;
            lock (_gate)
            {
                url = _url;
            }
            if (string.IsNullOrEmpty(url))
            {
                await DelayQuietly(ReconnectDelay, masterCt).ConfigureAwait(false);
                continue;
            }

            var connectionCts = CancellationTokenSource.CreateLinkedTokenSource(masterCt);
            lock (_gate)
            {
                _connectionCts = connectionCts;
            }

            try
            {
                using var socket = new ClientWebSocket();
                await socket.ConnectAsync(new Uri(url!), connectionCts.Token).ConfigureAwait(false);
                _connected = true;

                var keepAlive = KeepAliveLoopAsync(socket, connectionCts.Token);
                await ReceiveLoopAsync(socket, connectionCts.Token).ConfigureAwait(false);

                connectionCts.Cancel();               // stop the keepalive
                await keepAlive.ConfigureAwait(false);
                await CloseQuietlyAsync(socket).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Master cancel or a forced reconnect (Connect with a new URL) — loop decides below.
            }
            catch (Exception)
            {
                // Connect/receive failure — fall through to the reconnect delay.
            }
            finally
            {
                _connected = false;
                lock (_gate)
                {
                    if (ReferenceEquals(_connectionCts, connectionCts))
                    {
                        _connectionCts = null;
                    }
                }
                connectionCts.Dispose();
            }

            if (!masterCt.IsCancellationRequested)
            {
                await DelayQuietly(ReconnectDelay, masterCt).ConfigureAwait(false);
            }
        }
    }

    private async Task ReceiveLoopAsync(ClientWebSocket socket, CancellationToken ct)
    {
        var buffer = new byte[ReceiveBufferSize];
        var message = new StringBuilder();
        while (!ct.IsCancellationRequested && socket.State == WebSocketState.Open)
        {
            WebSocketReceiveResult result;
            try
            {
                result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                break;   // socket error → reconnect
            }

            if (result.MessageType == WebSocketMessageType.Close)
            {
                break;
            }

            message.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
            if (!result.EndOfMessage)
            {
                continue;
            }

            var text = message.ToString();
            message.Clear();
            // Ignore keepalive echoes; JSON frames are dispatched to the backend.
            if (!string.IsNullOrEmpty(text) && text != "PONG" && text != "PING")
            {
                try { MessageReceived?.Invoke(text); }
                catch (Exception) { /* a handler must never kill the receive loop */ }
            }
        }
    }

    private static async Task KeepAliveLoopAsync(ClientWebSocket socket, CancellationToken ct)
    {
        var ping = Encoding.UTF8.GetBytes("PING");
        try
        {
            while (!ct.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                await Task.Delay(KeepAliveInterval, ct).ConfigureAwait(false);
                await socket.SendAsync(new ArraySegment<byte>(ping), WebSocketMessageType.Text, true, ct)
                    .ConfigureAwait(false);
            }
        }
        catch (Exception)
        {
            // Cancelled or the socket is closing — the receive loop handles reconnection.
        }
    }

    private static async Task CloseQuietlyAsync(ClientWebSocket socket)
    {
        if (socket.State != WebSocketState.Open)
        {
            return;
        }
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", timeout.Token).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Best-effort close.
        }
    }

    private static async Task DelayQuietly(TimeSpan delay, CancellationToken ct)
    {
        try { await Task.Delay(delay, ct).ConfigureAwait(false); }
        catch (OperationCanceledException) { /* shutting down */ }
    }
}
