using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Threading.Channels;

namespace Jentl.Net.Tests;

public sealed class RemoteAgentTests : IDisposable
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(5);

    private readonly CancellationTokenSource _timeout = new(TimeSpan.FromSeconds(30));
    private readonly ConcurrentBag<IDisposable> _owned = [];
    private readonly ConcurrentBag<Task> _runs = [];

    private CancellationToken Cancel => _timeout.Token;

    public void Dispose()
    {
        foreach (var owned in _owned)
        {
            owned.Dispose();
        }

        _timeout.Dispose();
    }

    private static AgentCommand EchoAgent() =>
        new("dotnet", [Path.Combine(AppContext.BaseDirectory, "TestAgent", "Jentl.Net.TestAgent.dll"), "echo"]);

    private static RelayHost Host() => new(token => token switch { "t" => "Bot", "a" => "A", "b" => "B", _ => null });

    /// <summary>The events of <paramref name="host"/> as "+Bot" (connected) and "-Bot" (disconnected), in order.</summary>
    private static Channel<string> Events(RelayHost host)
    {
        var events = Channel.CreateUnbounded<string>();
        host.Connected += name => events.Writer.TryWrite($"+{name}");
        host.Disconnected += name => events.Writer.TryWrite($"-{name}");
        return events;
    }

    /// <summary>A fresh socket pair whose server end <paramref name="host"/> serves; the pair is disposed with the test.</summary>
    private SocketPair Serve(RelayHost host)
    {
        var pair = SocketPair.Create();
        _owned.Add(pair);
        _runs.Add(host.RunAsync(pair.Server, Cancel));
        return pair;
    }

    /// <summary>A runner with the echo agent for <paramref name="token"/>, each connection a new pair served by <paramref name="host"/>.</summary>
    private (AgentRelay Relay, Channel<SocketPair> Pairs, Channel<string> Status) Runner(RelayHost host, string token)
    {
        var pairs = Channel.CreateUnbounded<SocketPair>();
        var relay = new AgentRelay(_ =>
        {
            var pair = Serve(host);
            pairs.Writer.TryWrite(pair);
            return Task.FromResult(pair.Client);
        }, token, EchoAgent());
        _owned.Add(relay);
        var status = Channel.CreateUnbounded<string>();
        relay.Status += s => status.Writer.TryWrite(s);
        _ = relay.RunAsync(Cancel);
        return (relay, pairs, status);
    }

    private async Task<string> NextStatus(Channel<string> status, Func<string, bool> wanted)
    {
        while (true)
        {
            var s = await status.Reader.ReadAsync(Cancel);
            if (wanted(s))
            {
                return s;
            }
        }
    }

    [Fact]
    public async Task AnUnknownToken_IsRefused()
    {
        var host = Host();
        var pair = Serve(host);
        using var client = new SocketLines(pair.Client);
        var read = client.ReadAllAsync(Cancel).ToListAsync(Cancel);
        client.Send(Relay.Write(new RelayHello("nope", Relay.Version)));

        (await read).Select(Relay.Read).Should().Equal(new RelayRefused("unknown token"));
        host.Agent("Bot").Should().BeNull();
    }

    [Fact]
    public async Task AWrongVersion_IsRefused()
    {
        var host = Host();
        var pair = Serve(host);
        using var client = new SocketLines(pair.Client);
        var read = client.ReadAllAsync(Cancel).ToListAsync(Cancel);
        client.Send(Relay.Write(new RelayHello("t", Relay.Version + 1)));

        var messages = (await read).Select(Relay.Read).ToList();
        messages.Should().ContainSingle().Which.Should().BeOfType<RelayRefused>().Which.Reason.Should().Contain("version");
    }

    [Fact]
    public async Task NoHello_ClosesTheConnection_WithoutThrowing()
    {
        var host = Host();
        var pair = Serve(host);
        using var client = new SocketLines(pair.Client);
        var read = client.ReadAllAsync(Cancel).ToListAsync(Cancel);
        client.Send(Relay.Write(new RelayLine("not a hello")));

        (await read).Select(Relay.Read).Should().ContainSingle().Which.Should().BeOfType<RelayRefused>();
        await Task.WhenAll(_runs).WaitAsync(Cancel);
    }

    [Fact]
    public async Task ASilentConnection_IsClosed_AfterFiveSeconds()
    {
        var host = Host();
        var pair = Serve(host);
        using var client = new SocketLines(pair.Client);
        var watch = Stopwatch.StartNew();

        (await client.ReadAllAsync(Cancel).ToListAsync(Cancel)).Should().BeEmpty();
        await Task.WhenAll(_runs).WaitAsync(Cancel);
        watch.Elapsed.Should().BeGreaterThan(TimeSpan.FromSeconds(4));
    }

    [Fact]
    public async Task AKnownToken_IsWelcomed_AndItsAgentAnswers()
    {
        var host = Host();
        var events = Events(host);
        var (_, _, status) = Runner(host, "t");
        (await events.Reader.ReadAsync(Cancel)).Should().Be("+Bot");
        (await NextStatus(status, _ => true)).Should().Be("connected as Bot");

        var agent = host.Agent("Bot")!;
        agent.Name.Should().Be("Bot");
        agent.IsAway.Should().BeFalse();
        agent.Restart();
        (await agent.AskAsync("""{"type":"x"}""", Limit)).Should().Be("""{"type":"x"}""");
    }

    [Fact]
    public async Task AskAsync_DiscardsLinesThatCameBefore()
    {
        var host = Host();
        var events = Events(host);
        Runner(host, "t");
        await events.Reader.ReadAsync(Cancel);
        var agent = host.Agent("Bot")!;
        agent.Restart();
        agent.Send("late");
        (await agent.ReadAsync(Limit)).Should().Be("late");
        agent.Send("later");
        await Task.Delay(500, Cancel); // let the echo of "later" come back

        (await agent.AskAsync("now", Limit)).Should().Be("now");
    }

    [Fact]
    public async Task Stop_EndsTheAgent_AndRestart_RunsAFreshOne()
    {
        var host = Host();
        var events = Events(host);
        Runner(host, "t");
        await events.Reader.ReadAsync(Cancel);
        var agent = host.Agent("Bot")!;
        agent.Restart();
        (await agent.AskAsync("one", Limit)).Should().Be("one");

        agent.Stop();
        agent.HasExited.Should().BeTrue();
        agent.Problem.Should().Be("stopped");
        (await agent.AskAsync("lost", Limit)).Should().BeNull();

        agent.Restart();
        agent.HasExited.Should().BeFalse();
        agent.Problem.Should().BeNull();
        (await agent.AskAsync("two", Limit)).Should().Be("two");
    }

    [Fact]
    public async Task TheRunnersExit_EndsTheAgent_WithItsProblem()
    {
        var host = Host();
        var pair = Serve(host);
        using var client = new SocketLines(pair.Client);
        var read = client.ReadAllAsync(Cancel).GetAsyncEnumerator();
        client.Send(Relay.Write(new RelayHello("t", Relay.Version)));
        (await read.MoveNextAsync()).Should().BeTrue();
        Relay.Read(read.Current).Should().Be(new RelayWelcome("Bot"));
        var agent = host.Agent("Bot")!;
        agent.Restart();
        (await read.MoveNextAsync()).Should().BeTrue();
        Relay.Read(read.Current).Should().Be(new RelayStart(1));

        var answer = agent.ReadAsync(Limit);
        client.Send(Relay.Write(new RelayLine("last words", 1)));
        client.Send(Relay.Write(new RelayExited("exited with code 3", 1)));

        (await answer).Should().Be("last words");
        (await agent.ReadAsync(Limit)).Should().BeNull();
        agent.HasExited.Should().BeTrue();
        agent.Problem.Should().Be("exited with code 3");
    }

    [Fact]
    public async Task AReadThatWaits_EndsWhenTheRunnerGoes()
    {
        var host = Host();
        var events = Events(host);
        var (_, pairs, _) = Runner(host, "t");
        await events.Reader.ReadAsync(Cancel);
        var agent = host.Agent("Bot")!;
        var watch = Stopwatch.StartNew();
        var read = agent.ReadAsync(TimeSpan.FromSeconds(20));
        (await pairs.Reader.ReadAsync(Cancel)).Server.Abort();

        (await read.WaitAsync(Cancel)).Should().BeNull();
        watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));
        (await events.Reader.ReadAsync(Cancel)).Should().Be("-Bot");
    }

    [Fact]
    public async Task WhileAway_AskAnswersNullAtOnce()
    {
        var host = Host();
        var events = Events(host);
        var (relay, _, _) = Runner(host, "t");
        await events.Reader.ReadAsync(Cancel);
        var agent = host.Agent("Bot")!;
        relay.Dispose();
        (await events.Reader.ReadAsync(Cancel)).Should().Be("-Bot");

        agent.IsAway.Should().BeTrue();
        var watch = Stopwatch.StartNew();
        (await agent.AskAsync("x", Limit)).Should().BeNull();
        (await agent.ReadAsync(Limit)).Should().BeNull();
        watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1));
        await Task.WhenAll(_runs).WaitAsync(Cancel);
    }

    [Fact]
    public async Task AReconnect_TakesTheSameAgentBack()
    {
        var host = Host();
        var events = Events(host);
        var (_, pairs, _) = Runner(host, "t");
        (await events.Reader.ReadAsync(Cancel)).Should().Be("+Bot");
        var agent = host.Agent("Bot")!;
        agent.Restart();
        (await agent.AskAsync("a", Limit)).Should().Be("a");

        (await pairs.Reader.ReadAsync(Cancel)).Server.Abort();
        (await events.Reader.ReadAsync(Cancel)).Should().Be("-Bot");
        agent.IsAway.Should().BeTrue();
        (await events.Reader.ReadAsync(Cancel)).Should().Be("+Bot", "the runner connects again by itself");

        host.Agent("Bot").Should().BeSameAs(agent);
        agent.IsAway.Should().BeFalse();
        (await agent.AskAsync("b", Limit)).Should().Be("b", "the runner kept the agent running");
    }

    [Fact]
    public async Task ARestartWhileAway_IsSentOnReconnect()
    {
        var host = Host();
        var events = Events(host);
        var pair = Serve(host);
        using (var first = new SocketLines(pair.Client))
        {
            var read = first.ReadAllAsync(Cancel).GetAsyncEnumerator();
            first.Send(Relay.Write(new RelayHello("t", Relay.Version)));
            await read.MoveNextAsync();
            await events.Reader.ReadAsync(Cancel);
            pair.Client.Abort();
            (await events.Reader.ReadAsync(Cancel)).Should().Be("-Bot");
        }

        host.Agent("Bot")!.Restart();
        var again = Serve(host);
        using var second = new SocketLines(again.Client);
        var lines = second.ReadAllAsync(Cancel).GetAsyncEnumerator();
        second.Send(Relay.Write(new RelayHello("t", Relay.Version)));
        (await lines.MoveNextAsync()).Should().BeTrue();
        Relay.Read(lines.Current).Should().Be(new RelayWelcome("Bot"));
        (await lines.MoveNextAsync()).Should().BeTrue();
        Relay.Read(lines.Current).Should().Be(new RelayStart(1));
    }

    [Fact]
    public async Task ASecondConnection_TakesOver_AndTheFirstIsToldWhy()
    {
        var host = Host();
        var events = Events(host);
        var (_, _, firstStatus) = Runner(host, "t");
        (await events.Reader.ReadAsync(Cancel)).Should().Be("+Bot");
        var agent = host.Agent("Bot")!;

        var (_, _, secondStatus) = Runner(host, "t");
        (await NextStatus(secondStatus, s => s.StartsWith("connected", StringComparison.Ordinal))).Should().Be("connected as Bot");
        (await NextStatus(firstStatus, s => s.StartsWith("refused", StringComparison.Ordinal))).Should().Be("refused: connected elsewhere");

        host.Agent("Bot").Should().BeSameAs(agent);
        agent.IsAway.Should().BeFalse();
        agent.Restart(); // the second runner has no agent of its own yet
        (await agent.AskAsync("x", Limit)).Should().Be("x");
        events.Reader.TryRead(out var more).Should().BeFalse($"the bot never went away, but saw {more}");
    }

    [Fact]
    public async Task TheConnectionTakenOver_IsRefused_AndItsLateLinesDoNotCount()
    {
        var host = Host();
        var events = Events(host);
        var firstPair = Serve(host);
        using var first = new SocketLines(firstPair.Client);
        var firstRead = first.ReadAllAsync(Cancel).GetAsyncEnumerator();
        first.Send(Relay.Write(new RelayHello("t", Relay.Version)));
        await firstRead.MoveNextAsync();
        await events.Reader.ReadAsync(Cancel);

        var secondPair = Serve(host);
        using var second = new SocketLines(secondPair.Client);
        var secondRead = second.ReadAllAsync(Cancel).GetAsyncEnumerator();
        second.Send(Relay.Write(new RelayHello("t", Relay.Version)));
        await secondRead.MoveNextAsync();
        (await firstRead.MoveNextAsync()).Should().BeTrue();
        Relay.Read(firstRead.Current).Should().Be(new RelayRefused("connected elsewhere"));

        var agent = host.Agent("Bot")!;
        agent.Restart(); // run 1, on the second connection
        await first.SendNowAsync(Relay.Write(new RelayLine("old", 1)));
        await first.SendNowAsync(Relay.Write(new RelayExited("old", 1)));
        (await agent.ReadAsync(TimeSpan.FromMilliseconds(500))).Should().BeNull();
        agent.HasExited.Should().BeFalse();
        second.Send(Relay.Write(new RelayLine("new", 1)));
        (await agent.ReadAsync(Limit)).Should().Be("new");
    }

    [Fact]
    public async Task MessagesOverAConnectionThatWasTakenOver_AreIgnored()
    {
        using var firstPair = SocketPair.Create();
        using var secondPair = SocketPair.Create();
        using var first = new SocketLines(firstPair.Server);
        using var second = new SocketLines(secondPair.Server);
        var agent = new RemoteAgent("Bot");
        agent.Restart(); // run 1, sent when a connection attaches
        agent.Attach(first).Should().BeNull();
        agent.Attach(second).Should().BeSameAs(first);

        agent.Receive(first, new RelayLine("old", 1));
        agent.Receive(first, new RelayExited("old", 1));
        agent.Detach(first).Should().BeFalse();
        agent.IsAway.Should().BeFalse();
        agent.HasExited.Should().BeFalse();
        agent.Receive(second, new RelayLine("new", 1));
        (await agent.ReadAsync(Limit)).Should().Be("new");
    }

    [Fact]
    public async Task AFloodingRunner_IsClosed_AndAnotherBotStillAnswers()
    {
        var host = new RelayHost(token => token switch { "a" => "A", "b" => "B", _ => null }) { MaxPerSecond = 20 };
        var events = Events(host);
        Runner(host, "b");
        (await events.Reader.ReadAsync(Cancel)).Should().Be("+B");

        using var pair = SocketPair.Create();
        var run = host.RunAsync(pair.Server, Cancel);
        using var flood = new SocketLines(pair.Client);
        var floodRead = flood.ReadAllAsync(Cancel).GetAsyncEnumerator();
        flood.Send(Relay.Write(new RelayHello("a", Relay.Version)));
        await floodRead.MoveNextAsync();
        (await events.Reader.ReadAsync(Cancel)).Should().Be("+A");
        try
        {
            for (var i = 0; i < 100 && pair.Client.State == System.Net.WebSockets.WebSocketState.Open; i++)
            {
                await flood.SendNowAsync(Relay.Write(new RelayLine($"{i}")));
            }
        }
        catch (System.Net.WebSockets.WebSocketException)
        {
            // Closed by the host: what the test waits for.
        }

        (await events.Reader.ReadAsync(Cancel)).Should().Be("-A");
        (await run).Should().Contain("too many", "the run says why it closed the connection, for the server's log");
        host.Agent("A")!.IsAway.Should().BeTrue();
        var b = host.Agent("B")!;
        b.Restart();
        (await b.AskAsync("still here", Limit)).Should().Be("still here");
    }

    [Fact]
    public async Task LinesBeforeTheFirstStart_AreIgnored()
    {
        using var pair = SocketPair.Create();
        using var lines = new SocketLines(pair.Server);
        var agent = new RemoteAgent("Bot");
        agent.Attach(lines);

        agent.Receive(lines, new RelayLine("unasked", 0));

        (await agent.ReadAsync(TimeSpan.FromMilliseconds(200))).Should().BeNull("no run has started, so no line belongs to one");
    }

    [Fact]
    public async Task TheLinesThatWait_AreForgotten_WhenTheRunnerGoes()
    {
        using var firstPair = SocketPair.Create();
        using var secondPair = SocketPair.Create();
        using var first = new SocketLines(firstPair.Server);
        using var second = new SocketLines(secondPair.Server);
        var agent = new RemoteAgent("Bot");
        agent.Restart();
        agent.Attach(first);
        agent.Receive(first, new RelayLine("unread", 1));

        agent.Detach(first).Should().BeTrue();
        agent.Attach(second);

        (await agent.ReadAsync(TimeSpan.FromMilliseconds(200))).Should().BeNull("an away bot keeps nothing that nobody reads");
    }

    [Fact]
    public async Task TheLinesThatWait_AreBoundedInLength_TheOldestDroppedFirst()
    {
        using var pair = SocketPair.Create();
        using var lines = new SocketLines(pair.Server);
        var agent = new RemoteAgent("Bot");
        agent.Restart();
        agent.Attach(lines);
        var big = new string('x', 60_000);
        for (var i = 0; i < 100; i++)
        {
            agent.Receive(lines, new RelayLine($"{i}:{big}", 1));
        }

        var kept = new List<string>();
        while (await agent.ReadAsync(TimeSpan.Zero) is { } line)
        {
            kept.Add(line);
        }

        kept.Sum(l => l.Length).Should().BeLessThanOrEqualTo(RemoteAgent.MaxWaiting);
        kept.Should().NotBeEmpty().And.Subject.Last().Should().StartWith("99:", "the newest lines are kept");
    }

    [Fact]
    public async Task Drop_ClosesTheBotsConnection_AndTellsTheRunnerWhy()
    {
        var host = Host();
        var events = Events(host);
        var (_, _, status) = Runner(host, "t");
        (await events.Reader.ReadAsync(Cancel)).Should().Be("+Bot");
        var agent = host.Agent("Bot")!;

        host.Drop("Bot", "token replaced").Should().BeTrue();

        agent.IsAway.Should().BeTrue("the bot is away at once");
        (await events.Reader.ReadAsync(Cancel)).Should().Be("-Bot");
        (await NextStatus(status, s => s.StartsWith("refused", StringComparison.Ordinal))).Should().Be("refused: token replaced");
        await Task.WhenAll(_runs).WaitAsync(Cancel);
        host.Drop("Bot", "token replaced").Should().BeFalse("the bot is away already");
        host.Drop("Nobody", "token replaced").Should().BeFalse();
    }

    [Fact]
    public async Task ABrokenSocket_EndsTheRun_WithoutThrowing()
    {
        var host = Host();
        var events = Events(host);
        var pair = Serve(host);
        using var client = new SocketLines(pair.Client);
        var read = client.ReadAllAsync(Cancel).GetAsyncEnumerator();
        client.Send(Relay.Write(new RelayHello("t", Relay.Version)));
        await read.MoveNextAsync();
        await events.Reader.ReadAsync(Cancel);
        pair.Client.Dispose();

        await Task.WhenAll(_runs).WaitAsync(Cancel);
        (await events.Reader.ReadAsync(Cancel)).Should().Be("-Bot");
    }

    /// <summary>A runner played by the test: connects, says hello for <paramref name="token"/> and reads the welcome.</summary>
    private async Task<(SocketPair Pair, SocketLines Lines, IAsyncEnumerator<string> Read)> RawRunner(RelayHost host, string token)
    {
        var pair = Serve(host);
        var lines = new SocketLines(pair.Client);
        _owned.Add(lines);
        var read = lines.ReadAllAsync(Cancel).GetAsyncEnumerator(Cancel);
        lines.Send(Relay.Write(new RelayHello(token, Relay.Version)));
        (await NextMessage(read)).Should().BeOfType<RelayWelcome>();
        return (pair, lines, read);
    }

    private static async Task<RelayMessage?> NextMessage(IAsyncEnumerator<string> read)
    {
        (await read.MoveNextAsync()).Should().BeTrue("a message was expected before the connection closed");
        return Relay.Read(read.Current);
    }

    [Fact]
    public async Task ARestartIntoAHalfOpenConnection_IsSentAgain_WhenTheRunnerTakesTheSeatBack()
    {
        var host = Host();
        await RawRunner(host, "t"); // a connection that broke without the server noticing: it reads nothing more
        var agent = host.Agent("Bot")!;
        agent.Restart();
        agent.IsAway.Should().BeFalse();

        var (_, _, read) = await RawRunner(host, "t");

        (await NextMessage(read)).Should().Be(new RelayStart(1), "nothing came back since the start, so it may never have arrived");
    }

    [Fact]
    public async Task ATakeover_DoesNotRestartAnAgentThatAnswered()
    {
        var host = Host();
        var (_, first, firstRead) = await RawRunner(host, "t");
        var agent = host.Agent("Bot")!;
        agent.Restart();
        (await NextMessage(firstRead)).Should().Be(new RelayStart(1));
        first.Send(Relay.Write(new RelayLine("hi", 1)));
        (await agent.ReadAsync(Limit)).Should().Be("hi");

        var (_, _, read) = await RawRunner(host, "t");
        agent.Status(JsonSerializer.SerializeToElement(7));

        (await NextMessage(read)).Should().BeOfType<RelayStatus>("the runner kept its agent, which had answered");
    }

    [Fact]
    public async Task AStaleExit_AfterARestart_DoesNotEndTheNewRun()
    {
        var host = Host();
        var (_, client, read) = await RawRunner(host, "t");
        var agent = host.Agent("Bot")!;
        agent.Restart();
        (await NextMessage(read)).Should().Be(new RelayStart(1));
        agent.Restart();
        (await NextMessage(read)).Should().Be(new RelayStart(2));

        client.Send(Relay.Write(new RelayExited("exited with code 3", 1)));
        client.Send(Relay.Write(new RelayLine("old", 1)));
        client.Send(Relay.Write(new RelayLine("unnumbered")));
        client.Send(Relay.Write(new RelayLine("new", 2)));

        (await agent.ReadAsync(Limit)).Should().Be("new");
        agent.HasExited.Should().BeFalse();
        agent.Problem.Should().BeNull();
    }

    [Fact]
    public async Task ARunnerThatStopsReading_AndCloses_IsAwayAtOnce()
    {
        var host = Host();
        var events = Events(host);
        var (pair, _, _) = await RawRunner(host, "t");
        (await events.Reader.ReadAsync(Cancel)).Should().Be("+Bot");
        var agent = host.Agent("Bot")!;
        agent.Restart();
        var big = new string('x', 60_000);
        for (var i = 0; i < 20; i++)
        {
            agent.Send(big); // more than the connection holds: the host's sending gets stuck
        }

        await Task.Delay(300, Cancel);
        var watch = Stopwatch.StartNew();
        await pair.Client.CloseOutputAsync(System.Net.WebSockets.WebSocketCloseStatus.NormalClosure, "bye", Cancel);

        (await events.Reader.ReadAsync(Cancel)).Should().Be("-Bot");
        watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2), "the bot is away before the closing is done");
        agent.IsAway.Should().BeTrue();
    }

    [Fact]
    public async Task FastReconnects_RaiseConnectedAndDisconnected_InTurn()
    {
        var host = Host();
        var events = new ConcurrentQueue<string>();
        host.Connected += _ => events.Enqueue("+");
        host.Disconnected += _ => events.Enqueue("-");
        var aborts = new List<Task>();
        for (var i = 0; i < 40; i++)
        {
            var pair = Serve(host);
            var lines = new SocketLines(pair.Client);
            _owned.Add(lines);
            lines.Send(Relay.Write(new RelayHello("t", Relay.Version)));
            var wait = i % 4;
            aborts.Add(Task.Run(async () =>
            {
                await Task.Delay(wait, Cancel);
                pair.Client.Abort();
            }, Cancel));
        }

        await Task.WhenAll(aborts);
        await Task.WhenAll(_runs).WaitAsync(Cancel);

        var seen = string.Concat(events);
        seen.Should().MatchRegex("^(\\+-)*$", "each connect is followed by its disconnect");
        if (host.Agent("Bot") is { } agent)
        {
            agent.IsAway.Should().BeTrue();
        }
    }
}
