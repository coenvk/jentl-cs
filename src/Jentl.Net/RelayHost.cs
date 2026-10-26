using System.Collections.Concurrent;
using System.Net.WebSockets;

namespace Jentl.Net;

/// <summary>
/// The game server's side of online bots: takes the runners' WebSockets (<see cref="AgentRelay"/> on the owners'
/// computers), checks each hello's token, and keeps one <see cref="RemoteAgent"/> per bot that the game asks as it would
/// a local agent. <paramref name="nameOfToken"/> gives the bot's name for a token, or null for an unknown one.
/// </summary>
/// <remarks>
/// A bot has one connection at a time: a second one for the same bot takes over (a runner that reconnected before the
/// server noticed its old connection broke, or the same bot started elsewhere), and the first is told "connected
/// elsewhere" and closed. The agent stays the same instance throughout, so a match in progress keeps its seat. A runner
/// that breaks the limits (<see cref="MaxLength"/>, <see cref="MaxPerSecond"/>) loses only its own connection.
/// </remarks>
public sealed class RelayHost(Func<string, string?> nameOfToken)
{
    /// <summary>How long a new connection may take to say hello.</summary>
    private static readonly TimeSpan HelloWait = TimeSpan.FromSeconds(5);

    private readonly Func<string, string?> _nameOfToken = nameOfToken ?? throw new ArgumentNullException(nameof(nameOfToken));
    private readonly ConcurrentDictionary<string, Seat> _seats = new(StringComparer.Ordinal);

    // Each connection's way to end its own run, for when another connection takes over.
    private readonly ConcurrentDictionary<SocketLines, CancellationTokenSource> _runs = new();

    /// <summary>
    /// Raised with the bot's name when the bot comes back from away: a runner connected for it. For each bot,
    /// <see cref="Connected"/> and <see cref="Disconnected"/> are raised one at a time and in turn, and each says what
    /// <see cref="RemoteAgent.IsAway"/> was when it was raised; after the last one, it still says so.
    /// </summary>
    public event Action<string>? Connected;

    /// <summary>Raised with the bot's name when the bot is away: its runner's connection ended and no other took over. See <see cref="Connected"/>.</summary>
    public event Action<string>? Disconnected;

    /// <summary>The longest message a runner may send, in bytes; a longer one closes its connection.</summary>
    public int MaxLength { get; init; } = 65536;

    /// <summary>The most messages a runner may send in one second; more close its connection.</summary>
    public int MaxPerSecond { get; init; } = 200;

    /// <summary>The bot's agent, or null when no runner has connected for it yet.</summary>
    public RemoteAgent? Agent(string name) => _seats.TryGetValue(name, out var seat) ? seat.Agent : null;

    /// <summary>
    /// Serves one runner's connection until it closes: reads the hello (within 5 seconds), refuses an unknown token or
    /// another protocol version, else welcomes the runner, attaches the connection to the bot's agent (made on first
    /// use) and passes the runner's messages to it. The bot is away as soon as the connection ends, before the closing
    /// is done. Never throws for a socket that misbehaves or breaks; returns quietly when <paramref name="aborted"/> is
    /// cancelled. The socket belongs to the caller.
    /// </summary>
    public async Task RunAsync(WebSocket socket, CancellationToken aborted)
    {
        ArgumentNullException.ThrowIfNull(socket);
        using var lines = new SocketLines(socket, MaxLength, MaxPerSecond);
        using var ended = CancellationTokenSource.CreateLinkedTokenSource(aborted);
        ended.CancelAfter(HelloWait);
        Seat? seat = null;
        try
        {
            await using var messages = lines.ReadAllAsync(ended.Token).GetAsyncEnumerator(ended.Token);
            bool any;
            try
            {
                any = await messages.MoveNextAsync().ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!aborted.IsCancellationRequested)
            {
                await lines.CloseAsync("no hello in time").ConfigureAwait(false);
                return;
            }

            if (!any)
            {
                return;
            }

            ended.CancelAfter(Timeout.InfiniteTimeSpan);
            var hello = Relay.Read(messages.Current) as RelayHello;
            var name = hello?.Version == Relay.Version ? _nameOfToken(hello.Token) : null;
            if (name is null)
            {
                var refusal = hello is null ? "expected a hello"
                    : hello.Version != Relay.Version ? $"protocol version {hello.Version} is not supported (the server speaks {Relay.Version})"
                    : "unknown token";
                lines.Send(Relay.Write(new RelayRefused(refusal)));
                await lines.CloseAsync("refused").ConfigureAwait(false);
                return;
            }

            seat = _seats.GetOrAdd(name, n => new Seat(new RemoteAgent(n)));
            _runs[lines] = ended;
            lines.Send(Relay.Write(new RelayWelcome(name)));
            if (seat.Agent.Attach(lines) is { } previous)
            {
                _ = TakeOverAsync(previous);
            }

            Report(seat);
            while (await messages.MoveNextAsync().ConfigureAwait(false))
            {
                if (Relay.Read(messages.Current) is { } message)
                {
                    seat.Agent.Receive(lines, message);
                }
            }

            Leave(seat, lines); // away now: closing can take seconds when the runner stopped reading
            await lines.CloseAsync("bye").ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Cancelled, taken over, or no hello in time: the run is over.
        }
        finally
        {
            _runs.TryRemove(lines, out _);
            if (seat is not null)
            {
                Leave(seat, lines);
            }
        }
    }

    /// <summary>Detaches <paramref name="lines"/> from the seat's agent, and says so when the bot is away now.</summary>
    private void Leave(Seat seat, SocketLines lines)
    {
        if (seat.Agent.Detach(lines))
        {
            Report(seat);
        }
    }

    /// <summary>
    /// Raises <see cref="Connected"/> or <see cref="Disconnected"/> when whether the bot is away differs from what the
    /// last event said. Called after every attach and detach, one at a time per bot, so the events come in turn and the
    /// last one matches the bot, however attaching and detaching on other tasks interleave.
    /// </summary>
    private void Report(Seat seat)
    {
        lock (seat.Gate)
        {
            var present = !seat.Agent.IsAway;
            if (present == seat.Present)
            {
                return;
            }

            seat.Present = present;
            (present ? Connected : Disconnected)?.Invoke(seat.Agent.Name);
        }
    }

    /// <summary>Tells the connection that was taken over why, closes it, and ends its run.</summary>
    private async Task TakeOverAsync(SocketLines previous)
    {
        previous.Send(Relay.Write(new RelayRefused("connected elsewhere")));
        await previous.CloseAsync("connected elsewhere").ConfigureAwait(false);
        if (_runs.TryRemove(previous, out var run))
        {
            try
            {
                await run.CancelAsync().ConfigureAwait(false);
            }
            catch (ObjectDisposedException)
            {
                // Its run ended meanwhile.
            }
        }
    }

    /// <summary>A bot's agent, and what the last <see cref="Connected"/> or <see cref="Disconnected"/> said of it.</summary>
    private sealed class Seat(RemoteAgent agent)
    {
        public RemoteAgent Agent { get; } = agent;

        public Lock Gate { get; } = new();

        public bool Present { get; set; }
    }
}
