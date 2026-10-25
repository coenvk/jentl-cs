using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Channels;

namespace Jentl.Net;

/// <summary>
/// A WebSocket as lines of text, one text frame each, with limits for a peer that misbehaves: a message longer than
/// <c>maxLength</c> bytes, or more than <c>maxPerSecond</c> messages within one second, closes the socket with a
/// policy-violation status, and <see cref="Problem"/> says which. Sending never blocks: lines are queued and written in
/// the background.
/// </summary>
/// <remarks>
/// At most <see cref="MaxQueued"/> lines wait to be sent; when more come (the peer stopped reading), the oldest are
/// dropped. Read with one <see cref="ReadAllAsync"/> at a time, as a WebSocket allows one receive at a time.
/// </remarks>
public sealed class SocketLines : IDisposable
{
    /// <summary>How many lines wait to be sent at most; when more come, the oldest are dropped.</summary>
    public const int MaxQueued = 1024;

    // How long closing waits for the queued lines to go out before it gives up on them.
    private static readonly TimeSpan CloseWait = TimeSpan.FromSeconds(5);

    private readonly WebSocket _socket;
    private readonly int _maxLength;
    private readonly int _maxPerSecond;
    private readonly Channel<string> _outgoing = Channel.CreateBounded<string>(
        new BoundedChannelOptions(MaxQueued) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
    private readonly SemaphoreSlim _sending = new(1, 1);
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _writing;
    private volatile string? _problem;

    public SocketLines(WebSocket socket, int maxLength = 65536, int maxPerSecond = 200)
    {
        ArgumentNullException.ThrowIfNull(socket);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxLength, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxPerSecond, 1);
        (_socket, _maxLength, _maxPerSecond) = (socket, maxLength, maxPerSecond);
        _writing = Task.Run(WriteLoopAsync);
    }

    /// <summary>Why the socket was closed on the peer ("message too long …", "too many messages …"), or null.</summary>
    public string? Problem => _problem;

    /// <summary>The socket underneath, for tests that need to write to it directly.</summary>
    internal WebSocket Socket => _socket;

    /// <summary>Queues <paramref name="text"/> to go as one text frame. Never blocks; does nothing once the socket is closed.</summary>
    public void Send(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        _outgoing.Writer.TryWrite(text);
    }

    /// <summary>
    /// Every message the peer sends, as it comes, until the socket closes (by either side, or because the connection
    /// broke) or a limit is broken. Throws only when <paramref name="cancel"/> is cancelled.
    /// </summary>
    public async IAsyncEnumerable<string> ReadAllAsync([EnumeratorCancellation] CancellationToken cancel)
    {
        var buffer = new byte[4096];
        using var message = new MemoryStream();
        var times = new Queue<long>(); // when each of the last messages came, within the last second
        while (await ReceiveAsync(buffer, message, cancel).ConfigureAwait(false) is { } text)
        {
            var now = Environment.TickCount64;
            while (times.Count > 0 && now - times.Peek() >= 1000)
            {
                times.Dequeue();
            }

            if (times.Count >= _maxPerSecond)
            {
                await ViolatedAsync($"too many messages (over {_maxPerSecond} a second)").ConfigureAwait(false);
                yield break;
            }

            times.Enqueue(now);
            yield return text;
        }
    }

    /// <summary>Sends what is still queued (waiting a few seconds at most), then closes the socket with <paramref name="reason"/>.</summary>
    public async Task CloseAsync(string reason)
    {
        _outgoing.Writer.TryComplete();
        await Task.WhenAny(_writing, Task.Delay(CloseWait)).ConfigureAwait(false);
        await CloseOutputAsync(WebSocketCloseStatus.NormalClosure, reason).ConfigureAwait(false);
    }

    /// <summary>Stops sending. The socket itself belongs to whoever made it.</summary>
    public void Dispose()
    {
        _outgoing.Writer.TryComplete();
        try
        {
            _stop.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    /// <summary>The next whole text message, or null when the socket closed or broke, or the message was too long.</summary>
    private async Task<string?> ReceiveAsync(byte[] buffer, MemoryStream message, CancellationToken cancel)
    {
        message.SetLength(0);
        try
        {
            while (_socket.State == WebSocketState.Open)
            {
                var result = await _socket.ReceiveAsync(buffer, cancel).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye").ConfigureAwait(false);
                    return null;
                }

                message.Write(buffer, 0, result.Count);
                if (message.Length > _maxLength)
                {
                    await ViolatedAsync($"message too long (over {_maxLength} bytes)").ConfigureAwait(false);
                    return null;
                }

                if (result.EndOfMessage)
                {
                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        return Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length);
                    }

                    message.SetLength(0); // a binary frame is no line: skipped
                }
            }
        }
        catch (WebSocketException)
        {
            // The connection broke: the same as the end.
        }
        catch (ObjectDisposedException)
        {
            // The socket was disposed: the same as the end.
        }
        catch (OperationCanceledException) when (!cancel.IsCancellationRequested)
        {
            // The socket was aborted: the same as the end.
        }

        return null;
    }

    private async Task ViolatedAsync(string problem)
    {
        _problem = problem;
        _outgoing.Writer.TryComplete();
        await CloseOutputAsync(WebSocketCloseStatus.PolicyViolation, problem).ConfigureAwait(false);
    }

    /// <summary>Sends the close frame once, if the socket still may; a broken socket is closed already.</summary>
    private async Task CloseOutputAsync(WebSocketCloseStatus status, string reason)
    {
        using var timeout = new CancellationTokenSource(CloseWait);
        try
        {
            await _sending.WaitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // A send is stuck on a peer that does not read: give up on it.
            _socket.Abort();
            return;
        }

        try
        {
            if (_socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                await _socket.CloseOutputAsync(status, Shorten(reason), timeout.Token).ConfigureAwait(false);
            }
        }
        catch (Exception e) when (e is WebSocketException or ObjectDisposedException or OperationCanceledException)
        {
            // Broken or gone already.
        }
        finally
        {
            _sending.Release();
        }
    }

    private async Task WriteLoopAsync()
    {
        var cancel = _stop.Token;
        try
        {
            await foreach (var text in _outgoing.Reader.ReadAllAsync(cancel).ConfigureAwait(false))
            {
                await _sending.WaitAsync(cancel).ConfigureAwait(false);
                try
                {
                    if (_socket.State != WebSocketState.Open)
                    {
                        return;
                    }

                    await _socket.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, endOfMessage: true, cancel).ConfigureAwait(false);
                }
                finally
                {
                    _sending.Release();
                }
            }
        }
        catch (Exception e) when (e is WebSocketException or ObjectDisposedException or OperationCanceledException)
        {
            // The socket broke or was closed: what is still queued is lost with it.
        }
        finally
        {
            _outgoing.Writer.TryComplete();
        }
    }

    /// <summary>A close reason may be at most 123 bytes of UTF-8.</summary>
    private static string Shorten(string reason)
    {
        var text = reason ?? "";
        while (Encoding.UTF8.GetByteCount(text) > 123)
        {
            text = text[..^1];
        }

        return text;
    }
}
