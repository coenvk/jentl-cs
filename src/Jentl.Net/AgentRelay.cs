using System.Net.WebSockets;
using System.Text;

namespace Jentl.Net;

/// <summary>
/// The runner's side of an online bot: connects to the game server, says hello with the bot's token, and then does
/// what the server asks: starts the agent process, passes lines to it, sends its lines back (in the order it wrote
/// them) and its exit, and stops it. A dropped connection is tried again after 1, 2, 4 … 30 seconds, and the agent keeps
/// running meanwhile; only the server's <see cref="RelayRefused"/> (or cancelling) ends the run.
/// </summary>
/// <remarks>
/// <para>
/// Lines the agent writes while the connection is down wait in its own queue (see <see cref="AgentProcess.MaxLines"/>)
/// and go once the server has welcomed the runner again. A line that was on its way when the connection broke is lost.
/// The server's messages are not limited in rate (only in length): it sets the pace of the game, and a match between
/// fast bots sends hundreds of lines a second. The runner keeps to the server's limits itself, so that a bot that prints
/// more than it is asked (debug output, say) loses those lines rather than its connection: a line longer than
/// <see cref="MaxLength"/> is dropped, and so are lines beyond <see cref="ExtraPerSecond"/> a second besides the answers
/// (each message from the server allows one answer, as <see cref="SocketLines"/> counts them). The owner is told once per
/// connection through <see cref="Status"/>.
/// </para>
/// <para>
/// A start for the run the agent already has (the server sends it again on a new connection when it heard nothing since,
/// as from an agent still starting up) keeps that agent running rather than starting it over.
/// </para>
/// </remarks>
public sealed class AgentRelay(Func<CancellationToken, Task<WebSocket>> connect, string token, AgentCommand command) : IDisposable
{
    private static readonly TimeSpan FirstWait = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan LongestWait = TimeSpan.FromSeconds(30);

    // How long one read of the agent waits; it only bounds how often the loop looks again.
    private static readonly TimeSpan ReadLimit = TimeSpan.FromMinutes(1);

    // How often a line that is waiting for the connection looks whether it is back.
    private static readonly TimeSpan ConnectionPoll = TimeSpan.FromMilliseconds(100);

    private readonly Func<CancellationToken, Task<WebSocket>> _connect = connect ?? throw new ArgumentNullException(nameof(connect));
    private readonly string _token = token ?? throw new ArgumentNullException(nameof(token));
    private readonly AgentCommand _command = command ?? throw new ArgumentNullException(nameof(command));
    private readonly CancellationTokenSource _disposed = new();
    private readonly Lock _lock = new();
    private AgentProcess? _agent; // the one agent whose lines go to the server
    private int _agentRun; // the run that started it
    private SocketLines? _lines; // the connection, once the server has welcomed the runner
    private Pace? _pace; // what the server lets that connection send

    /// <summary>
    /// What the owner should see, as plain text: "connected as Bot", "refused: unknown token", "reconnecting in 4 s",
    /// "dropped a line from the bot: …", and the JSON of each <see cref="RelayStatus"/> the server sends. Raised on the
    /// thread that runs the relay, or (for a dropped line) on the one that reads the agent.
    /// </summary>
    public event Action<string>? Status;

    /// <summary>
    /// The longest message the server takes, in bytes (its <see cref="RelayHost.MaxLength"/>, 64 KiB by default): a line
    /// of the agent's that would make a longer message is dropped here, since the server would close the connection for it.
    /// </summary>
    public int MaxLength { get; init; } = 65536;

    /// <summary>
    /// How many messages a second the runner sends besides its answers, in bursts of as many; the agent's lines beyond
    /// that are dropped. Half the server's <see cref="RelayHost.MaxPerSecond"/> by default, for room to spare.
    /// </summary>
    public int ExtraPerSecond { get; init; } = 100;

    /// <summary>
    /// Relays until the server refuses the runner (or <paramref name="cancel"/> or <see cref="Dispose"/> ends it), then
    /// stops the agent. Never throws for a network problem: it connects again. Returns quietly when cancelled.
    /// </summary>
    public async Task RunAsync(CancellationToken cancel)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancel, _disposed.Token);
        var stop = linked.Token;
        var wait = FirstWait;
        try
        {
            while (!stop.IsCancellationRequested)
            {
                var (refused, welcomed) = await ConnectOnceAsync(stop).ConfigureAwait(false);
                if (refused is not null)
                {
                    Status?.Invoke($"refused: {refused}");
                    return;
                }

                if (stop.IsCancellationRequested)
                {
                    return;
                }

                wait = welcomed ? FirstWait : wait;
                Status?.Invoke($"reconnecting in {wait.TotalSeconds:0} s");
                await Task.Delay(wait, stop).ConfigureAwait(false);
                wait = wait * 2 > LongestWait ? LongestWait : wait * 2;
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            // Cancelled: the run is over.
        }
        finally
        {
            StopAgent(wait: true);
        }
    }

    /// <summary>Ends the run and stops the agent.</summary>
    public void Dispose()
    {
        try
        {
            _disposed.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        StopAgent(wait: true);
    }

    /// <summary>One connection, from connecting to its end: the reason when the server refused, and whether it welcomed us.</summary>
    private async Task<(string? Refused, bool Welcomed)> ConnectOnceAsync(CancellationToken stop)
    {
        WebSocket socket;
        try
        {
            socket = await _connect(stop).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // Any failure to connect is tried again later.
        catch (Exception)
#pragma warning restore CA1031
        {
            return (null, false);
        }

        var welcomed = false;
        var pace = new Pace(ExtraPerSecond);
        using (socket)
        using (var lines = new SocketLines(socket, maxPerSecond: int.MaxValue)) // the server sets the pace: a fast match sends hundreds of lines a second
        {
            pace.Take(); // the hello is no answer
            lines.Send(Relay.Write(new RelayHello(_token, Relay.Version)));
            try
            {
                await foreach (var text in lines.ReadAllAsync(stop).ConfigureAwait(false))
                {
                    lock (_lock)
                    {
                        pace.Received();
                    }

                    switch (Relay.Read(text))
                    {
                        case RelayWelcome welcome:
                            welcomed = true;
                            lock (_lock)
                            {
                                (_lines, _pace) = (lines, pace);
                            }

                            Status?.Invoke($"connected as {welcome.Name}");
                            break;
                        case RelayRefused refused:
                            return (refused.Reason, welcomed);
                        case RelayStart start:
                            StartAgent(start.Run);
                            break;
                        case RelayLine line:
                            lock (_lock)
                            {
                                _agent?.Send(line.Text);
                            }

                            break;
                        case RelayStop:
                            StopAgent(wait: false);
                            break;
                        case RelayStatus status:
                            Status?.Invoke(status.Status.GetRawText());
                            break;
                    }
                }
            }
            finally
            {
                lock (_lock)
                {
                    if (_lines == lines)
                    {
                        (_lines, _pace) = (null, null);
                    }
                }
            }
        }

        return (null, welcomed);
    }

    /// <summary>Starts a fresh agent for <paramref name="run"/>, stopping the one before; the agent of that run when it still runs is kept.</summary>
    private void StartAgent(int run)
    {
        lock (_lock)
        {
            // The server sent the start again, as it heard nothing since: starting over could miss the hello's time.
            if (_agent is { HasExited: false } && _agentRun == run)
            {
                return;
            }
        }

        StopAgent(wait: false);
        var agent = AgentProcess.Start(_command);
        lock (_lock)
        {
            (_agent, _agentRun) = (agent, run);
        }

        _ = Task.Run(() => PumpAsync(agent, run));
    }

    /// <summary>Stops the agent, if one runs; it sends nothing more, not even its exit. Waits for it to end only when asked.</summary>
    private void StopAgent(bool wait)
    {
        AgentProcess? agent;
        lock (_lock)
        {
            (agent, _agent) = (_agent, null);
        }

        if (agent is null)
        {
            return;
        }

        if (wait)
        {
            agent.Dispose();
        }
        else
        {
            _ = Task.Run(agent.Dispose); // killing it can take a moment; the server's messages should not wait for that
        }
    }

    /// <summary>Sends the agent's lines to the server as it writes them, then its exit, each with the run that started it, for as long as it is the current agent.</summary>
    private async Task PumpAsync(AgentProcess agent, int run)
    {
        while (true)
        {
            if (await agent.ReadAsync(ReadLimit).ConfigureAwait(false) is { } line)
            {
                if (!await SendAsync(agent, new RelayLine(line, run), mayDrop: true).ConfigureAwait(false))
                {
                    return;
                }
            }
            else if (agent.HasExited)
            {
                await SendAsync(agent, new RelayExited(agent.Problem, run), mayDrop: false).ConfigureAwait(false);
                return;
            }
        }
    }

    /// <summary>
    /// Sends a message from <paramref name="agent"/> once there is a connection, or drops it there when it may be dropped
    /// and the server would not take it (too long, or too many); false when it is no longer the current agent.
    /// </summary>
    private async Task<bool> SendAsync(AgentProcess agent, RelayMessage message, bool mayDrop)
    {
        var text = Relay.Write(message);
        var tooLong = Encoding.UTF8.GetByteCount(text) > MaxLength;
        while (true)
        {
            string? said = null;
            var done = false;
            lock (_lock)
            {
                if (_agent != agent)
                {
                    return false;
                }

                if (_lines is { } lines && _pace is { } pace)
                {
                    done = true;
                    if (mayDrop && tooLong)
                    {
                        said = pace.FirstTooLong() ? $"dropped a line from the bot: longer than {MaxLength} bytes" : null;
                    }
                    else if (pace.Take() || !mayDrop)
                    {
                        lines.Send(text);
                    }
                    else
                    {
                        said = pace.FirstTooMany() ? $"dropped lines from the bot: more than {ExtraPerSecond} a second besides its answers" : null;
                    }
                }
            }

            if (said is not null)
            {
                Status?.Invoke(said);
            }

            if (done)
            {
                return true;
            }

            await Task.Delay(ConnectionPoll).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// What the server lets one connection send, counted as the server's <see cref="SocketLines"/> counts it: every message
    /// from the server allows one answer, and besides those <paramref name="perSecond"/> messages a second, in bursts of as
    /// many. Used under the relay's lock.
    /// </summary>
    private sealed class Pace(int perSecond)
    {
        private int _answers;
        private double _allowance = perSecond;
        private long _last = Environment.TickCount64;
        private bool _toldTooLong;
        private bool _toldTooMany;

        /// <summary>A message came from the server: one more answer is allowed.</summary>
        public void Received() => _answers = Math.Min(_answers + 1, SocketLines.MaxQueued);

        /// <summary>Counts a message to send, as an answer while one is left, else from the allowance; false when neither is.</summary>
        public bool Take()
        {
            if (_answers > 0)
            {
                _answers--;
                return true;
            }

            var now = Environment.TickCount64;
            _allowance = Math.Min(perSecond, _allowance + ((now - _last) * (double)perSecond / 1000));
            _last = now;
            if (_allowance < 1)
            {
                return false;
            }

            _allowance--;
            return true;
        }

        /// <summary>True the first time a line is dropped for its length on this connection: the owner is told once.</summary>
        public bool FirstTooLong() => !_toldTooLong && (_toldTooLong = true);

        /// <summary>True the first time a line is dropped for too many lines on this connection: the owner is told once.</summary>
        public bool FirstTooMany() => !_toldTooMany && (_toldTooMany = true);
    }
}
