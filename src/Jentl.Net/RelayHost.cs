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
    private readonly ConcurrentDictionary<string, RemoteAgent> _agents = new(StringComparer.Ordinal);

    // Each connection's way to end its own run, for when another connection takes over.
    private readonly ConcurrentDictionary<SocketLines, CancellationTokenSource> _runs = new();

    /// <summary>Raised with the bot's name when its runner connects while the bot was away.</summary>
    public event Action<string>? Connected;

    /// <summary>Raised with the bot's name when its runner's connection ends and no other took over: the bot is away.</summary>
    public event Action<string>? Disconnected;

    /// <summary>The longest message a runner may send, in bytes; a longer one closes its connection.</summary>
    public int MaxLength { get; init; } = 65536;

    /// <summary>The most messages a runner may send in one second; more close its connection.</summary>
    public int MaxPerSecond { get; init; } = 200;

    /// <summary>The bot's agent, or null when no runner has connected for it yet.</summary>
    public RemoteAgent? Agent(string name) => _agents.TryGetValue(name, out var agent) ? agent : null;

    /// <summary>
    /// Serves one runner's connection until it closes: reads the hello (within 5 seconds), refuses an unknown token or
    /// another protocol version, else welcomes the runner, attaches the connection to the bot's agent (made on first
    /// use) and passes the runner's messages to it. Never throws for a socket that misbehaves or breaks; returns quietly
    /// when <paramref name="aborted"/> is cancelled. The socket belongs to the caller.
    /// </summary>
    public async Task RunAsync(WebSocket socket, CancellationToken aborted)
    {
        ArgumentNullException.ThrowIfNull(socket);
        using var lines = new SocketLines(socket, MaxLength, MaxPerSecond);
        using var ended = CancellationTokenSource.CreateLinkedTokenSource(aborted);
        ended.CancelAfter(HelloWait);
        RemoteAgent? agent = null;
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

            agent = _agents.GetOrAdd(name, n => new RemoteAgent(n));
            _runs[lines] = ended;
            lines.Send(Relay.Write(new RelayWelcome(name)));
            if (agent.Attach(lines) is { } previous)
            {
                _ = TakeOverAsync(previous);
            }
            else
            {
                Connected?.Invoke(name);
            }

            while (await messages.MoveNextAsync().ConfigureAwait(false))
            {
                if (Relay.Read(messages.Current) is { } message)
                {
                    agent.Receive(lines, message);
                }
            }

            await lines.CloseAsync("bye").ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Cancelled, taken over, or no hello in time: the run is over.
        }
        finally
        {
            _runs.TryRemove(lines, out _);
            if (agent is not null && agent.Detach(lines))
            {
                Disconnected?.Invoke(agent.Name);
            }
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
}
