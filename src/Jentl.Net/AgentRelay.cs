using System.Net.WebSockets;

namespace Jentl.Net;

/// <summary>
/// The runner's side of an online bot: connects to the game server, says hello with the bot's token, and then does
/// what the server asks: starts the agent process, passes lines to it, sends its lines back (in the order it wrote
/// them) and its exit, and stops it. A dropped connection is tried again after 1, 2, 4 … 30 seconds, and the agent keeps
/// running meanwhile; only the server's <see cref="RelayRefused"/> (or cancelling) ends the run.
/// </summary>
/// <remarks>
/// Lines the agent writes while the connection is down wait in its own queue (see <see cref="AgentProcess.MaxLines"/>)
/// and go once the server has welcomed the runner again. A line that was on its way when the connection broke is lost.
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
    private SocketLines? _lines; // the connection, once the server has welcomed the runner

    /// <summary>
    /// What the owner should see, as plain text: "connected as Bot", "refused: unknown token", "reconnecting in 4 s",
    /// and the JSON of each <see cref="RelayStatus"/> the server sends. Raised on the thread that runs the relay.
    /// </summary>
    public event Action<string>? Status;

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
        using (socket)
        using (var lines = new SocketLines(socket))
        {
            lines.Send(Relay.Write(new RelayHello(_token, Relay.Version)));
            try
            {
                await foreach (var text in lines.ReadAllAsync(stop).ConfigureAwait(false))
                {
                    switch (Relay.Read(text))
                    {
                        case RelayWelcome welcome:
                            welcomed = true;
                            lock (_lock)
                            {
                                _lines = lines;
                            }

                            Status?.Invoke($"connected as {welcome.Name}");
                            break;
                        case RelayRefused refused:
                            return (refused.Reason, welcomed);
                        case RelayStart:
                            StartAgent();
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
                        _lines = null;
                    }
                }
            }
        }

        return (null, welcomed);
    }

    private void StartAgent()
    {
        StopAgent(wait: false);
        var agent = AgentProcess.Start(_command);
        lock (_lock)
        {
            _agent = agent;
        }

        _ = Task.Run(() => PumpAsync(agent));
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

    /// <summary>Sends the agent's lines to the server as it writes them, then its exit, for as long as it is the current agent.</summary>
    private async Task PumpAsync(AgentProcess agent)
    {
        while (true)
        {
            if (await agent.ReadAsync(ReadLimit).ConfigureAwait(false) is { } line)
            {
                if (!await SendAsync(agent, new RelayLine(line)).ConfigureAwait(false))
                {
                    return;
                }
            }
            else if (agent.HasExited)
            {
                await SendAsync(agent, new RelayExited(agent.Problem)).ConfigureAwait(false);
                return;
            }
        }
    }

    /// <summary>Sends a message from <paramref name="agent"/> once there is a connection; false when it is no longer the current agent.</summary>
    private async Task<bool> SendAsync(AgentProcess agent, RelayMessage message)
    {
        var text = Relay.Write(message);
        while (true)
        {
            lock (_lock)
            {
                if (_agent != agent)
                {
                    return false;
                }

                if (_lines is { } lines)
                {
                    lines.Send(text);
                    return true;
                }
            }

            await Task.Delay(ConnectionPoll).ConfigureAwait(false);
        }
    }
}
