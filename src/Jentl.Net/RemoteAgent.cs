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
/// answers null at once, so a table folds the seat without waiting. A <see cref="Restart"/> or <see cref="Stop"/>
/// while away is passed on when the runner comes back. Safe to use from any thread: sockets attach and detach on their
/// own tasks while the game asks on others.
/// </remarks>
public sealed class RemoteAgent : IAgent
{
    private readonly Lock _lock = new();
    private readonly Channel<string> _lines = Channel.CreateBounded<string>(
        new BoundedChannelOptions(AgentProcess.MaxLines) { FullMode = BoundedChannelFullMode.DropOldest });
    private SocketLines? _socket; // the runner's connection, or null while away
    private RelayMessage? _owed; // a start or stop asked for while away, to send when the runner comes back
    private TaskCompletionSource _changed = NewChanged(); // completed when the connection goes or the agent exits
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
                    if (_lines.Reader.TryRead(out var line))
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
        while (_lines.Reader.TryRead(out _))
        {
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
            while (_lines.Reader.TryRead(out _))
            {
            }

            _exited = false;
            _problem = null;
            Order(new RelayStart());
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
    /// Makes <paramref name="socket"/> the runner's connection and sends a start or stop that was asked for while away.
    /// Returns the connection it takes over from, or null when the bot was away.
    /// </summary>
    internal SocketLines? Attach(SocketLines socket)
    {
        SocketLines? previous;
        TaskCompletionSource changed;
        lock (_lock)
        {
            (previous, _socket) = (_socket, socket);
            if (_owed is { } owed)
            {
                socket.Send(Relay.Write(owed));
                _owed = null;
            }

            changed = Change();
        }

        changed.TrySetResult(); // reads waiting on the connection before look again
        return previous;
    }

    /// <summary>Forgets <paramref name="socket"/> when it is still the runner's connection (the bot is away then); true if it was.</summary>
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
            changed = Change();
        }

        changed.TrySetResult();
        return true;
    }

    /// <summary>Takes a message the runner sent over <paramref name="socket"/>; ignored when that connection was taken over.</summary>
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
                case RelayLine line:
                    _lines.Writer.TryWrite(line.Text);
                    break;
                case RelayExited exited:
                    _exited = true;
                    _problem = exited.Problem;
                    changed = Change();
                    break;
            }
        }

        changed?.TrySetResult();
    }

    /// <summary>Sends <paramref name="message"/> to the runner, or keeps it for when the runner comes back. Call under the lock.</summary>
    private void Order(RelayMessage message)
    {
        if (_socket is { } socket)
        {
            socket.Send(Relay.Write(message));
        }
        else
        {
            _owed = message;
        }
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
