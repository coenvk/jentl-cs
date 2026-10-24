using System.Net.WebSockets;
using System.Text;
using System.Threading.Channels;

namespace Jentl.Net;

/// <summary>
/// The server side of one player's WebSocket: reads their messages into a <see cref="RoomServer"/> and writes
/// what the rooms send back, until either side closes. Messages that are too big end the connection.
/// </summary>
public sealed class SocketConnection(WebSocket socket, RoomServer rooms) : IConnection, IDisposable
{
    private readonly Channel<string> _outbox = Channel.CreateBounded<string>(new BoundedChannelOptions(256) { FullMode = BoundedChannelFullMode.DropOldest });
    private readonly CancellationTokenSource _stop = new();

    public void Send(string message) => _outbox.Writer.TryWrite(message);

    public void Close()
    {
        try
        {
            _stop.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    public void Dispose() => _stop.Dispose();

    /// <summary>Serves the player until the connection ends.</summary>
    public async Task RunAsync(CancellationToken aborted)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(aborted, _stop.Token);
        var cancel = linked.Token;
        var writing = WriteLoopAsync(cancel);
        try
        {
            await ReadLoopAsync(cancel).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (WebSocketException)
        {
        }
        finally
        {
            rooms.Disconnected(this);
            _outbox.Writer.TryComplete();
            try
            {
                await writing.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (WebSocketException)
            {
            }
        }
    }

    private async Task ReadLoopAsync(CancellationToken cancel)
    {
        var buffer = new byte[4096];
        using var message = new MemoryStream();
        while (socket.State == WebSocketState.Open)
        {
            var result = await socket.ReceiveAsync(buffer, cancel).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Bye", cancel).ConfigureAwait(false);
                return;
            }

            message.Write(buffer, 0, result.Count);
            if (message.Length > Protocol.MaxMessageSize)
            {
                await socket.CloseOutputAsync(WebSocketCloseStatus.MessageTooBig, "Too big", cancel).ConfigureAwait(false);
                return;
            }

            if (result.EndOfMessage)
            {
                rooms.Received(this, Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length));
                message.SetLength(0);
            }
        }
    }

    private async Task WriteLoopAsync(CancellationToken cancel)
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
}
