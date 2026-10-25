using System.IO.Pipelines;
using System.Net.WebSockets;
using System.Text;

namespace Jentl.Net.Tests;

/// <summary>
/// Two WebSockets talking to each other in memory, a client and a server, over two pipes (one each way). Aborting or
/// disposing either side drops the connection for the other, as a broken network would.
/// </summary>
public sealed class SocketPair : IDisposable
{
    private SocketPair(WebSocket client, WebSocket server) => (Client, Server) = (client, server);

    public WebSocket Client { get; }

    public WebSocket Server { get; }

    public static SocketPair Create()
    {
        var up = new Pipe();
        var down = new Pipe();
        var options = new WebSocketCreationOptions { KeepAliveInterval = TimeSpan.Zero };
        var client = WebSocket.CreateFromStream(new Duplex(down.Reader, up.Writer), options);
        var server = WebSocket.CreateFromStream(new Duplex(up.Reader, down.Writer), new WebSocketCreationOptions { IsServer = true, KeepAliveInterval = TimeSpan.Zero });
        return new SocketPair(client, server);
    }

    public void Dispose()
    {
        Client.Dispose();
        Server.Dispose();
    }

    /// <summary>One end of the pair: reads what the other end writes; disposing it ends the other end's input.</summary>
    private sealed class Duplex(PipeReader reader, PipeWriter writer) : Stream
    {
        private readonly Stream _in = reader.AsStream();
        private readonly Stream _out = writer.AsStream();

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) => _in.Read(buffer, offset, count);

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => _in.ReadAsync(buffer, offset, count, cancellationToken);

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => _in.ReadAsync(buffer, cancellationToken);

        public override void Write(byte[] buffer, int offset, int count) => _out.Write(buffer, offset, count);

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => _out.WriteAsync(buffer, offset, count, cancellationToken);

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) => _out.WriteAsync(buffer, cancellationToken);

        public override void Flush() => _out.Flush();

        public override Task FlushAsync(CancellationToken cancellationToken) => _out.FlushAsync(cancellationToken);

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _in.Dispose();
                _out.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}

public static class SocketLinesTestExtensions
{
    /// <summary>Writes <paramref name="text"/> as one text frame straight away, past the queue (and anything it would check).</summary>
    public static Task SendNowAsync(this SocketLines lines, string text) =>
        lines.Socket.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, endOfMessage: true, CancellationToken.None);
}
