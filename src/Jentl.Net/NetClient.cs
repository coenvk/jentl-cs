using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Threading.Channels;

namespace Jentl.Net;

public enum NetStatus
{
    Connecting,
    Connected,

    /// <summary>The connection failed or ended; <see cref="NetClient.Error"/> says why.</summary>
    Closed,
}

/// <summary>
/// A connection to a room server over a WebSocket, made for a game loop: messages arrive in the background and
/// wait in a queue until <see cref="TryReceive"/> takes them, once per frame. Works on desktop and in the browser.
/// </summary>
public sealed class NetClient : IDisposable
{
    private readonly ConcurrentQueue<ServerMessage> _inbox = new();
    private readonly Channel<string> _outbox = Channel.CreateUnbounded<string>();
    private readonly CancellationTokenSource _stop = new();
    private readonly Func<CancellationToken, Task<WebSocket>> _connect;
    private volatile NetStatus _status = NetStatus.Connecting;
    private string? _error;

    /// <summary>Connects to <paramref name="server"/> (<c>ws://</c> or <c>wss://</c>).</summary>
    public NetClient(Uri server)
        : this(async cancel =>
        {
            var socket = new ClientWebSocket();
            await socket.ConnectAsync(server, cancel).ConfigureAwait(false);
            return socket;
        })
    {
    }

    /// <summary>Connects with any WebSocket, e.g. a test server's.</summary>
    public NetClient(Func<CancellationToken, Task<WebSocket>> connect)
    {
        _connect = connect ?? throw new ArgumentNullException(nameof(connect));
        _ = RunAsync();
    }

    public NetStatus Status => _status;

    public string? Error => _error;

    /// <summary>Queues a message; it goes as soon as the connection is open.</summary>
    public void Send(ClientMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        _outbox.Writer.TryWrite(Protocol.Write(message));
    }

    public bool TryReceive(out ServerMessage message) => _inbox.TryDequeue(out message!);

    public void Dispose()
    {
        _outbox.Writer.TryComplete();
        _stop.Cancel();
        _stop.Dispose();
    }

    private async Task RunAsync()
    {
        WebSocket? socket = null;
        try
        {
            var cancel = _stop.Token;
            socket = await _connect(cancel).ConfigureAwait(false);
            _status = NetStatus.Connected;
            var sending = SendLoopAsync(socket, cancel);
            await ReceiveLoopAsync(socket, cancel).ConfigureAwait(false);
            _outbox.Writer.TryComplete();
            await sending.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _error ??= "Disconnected.";
        }
        catch (ObjectDisposedException)
        {
            _error ??= "Disconnected.";
        }
#pragma warning disable CA1031 // Any network failure just ends the connection, with its message for the screen.
        catch (Exception e)
#pragma warning restore CA1031
        {
            _error ??= e is WebSocketException or System.Net.Http.HttpRequestException ? "Could not reach the server." : e.Message;
        }
        finally
        {
            _status = NetStatus.Closed;
            socket?.Dispose();
        }
    }

    private async Task SendLoopAsync(WebSocket socket, CancellationToken cancel)
    {
        await foreach (var text in _outbox.Reader.ReadAllAsync(cancel).ConfigureAwait(false))
        {
            if (socket.State != WebSocketState.Open)
            {
                return;
            }

            await socket.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, endOfMessage: true, cancel).ConfigureAwait(false);
        }
    }

    private async Task ReceiveLoopAsync(WebSocket socket, CancellationToken cancel)
    {
        var buffer = new byte[8192];
        using var message = new MemoryStream();
        while (socket.State == WebSocketState.Open)
        {
            var result = await socket.ReceiveAsync(buffer, cancel).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                _error ??= "The server closed the connection.";
                return;
            }

            message.Write(buffer, 0, result.Count);
            if (message.Length > Protocol.MaxMessageSize)
            {
                _error = "The server sent too much.";
                return;
            }

            if (result.EndOfMessage)
            {
                if (Protocol.ReadServer(Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length)) is { } parsed)
                {
                    _inbox.Enqueue(parsed);
                }

                message.SetLength(0);
            }
        }
    }
}
