using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Threading.Channels;

namespace Jentl.Net;

/// <summary>
/// A bot running as its own process, in any language, that talks in lines: one line in on its stdin, one line out on its
/// stdout, and whatever it writes to stderr kept for the log. Every wait has a time limit, so a silent or crashed agent
/// costs a turn, not the game. Not tied to any game or protocol.
/// </summary>
/// <remarks>
/// Nothing here throws for an agent that misbehaves: a program that cannot start is an agent that has already exited,
/// and <see cref="Problem"/> says what happened. Lines arrive on a queue; <see cref="AskAsync"/> empties it first so an
/// answer that came late is never mistaken for the answer to the next question. An agent that floods its output costs
/// no more than <see cref="MaxLines"/> lines of at most <see cref="MaxLineLength"/> characters: older lines are dropped,
/// and a longer line is cut.
/// </remarks>
public sealed class AgentProcess : IDisposable
{
    /// <summary>How many of the last lines on stderr are kept.</summary>
    public const int ErrorLines = 50;

    /// <summary>How many lines from stdout wait to be read at most; when more come, the oldest are dropped.</summary>
    public const int MaxLines = 1024;

    /// <summary>The most characters of one line that are kept, on stdout and stderr (1 MiB); the rest of a longer line is skipped.</summary>
    public const int MaxLineLength = 1 << 20;

    private static readonly TimeSpan Settle = TimeSpan.FromSeconds(2);

    private readonly Process? _process;
    private readonly string? _startProblem;
    private readonly Channel<string> _lines = Channel.CreateBounded<string>(
        new BoundedChannelOptions(MaxLines) { FullMode = BoundedChannelFullMode.DropOldest });
    private readonly Queue<string> _errors = new();
    private readonly Task _finished = Task.CompletedTask;
    private volatile bool _timedOut;
    private volatile bool _stopped;
    private readonly Channel<string> _outgoing = Channel.CreateUnbounded<string>();
    private volatile Final? _final;

    /// <summary>What was true of the agent when it was disposed (the process object can no longer be asked).</summary>
    private sealed record Final(bool HasExited, string? Problem);

    private AgentProcess(Process? process, string? startProblem)
    {
        _process = process;
        _startProblem = startProblem;
        if (process is null)
        {
            _lines.Writer.TryComplete();
            return;
        }

        _ = Task.Run(WriteInputAsync);
        var output = Task.Run(ReadOutputAsync);
        var errors = Task.Run(ReadErrorsAsync);
        _finished = Task.Run(async () =>
        {
            await process.WaitForExitAsync().ConfigureAwait(false);
            // The pipes can stay open when the agent started children of its own: do not wait for them forever.
            await Task.WhenAny(Task.WhenAll(output, errors), Task.Delay(Settle)).ConfigureAwait(false);
            _lines.Writer.TryComplete();
        });
    }

    /// <summary>Starts the agent. Never throws: a command that cannot be started gives an agent that has exited, with a <see cref="Problem"/>.</summary>
    public static AgentProcess Start(AgentCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        var info = new ProcessStartInfo(command.FileName)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        if (command.WorkingDirectory is not null)
        {
            info.WorkingDirectory = command.WorkingDirectory;
        }

        foreach (var argument in command.Arguments)
        {
            info.ArgumentList.Add(argument);
        }

        Process? process = null;
        try
        {
            process = new Process { StartInfo = info, EnableRaisingEvents = true };
            process.Start();
            return new AgentProcess(process, null);
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            process?.Dispose();
            return new AgentProcess(null, $"could not start: {e.Message}");
        }
    }

    /// <summary>Whether the agent is no longer running (it ended, was stopped, or never started).</summary>
    public bool HasExited => _final?.HasExited ?? (_process is null || _process.HasExited);

    /// <summary>What is wrong with the agent ("could not start: …", "exited with code 3", "timed out"), or null when all is well.</summary>
    public string? Problem
    {
        get
        {
            if (_startProblem is not null)
            {
                return _startProblem;
            }

            if (_final is { } final)
            {
                return final.Problem;
            }

            if (_process!.HasExited && !_stopped)
            {
                return $"exited with code {_process.ExitCode}";
            }

            return _timedOut ? "timed out" : null;
        }
    }

    /// <summary>The last <see cref="ErrorLines"/> lines the agent wrote to stderr.</summary>
    public IReadOnlyList<string> ErrorOutput
    {
        get
        {
            lock (_errors)
            {
                return [.. _errors];
            }
        }
    }

    /// <summary>Sends one line to the agent. Never blocks: the line is queued and written in the background, so an agent that stops reading costs only its own answers. Does nothing when the agent is gone.</summary>
    public void Send(string line)
    {
        if (_process is not null && _final is null)
        {
            _outgoing.Writer.TryWrite(line);
        }
    }

    /// <summary>The next line from the agent, or null when none came within the limit or the agent has exited.</summary>
    public async Task<string?> ReadAsync(TimeSpan limit)
    {
        using var timeout = new CancellationTokenSource(limit < TimeSpan.Zero ? TimeSpan.Zero : limit);
        try
        {
            var line = await _lines.Reader.ReadAsync(timeout.Token).ConfigureAwait(false);
            _timedOut = false;
            return line;
        }
        catch (OperationCanceledException)
        {
            _timedOut = true;
            return null;
        }
        catch (ChannelClosedException)
        {
            // Stdout ended: let the exit and the last of stderr settle so the problem and the error output are complete.
            await Task.WhenAny(_finished, Task.Delay(Settle)).ConfigureAwait(false);
            return null;
        }
    }

    /// <summary>
    /// Asks the agent something: throws away every line it sent before (late answers to earlier questions), sends the
    /// line and reads the answer. Null when there was none in time.
    /// </summary>
    public Task<string?> AskAsync(string line, TimeSpan limit)
    {
        while (_lines.Reader.TryRead(out _))
        {
        }

        Send(line);
        return ReadAsync(limit);
    }

    /// <summary>Kills the agent and everything it started. Safe to call again, or on an agent that has exited.</summary>
    public void Stop()
    {
        if (_process is null || _final is not null)
        {
            return;
        }

        // An exit that happened before is the agent's own (a crash keeps its problem); only a running agent is stopped by us.
        if (!_process.HasExited)
        {
            _stopped = true;
        }

        _outgoing.Writer.TryComplete();
        try
        {
            _process.Kill(entireProcessTree: true);
        }
        catch (Exception e) when (e is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            // Already gone.
        }

        try
        {
            _process.WaitForExit(Settle);
        }
        catch (InvalidOperationException)
        {
            // Never started or already disposed.
        }
    }

    /// <summary>Stops the agent.</summary>
    public void Dispose()
    {
        if (_process is null || _final is not null)
        {
            return;
        }

        Stop();
        _final = new Final(HasExited, Problem);
        _process.Dispose();
    }

    private async Task WriteInputAsync()
    {
        try
        {
            var input = _process!.StandardInput;
            await foreach (var line in _outgoing.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                await input.WriteLineAsync(line).ConfigureAwait(false);
                await input.FlushAsync().ConfigureAwait(false);
            }
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or InvalidOperationException)
        {
            // The agent ended or closed its input: the exit shows in Problem.
        }
        finally
        {
            _outgoing.Writer.TryComplete();
        }
    }

    private async Task ReadOutputAsync()
    {
        try
        {
            await ReadLinesAsync(_process!.StandardOutput, line => _lines.Writer.TryWrite(line)).ConfigureAwait(false);
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or InvalidOperationException)
        {
            // The pipe broke: same as the end of the output.
        }
        finally
        {
            _lines.Writer.TryComplete();
        }
    }

    private async Task ReadErrorsAsync()
    {
        try
        {
            await ReadLinesAsync(_process!.StandardError, line =>
            {
                lock (_errors)
                {
                    _errors.Enqueue(line);
                    if (_errors.Count > ErrorLines)
                    {
                        _errors.Dequeue();
                    }
                }
            }).ConfigureAwait(false);
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or InvalidOperationException)
        {
            // The pipe broke: same as the end of the output.
        }
    }

    /// <summary>
    /// Reads <paramref name="reader"/> to its end line by line, as <see cref="TextReader.ReadLine"/> splits them (at
    /// "\n", "\r" or "\r\n"), handing each to <paramref name="take"/> cut to <see cref="MaxLineLength"/> characters, so
    /// that a line without an end never fills the memory.
    /// </summary>
    private static async Task ReadLinesAsync(StreamReader reader, Action<string> take)
    {
        var buffer = new char[4096];
        var line = new StringBuilder();
        var open = false;
        var afterReturn = false;
        int read;
        while ((read = await reader.ReadAsync(buffer.AsMemory()).ConfigureAwait(false)) > 0)
        {
            for (var i = 0; i < read; i++)
            {
                var c = buffer[i];
                if (c == '\n' && afterReturn)
                {
                    afterReturn = false;
                    continue;
                }

                afterReturn = c == '\r';
                if (c is '\n' or '\r')
                {
                    take(line.ToString());
                    line.Clear();
                    open = false;
                }
                else
                {
                    open = true;
                    if (line.Length < MaxLineLength)
                    {
                        line.Append(c);
                    }
                }
            }
        }

        if (open)
        {
            take(line.ToString());
        }
    }
}
