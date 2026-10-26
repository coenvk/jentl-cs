using System.Text.Json;
using System.Threading.Channels;

namespace Jentl.Net;

/// <summary>
/// A bot that runs on its owner's computer, as the game server sees it: an <see cref="IAgent"/> whose lines come and
/// go through a runner (<see cref="AgentRelay"/>) over a WebSocket that <see cref="RelayHost"/> attaches. The same
/// instance stays the bot's for as long as the host runs, so a runner that reconnects carries on where it was.
/// </summary>
/// <remarks>
/// While no runner is connected the agent is away (<see cref="IsAway"/>): sending drops the line and every read
/// answers null at once, so a table folds the seat without waiting. A <see cref="Restart"/> or <see cref="Stop"/> is
/// sent again on the next connection until the runner answers, so one asked for while away, or sent into a connection
/// that had broken unnoticed, still arrives. Each start has a run number that the runner sends back with the agent's
/// lines and exit; messages from an earlier run, or from before the first start, are ignored. The lines that wait to be
/// read are bounded (<see cref="AgentProcess.MaxLines"/> lines and <see cref="MaxWaiting"/> characters in all, the oldest
/// dropped first) and forgotten when the runner goes, so a bot that nobody asks cannot hold on to memory. Safe to use
/// from any thread: sockets attach and detach on their own tasks while the game asks on others.
/// </remarks>
public sealed class RemoteAgent : IAgent
{
    /// <summary>
    /// How many characters of the agent's lines wait to be read at most, all lines together; when more come, the oldest
    /// lines are dropped. Real answers are short, so this only bites a runner that sends what nobody asked for.
    /// </summary>
    public const int MaxWaiting = 1 << 18;

    private readonly Lock _lock = new();
    private readonly Channel<string> _lines = Channel.CreateUnbounded<string>(); // bounded by Keep, under the lock
    private int _waiting; // the characters of the lines in _lines
    private SocketLines? _socket; // the runner's connection, or null while away
    private RelayMessage? _order; // the last start or stop
    private bool _heard; // whether the runner answered since the last order: then it surely arrived
    private TaskCompletionSource _changed = NewChanged(); // completed when the connection goes or the agent exits
    private int _run; // the number of the last start; the runner's lines and exit carry it
    private volatile bool _exited;
    private volatile string? _problem;

    internal RemoteAgent(string name) => Name = name;

    /// <summary>The bot's name, as the host's token lookup gave it.</summary>
    public string Name { get; }

    /// <summary>True while no runner is connected for the bot.</summary>
    public bool IsAway
    {
        get
        {
            lock (_lock)
            {
                return _socket is null;
            }
        }
    }

    /// <summary>Whether the agent was stopped, or the runner said its process ended; <see cref="Restart"/> clears it.</summary>
    public bool HasExited => _exited;

    /// <summary>"stopped" after <see cref="Stop"/>, the runner's problem after its process ended, or null.</summary>
    public string? Problem => _problem;

    /// <summary>Always empty: the agent's error output stays on its owner's computer.</summary>
    public IReadOnlyList<string> ErrorOutput => [];

    /// <inheritdoc/>
    public void Send(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        lock (_lock)
        {
            if (!_exited)
            {
                _socket?.Send(Relay.Write(new RelayLine(line)));
            }
        }
    }

    /// <summary>
    /// The next line from the agent, or null when none came within the limit or the agent has exited. Null at once while
    /// the agent is away, and as soon as the runner goes while waiting.
    /// </summary>
    public async Task<string?> ReadAsync(TimeSpan limit)
    {
        using var timeout = new CancellationTokenSource(limit < TimeSpan.Zero ? TimeSpan.Zero : limit);
        try
        {
            while (true)
            {
                Task changed;
                lock (_lock)
                {
                    if (_socket is null)
                    {
                        return null;
                    }

                    // Lines that came before the exit are still read, as with AgentProcess.
                    if (Take() is { } line)
                    {
                        return line;
                    }

                    if (_exited)
                    {
                        return null;
                    }

                    changed = _changed.Task;
                }

                var waiting = _lines.Reader.WaitToReadAsync(timeout.Token).AsTask();
                if (await Task.WhenAny(waiting, changed).ConfigureAwait(false) == waiting)
                {
                    await waiting.ConfigureAwait(false); // throws when the limit passed
                }
            }
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        finally
        {
            await timeout.CancelAsync().ConfigureAwait(false); // ends a wait that a change overtook
        }
    }

    /// <inheritdoc/>
    public Task<string?> AskAsync(string line, TimeSpan limit)
    {
        lock (_lock)
        {
            Forget();
        }

        Send(line);
        return ReadAsync(limit);
    }

    /// <summary>
    /// Has the runner start a fresh process for a new match (stopping one that still runs), and forgets the lines and
    /// the exit of the one before. While away, the start is sent when the runner comes back.
    /// </summary>
    public void Restart()
    {
        lock (_lock)
        {
            Forget();
            _exited = false;
            _problem = null;
            Order(new RelayStart(++_run));
        }
    }

    /// <summary>Has the runner stop the agent. Safe to call again. While away, the stop is sent when the runner comes back.</summary>
    public void Stop()
    {
        TaskCompletionSource? changed = null;
        lock (_lock)
        {
            // A process that ended by itself keeps its own problem; only a running one is stopped by us.
            if (!_exited)
            {
                _exited = true;
                _problem = "stopped";
                changed = Change();
            }

            Order(new RelayStop());
        }

        changed?.TrySetResult();
    }

    /// <summary>Stops the agent.</summary>
    public void Dispose() => Stop();

    /// <summary>Sends <paramref name="status"/> (the game's own JSON) for the runner to show its owner; dropped while away.</summary>
    public void Status(JsonElement status)
    {
        lock (_lock)
        {
            _socket?.Send(Relay.Write(new RelayStatus(status)));
        }
    }

    /// <summary>
    /// Makes <paramref name="socket"/> the runner's connection. Sends the last start or stop again when the runner has
    /// not answered since: it was asked for while away, or went into a connection that had broken without the server
    /// noticing (the runner then comes back and takes its seat over). Returns the connection it takes over from, or null
    /// when the bot was away.
    /// </summary>
    internal SocketLines? Attach(SocketLines socket)
    {
        SocketLines? previous;
        TaskCompletionSource changed;
        lock (_lock)
        {
            (previous, _socket) = (_socket, socket);
            if (_order is { } order && !_heard)
            {
                socket.Send(Relay.Write(order));
            }

            changed = Change();
        }

        changed.TrySetResult(); // reads waiting on the connection before look again
        return previous;
    }

    /// <summary>
    /// Forgets <paramref name="socket"/> when it is still the runner's connection (the bot is away then), and the lines that
    /// wait, which nobody reads while away; true if it was.
    /// </summary>
    internal bool Detach(SocketLines socket)
    {
        TaskCompletionSource changed;
        lock (_lock)
        {
            if (_socket != socket)
            {
                return false;
            }

            _socket = null;
            Forget();
            changed = Change();
        }

        changed.TrySetResult();
        return true;
    }

    /// <summary>
    /// Takes a message the runner sent over <paramref name="socket"/>. Ignored when that connection was taken over, and
    /// when it comes from a process before the last start (its run is not the current one) or before the first start.
    /// </summary>
    internal void Receive(SocketLines socket, RelayMessage message)
    {
        TaskCompletionSource? changed = null;
        lock (_lock)
        {
            if (_socket != socket || _exited)
            {
                return;
            }

            switch (message)
            {
                case RelayLine line when _run > 0 && line.Run == _run:
                    _heard = true;
                    Keep(line.Text);
                    break;
                case RelayExited exited when _run > 0 && exited.Run == _run:
                    _heard = true;
                    _exited = true;
                    _problem = exited.Problem;
                    changed = Change();
                    break;
            }
        }

        changed?.TrySetResult();
    }

    /// <summary>The runner's connection, or null while the bot is away.</summary>
    internal SocketLines? Connection
    {
        get
        {
            lock (_lock)
            {
                return _socket;
            }
        }
    }

    /// <summary>Queues a line to be read, first dropping the oldest while there would be too many or too long. Call under the lock.</summary>
    private void Keep(string text)
    {
        while (_lines.Reader.Count > 0 && (_lines.Reader.Count >= AgentProcess.MaxLines || _waiting + text.Length > MaxWaiting))
        {
            Take();
        }

        if (_lines.Writer.TryWrite(text))
        {
            _waiting += text.Length;
        }
    }

    /// <summary>The oldest line that waits, or null when none does. Call under the lock.</summary>
    private string? Take()
    {
        if (!_lines.Reader.TryRead(out var line))
        {
            return null;
        }

        _waiting -= line.Length;
        return line;
    }

    /// <summary>Drops every line that waits. Call under the lock.</summary>
    private void Forget()
    {
        while (Take() is not null)
        {
        }
    }

    /// <summary>
    /// Sends <paramref name="message"/> (a start or stop) to the runner, if one is connected, and keeps it to send again
    /// on the next connection until the runner answers. Call under the lock.
    /// </summary>
    private void Order(RelayMessage message)
    {
        _order = message;
        _heard = false;
        _socket?.Send(Relay.Write(message));
    }

    /// <summary>Swaps in a new change signal and returns the old one, to complete outside the lock. Call under the lock.</summary>
    private TaskCompletionSource Change()
    {
        var old = _changed;
        _changed = NewChanged();
        return old;
    }

    private static TaskCompletionSource NewChanged() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
