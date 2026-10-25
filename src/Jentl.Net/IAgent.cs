namespace Jentl.Net;

/// <summary>
/// A bot that talks in lines, wherever it runs: one line in, one line out, every wait with a time limit, so a silent or
/// crashed agent costs a turn, not the game. Nothing here throws for an agent that misbehaves; <see cref="Problem"/> says
/// what happened.
/// </summary>
public interface IAgent : IDisposable
{
    /// <summary>Whether the agent is no longer running (it ended, was stopped, or never started).</summary>
    bool HasExited { get; }

    /// <summary>What is wrong with the agent ("could not start: …", "exited with code 3", "timed out"), or null when all is well.</summary>
    string? Problem { get; }

    /// <summary>The last lines the agent wrote to its error output, for the log.</summary>
    IReadOnlyList<string> ErrorOutput { get; }

    /// <summary>True while a remote agent's connection is down; it may come back. An agent that is always there is never away.</summary>
    bool IsAway => false;

    /// <summary>Sends one line to the agent. Never blocks: the line is queued and written in the background, so an agent that stops reading costs only its own answers. Does nothing when the agent is gone.</summary>
    void Send(string line);

    /// <summary>The next line from the agent, or null when none came within the limit or the agent has exited.</summary>
    Task<string?> ReadAsync(TimeSpan limit);

    /// <summary>
    /// Asks the agent something: throws away every line it sent before (late answers to earlier questions), sends the
    /// line and reads the answer. Null when there was none in time.
    /// </summary>
    Task<string?> AskAsync(string line, TimeSpan limit);

    /// <summary>Kills the agent and everything it started. Safe to call again, or on an agent that has exited.</summary>
#pragma warning disable CA1716 // Stop is what AgentProcess has always been called; it is only a keyword in Visual Basic.
    void Stop();
#pragma warning restore CA1716
}
