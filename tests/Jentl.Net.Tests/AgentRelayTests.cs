using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;

namespace Jentl.Net.Tests;

public sealed class AgentRelayTests : IDisposable
{
    private readonly CancellationTokenSource _timeout = new(TimeSpan.FromSeconds(30));

    private CancellationToken Cancel => _timeout.Token;

    public void Dispose() => _timeout.Dispose();

    // The test agent is copied next to the tests by the project file.
    private static AgentCommand Agent(string mode) =>
        new("dotnet", [Path.Combine(AppContext.BaseDirectory, "TestAgent", "Jentl.Net.TestAgent.dll"), mode]);

    private static AgentCommand EchoAgent() => Agent("echo");

    private static async Task<object?> Next(IAsyncEnumerator<string> lines)
    {
        (await lines.MoveNextAsync()).Should().BeTrue("a message was expected before the connection closed");
        return Relay.Read(lines.Current);
    }

    [Fact]
    public void Messages_RoundTrip() =>
        Relay.Read(Relay.Write(new RelayHello("t", 1))).Should().Be(new RelayHello("t", 1));

    [Fact]
    public void EveryMessage_RoundTrips_AsCamelCaseJson_WithItsType()
    {
        RelayMessage[] messages = [new RelayHello("t", Relay.Version), new RelayLine("x"), new RelayExited("exited with code 3"),
            new RelayExited(null), new RelayWelcome("Bot"), new RelayRefused("no"), new RelayStart(), new RelayStop()];
        foreach (var message in messages)
        {
            Relay.Read(Relay.Write(message)).Should().Be(message);
        }

        Relay.Write(new RelayHello("t", 1)).Should().Be("""{"type":"hello","token":"t","version":1}""");
        var status = (RelayStatus)Relay.Read("""{"type":"status","status":{"rank":3}}""")!;
        status.Status.GetProperty("rank").GetInt32().Should().Be(3);
    }

    [Fact]
    public void UnknownMessages_ReadAsNull() => Relay.Read("""{"type":"nope"}""").Should().BeNull();

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"type":"line"}""")]
    [InlineData("""{"type":"line","text":null}""")]
    [InlineData("""{"text":"x"}""")]
    [InlineData("null")]
    public void BadMessages_ReadAsNull(string text) => Relay.Read(text).Should().BeNull();

    [Fact]
    public async Task TheRelay_SaysHello_StartsTheAgent_AndPassesLinesBothWays()
    {
        using var pair = SocketPair.Create();
        using var relay = new AgentRelay(_ => Task.FromResult(pair.Client), "token", EchoAgent());
        var run = relay.RunAsync(Cancel);
        var server = new SocketLines(pair.Server);
        var lines = server.ReadAllAsync(Cancel).GetAsyncEnumerator();
        (await Next(lines)).Should().Be(new RelayHello("token", 1));
        server.Send(Relay.Write(new RelayWelcome("Bot")));
        server.Send(Relay.Write(new RelayStart()));
        server.Send(Relay.Write(new RelayLine("ping")));
        (await Next(lines)).Should().Be(new RelayLine("ping"), "the echo agent answers with the same line");
    }

    [Fact]
    public async Task TheAgentsLines_GoBack_InTheOrderItWroteThem()
    {
        using var pair = SocketPair.Create();
        using var relay = new AgentRelay(_ => Task.FromResult(pair.Client), "token", EchoAgent());
        _ = relay.RunAsync(Cancel);
        using var server = new SocketLines(pair.Server);
        var lines = server.ReadAllAsync(Cancel).GetAsyncEnumerator();
        await Next(lines);
        server.Send(Relay.Write(new RelayWelcome("Bot")));
        server.Send(Relay.Write(new RelayStart()));
        for (var i = 0; i < 100; i++)
        {
            server.Send(Relay.Write(new RelayLine($"{i}")));
        }

        for (var i = 0; i < 100; i++)
        {
            (await Next(lines)).Should().Be(new RelayLine($"{i}"));
        }
    }

    [Fact]
    public async Task Refused_EndsTheRun()
    {
        using var pair = SocketPair.Create();
        using var relay = new AgentRelay(_ => Task.FromResult(pair.Client), "token", EchoAgent());
        var status = new ConcurrentQueue<string>();
        relay.Status += status.Enqueue;
        var run = relay.RunAsync(Cancel);
        using var server = new SocketLines(pair.Server);
        var lines = server.ReadAllAsync(Cancel).GetAsyncEnumerator();
        await Next(lines);
        server.Send(Relay.Write(new RelayRefused("unknown token")));
        await server.CloseAsync("refused");

        await run.WaitAsync(Cancel);
        status.Should().Contain("refused: unknown token");
    }

    [Fact]
    public async Task Welcome_AndStatus_AreShown()
    {
        using var pair = SocketPair.Create();
        using var relay = new AgentRelay(_ => Task.FromResult(pair.Client), "token", EchoAgent());
        var status = Channel.CreateUnbounded<string>();
        relay.Status += s => status.Writer.TryWrite(s);
        _ = relay.RunAsync(Cancel);
        using var server = new SocketLines(pair.Server);
        var lines = server.ReadAllAsync(Cancel).GetAsyncEnumerator();
        await Next(lines);
        server.Send(Relay.Write(new RelayWelcome("Bot")));
        server.Send(Relay.Write(new RelayStatus(JsonSerializer.SerializeToElement(new { rank = 3 }))));

        (await status.Reader.ReadAsync(Cancel)).Should().Be("connected as Bot");
        (await status.Reader.ReadAsync(Cancel)).Should().Be("""{"rank":3}""");
    }

    [Fact]
    public async Task TheAgentsExit_IsSent_WithItsProblem()
    {
        using var pair = SocketPair.Create();
        using var relay = new AgentRelay(_ => Task.FromResult(pair.Client), "token", Agent("crash"));
        _ = relay.RunAsync(Cancel);
        using var server = new SocketLines(pair.Server);
        var lines = server.ReadAllAsync(Cancel).GetAsyncEnumerator();
        await Next(lines);
        server.Send(Relay.Write(new RelayWelcome("Bot")));
        server.Send(Relay.Write(new RelayStart()));
        server.Send(Relay.Write(new RelayLine("x")));

        (await Next(lines)).Should().Be(new RelayExited("exited with code 3"));
    }

    [Fact]
    public async Task AStoppedAgent_NoLongerAnswers_AndAStartAfterwardsRunsAFreshOne()
    {
        using var pair = SocketPair.Create();
        using var relay = new AgentRelay(_ => Task.FromResult(pair.Client), "token", EchoAgent());
        _ = relay.RunAsync(Cancel);
        using var server = new SocketLines(pair.Server);
        var lines = server.ReadAllAsync(Cancel).GetAsyncEnumerator();
        await Next(lines);
        server.Send(Relay.Write(new RelayWelcome("Bot")));
        server.Send(Relay.Write(new RelayStart()));
        server.Send(Relay.Write(new RelayLine("one")));
        (await Next(lines)).Should().Be(new RelayLine("one"));

        server.Send(Relay.Write(new RelayStop()));
        server.Send(Relay.Write(new RelayLine("lost")));
        server.Send(Relay.Write(new RelayStart()));
        server.Send(Relay.Write(new RelayLine("two")));

        (await Next(lines)).Should().Be(new RelayLine("two"), "a stopped agent sends nothing more, not even its exit");
    }

    [Fact]
    public async Task AfterADroppedConnection_ItReconnects_AndKeepsTheAgent()
    {
        var pairs = Channel.CreateUnbounded<SocketPair>();
        var connects = 0;
        using var relay = new AgentRelay(_ =>
        {
            if (Interlocked.Increment(ref connects) == 2)
            {
                throw new System.Net.WebSockets.WebSocketException("no network");
            }

            var pair = SocketPair.Create();
            pairs.Writer.TryWrite(pair);
            return Task.FromResult(pair.Client);
        }, "token", EchoAgent());
        var status = new ConcurrentQueue<string>();
        relay.Status += status.Enqueue;
        _ = relay.RunAsync(Cancel);

        using var first = await pairs.Reader.ReadAsync(Cancel);
        using var server = new SocketLines(first.Server);
        var lines = server.ReadAllAsync(Cancel).GetAsyncEnumerator();
        await Next(lines);
        server.Send(Relay.Write(new RelayWelcome("Bot")));
        server.Send(Relay.Write(new RelayStart()));
        server.Send(Relay.Write(new RelayLine("a")));
        (await Next(lines)).Should().Be(new RelayLine("a"));
        first.Server.Abort();

        using var second = await pairs.Reader.ReadAsync(Cancel);
        using var again = new SocketLines(second.Server);
        var moreLines = again.ReadAllAsync(Cancel).GetAsyncEnumerator();
        (await Next(moreLines)).Should().Be(new RelayHello("token", 1));
        again.Send(Relay.Write(new RelayWelcome("Bot")));
        again.Send(Relay.Write(new RelayLine("b")));

        (await Next(moreLines)).Should().Be(new RelayLine("b"), "the agent kept running");
        status.Should().ContainInOrder("connected as Bot", "reconnecting in 1 s", "reconnecting in 2 s", "connected as Bot");
    }

    [Fact]
    public async Task AFlood_ClosesTheSocket()
    {
        using var pair = SocketPair.Create();
        var server = new SocketLines(pair.Server, maxLength: 100);
        var read = server.ReadAllAsync(Cancel).ToListAsync(Cancel);
        await new SocketLines(pair.Client).SendNowAsync(new string('x', 101));   // test helper on the client side
        (await read).Should().BeEmpty();
        server.Problem.Should().Contain("too long");
    }

    [Fact]
    public async Task TooManyMessagesInASecond_CloseTheSocket()
    {
        using var pair = SocketPair.Create();
        using var server = new SocketLines(pair.Server, maxPerSecond: 10);
        var read = server.ReadAllAsync(Cancel).ToListAsync(Cancel);
        using var client = new SocketLines(pair.Client);
        var clientRead = client.ReadAllAsync(Cancel).ToListAsync(Cancel);
        for (var i = 0; i < 11; i++)
        {
            client.Send($"{i}");
        }

        (await read).Should().HaveCount(10);
        server.Problem.Should().Contain("too many");
        await clientRead;
        pair.Client.CloseStatus.Should().Be(System.Net.WebSockets.WebSocketCloseStatus.PolicyViolation);
    }

    [Fact]
    public async Task BinaryMessages_CloseTheSocket()
    {
        using var pair = SocketPair.Create();
        using var server = new SocketLines(pair.Server, maxPerSecond: 10);
        var read = server.ReadAllAsync(Cancel).ToListAsync(Cancel);
        using var client = new SocketLines(pair.Client);
        var clientRead = client.ReadAllAsync(Cancel).ToListAsync(Cancel);
        await SendFramesAsync(pair.Client, 50, System.Net.WebSockets.WebSocketMessageType.Binary, [1, 2, 3], endOfMessage: true);

        (await read).Should().BeEmpty();
        server.Problem.Should().Contain("binary");
        await clientRead;
        pair.Client.CloseStatus.Should().Be(System.Net.WebSockets.WebSocketCloseStatus.PolicyViolation);
    }

    [Fact]
    public async Task AMessageThatNeverEnds_InEmptyFrames_ClosesTheSocket()
    {
        using var pair = SocketPair.Create();
        using var server = new SocketLines(pair.Server);
        var read = server.ReadAllAsync(Cancel).ToListAsync(Cancel);
        using var client = new SocketLines(pair.Client);
        var clientRead = client.ReadAllAsync(Cancel).ToListAsync(Cancel);
        await SendFramesAsync(pair.Client, 1, System.Net.WebSockets.WebSocketMessageType.Text, "x"u8.ToArray(), endOfMessage: false);
        await SendFramesAsync(pair.Client, SocketLines.MaxFrames + 10, System.Net.WebSockets.WebSocketMessageType.Text, [], endOfMessage: false);

        (await read).Should().BeEmpty();
        server.Problem.Should().Contain("too many frames");
        await clientRead;
        pair.Client.CloseStatus.Should().Be(System.Net.WebSockets.WebSocketCloseStatus.PolicyViolation);
    }

    /// <summary>Writes raw frames until the other side closes.</summary>
    private static async Task SendFramesAsync(System.Net.WebSockets.WebSocket socket, int count, System.Net.WebSockets.WebSocketMessageType type, byte[] data, bool endOfMessage)
    {
        try
        {
            for (var i = 0; i < count && socket.State == System.Net.WebSockets.WebSocketState.Open; i++)
            {
                await socket.SendAsync(data, type, endOfMessage, CancellationToken.None);
            }
        }
        catch (System.Net.WebSockets.WebSocketException)
        {
            // The other side closed: that is what the test waits for.
        }
    }

    [Fact]
    public async Task Closing_SendsWhatWasQueuedFirst_AndEndsTheOtherSidesReading()
    {
        using var pair = SocketPair.Create();
        using var server = new SocketLines(pair.Server);
        using var client = new SocketLines(pair.Client);
        var read = client.ReadAllAsync(Cancel).ToListAsync(Cancel);
        server.Send("one");
        server.Send("two");
        await server.CloseAsync("bye");

        (await read).Should().Equal("one", "two");
        client.Problem.Should().BeNull();
    }
}
