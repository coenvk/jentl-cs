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
/// that breaks the limits (<see cref="MaxLength"/>, <see cref="MaxPerSecond"/>) loses only its own connection, and
/// <see cref="Drop"/> closes a bot's connection when its token no longer holds.
/// <para>
/// The run numbers of the agents' starts (<see cref="RelayStart"/>) begin at a random number for each host, so after a
/// server restart a runner's process from the old server's match never passes for one of the new server's: the first
/// start gets a fresh process, and the old process's late lines are ignored.
/// </para>
/// </remarks>
public sealed class RelayHost(Func<string, string?> nameOfToken)
{
    /// <summary>How long a new connection may take to say hello.</summary>
    private static readonly TimeSpan HelloWait = TimeSpan.FromSeconds(5);

    private readonly Func<string, string?> _nameOfToken = nameOfToken ?? throw new ArgumentNullException(nameof(nameOfToken));
    private readonly ConcurrentDictionary<string, Seat> _seats = new(StringComparer.Ordinal);

    // The run before every agent's first start: random for each host, with room for a billion starts before it wraps.
    private readonly int _runBefore = Random.Shared.Next(1 << 30);

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

    /// <summary>
    /// The most messages a runner may send in one second besides its answers; more close its connection. Every line the
    /// server sends lets the runner send one more, so a fast bot that answers each line is never cut off (see
    /// <see cref="SocketLines"/>).
    /// </summary>
    public int MaxPerSecond { get; init; } = 200;

    /// <summary>
    /// Closes the bot's connection, if it has one: the runner is refused with <paramref name="reason"/>, so it does not
    /// connect again by itself, and the bot is away at once. For a bot whose token was replaced or that was banned, as the
    /// token is checked only at hello: a runner still connected with the old token would otherwise play on. True when
    /// there was a connection to close.
    /// </summary>
    public bool Drop(string name, string reason)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(reason);
        if (!_seats.TryGetValue(name, out var seat))
        {
            return false;
        }

        // One step with joining (see Join): no hello checked before the token changed can attach after this.
        SocketLines? lines;
        lock (seat.Joining)
        {
            lines = seat.Agent.Connection;
            if (lines is null || !seat.Agent.Detach(lines))
            {
                return false;
            }
        }

        Report(seat);
        _ = EndAsync(lines, reason);
        return true;
    }

    /// <summary>The bot's agent, or null when no runner has connected for it yet.</summary>
    public RemoteAgent? Agent(string name) => _seats.TryGetValue(name, out var seat) ? seat.Agent : null;

    /// <summary>
    /// Serves one runner's connection until it closes: reads the hello (within 5 seconds), refuses an unknown token or
    /// another protocol version, else welcomes the runner, attaches the connection to the bot's agent (made on first
    /// use) and passes the runner's messages to it. The token is looked up again just before the attach, in one step
    /// with it that a <see cref="Drop"/> cannot come between: a token replaced meanwhile is refused with "token replaced",
    /// and never takes the seat. The bot is away as soon as the connection ends, before the closing is done. Never throws for a socket that misbehaves or breaks; returns quietly when <paramref name="aborted"/> is
    /// cancelled. The socket belongs to the caller. Answers the limit the runner broke when that closed the connection
    /// (<see cref="SocketLines.Problem"/>, for the server's log), else null.
    /// </summary>
    public async Task<string?> RunAsync(WebSocket socket, CancellationToken aborted)
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
                return null;
            }

            if (!any)
            {
                return lines.Problem;
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
                return null;
            }

            seat = _seats.GetOrAdd(name, n => new Seat(new RemoteAgent(n, _runBefore)));
            _runs[lines] = ended;
            lines.Send(Relay.Write(new RelayWelcome(name)));
            if (!Join(seat, lines, hello!.Token))
            {
                lines.Send(Relay.Write(new RelayRefused("token replaced")));
                await lines.CloseAsync("token replaced").ConfigureAwait(false);
                return null;
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

        return lines.Problem;
    }

    /// <summary>Detaches <paramref name="lines"/> from the seat's agent, and says so when the bot is away now.</summary>
    private void Leave(Seat seat, SocketLines lines)
    {
        lock (seat.Joining)
        {
            if (!seat.Agent.Detach(lines))
            {
                return;
            }
        }

        Report(seat);
    }

    /// <summary>
    /// Attaches <paramref name="lines"/> to the seat's agent, taking over from the connection before, when
    /// <paramref name="token"/> still names the bot; false, and nothing attached, when it no longer does. A hello checked
    /// just before its token was replaced would otherwise attach just after the <see cref="Drop"/> that came with it, and
    /// stay. Checking and attaching are one step for the seat, as are <see cref="Drop"/> and <see cref="Leave"/>: a drop
    /// comes either before the check, which then fails, or after the attach, and then closes this connection.
    /// </summary>
    private bool Join(Seat seat, SocketLines lines, string token)
    {
        SocketLines? previous;
        lock (seat.Joining)
        {
            if (_nameOfToken(token) != seat.Agent.Name)
            {
                return false;
            }

            previous = seat.Agent.Attach(lines);
        }

        if (previous is not null)
        {
            _ = EndAsync(previous, "connected elsewhere");
        }

        return true;
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

    /// <summary>Tells a connection that was taken over or dropped why, closes it, and ends its run.</summary>
    private async Task EndAsync(SocketLines lines, string reason)
    {
        lines.Send(Relay.Write(new RelayRefused(reason)));
        await lines.CloseAsync(reason).ConfigureAwait(false);
        if (_runs.TryRemove(lines, out var run))
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

        /// <summary>Held while a connection joins the seat, leaves it or is dropped: see <see cref="Join"/>.</summary>
        public Lock Joining { get; } = new();

        public bool Present { get; set; }
    }
}
