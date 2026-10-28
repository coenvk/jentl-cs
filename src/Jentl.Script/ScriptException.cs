namespace Jentl.Script;

/// <summary>
/// A mistake in a script, found while reading it (<see cref="ScriptSyntaxException"/>) or while running it. The
/// message starts with where it happened, as <c>name:line:</c>, and <see cref="Trace"/> lists the functions it was in.
/// </summary>
public class ScriptException : Exception
{
    public ScriptException()
    {
    }

    public ScriptException(string message)
        : base(message)
    {
        Reason = message;
    }

    public ScriptException(string message, Exception innerException)
        : base(message, innerException)
    {
        Reason = message;
    }

    internal ScriptException(string reason, string? source, int line, IReadOnlyList<string> trace, Exception? inner = null)
        : base(source is null ? reason : $"{source}:{line}: {reason}", inner)
    {
        Reason = reason;
        ScriptName = source;
        Line = line;
        Trace = trace;
    }

    /// <summary>What went wrong, without where.</summary>
    public string Reason { get; } = "";

    /// <summary>The name the script was loaded under, or null before it is known.</summary>
    public string? ScriptName { get; }

    /// <summary>The line (from 1) it happened on, or 0 before it is known.</summary>
    public int Line { get; }

    /// <summary>The calls it happened in, innermost first, as <c>name:line in function</c>.</summary>
    public IReadOnlyList<string> Trace { get; } = [];
}

/// <summary>A script that can't be read: a typo, a missing <c>end</c>, a name used before <c>let</c>.</summary>
public sealed class ScriptSyntaxException : ScriptException
{
    public ScriptSyntaxException()
    {
    }

    public ScriptSyntaxException(string message)
        : base(message)
    {
    }

    public ScriptSyntaxException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    internal ScriptSyntaxException(string reason, string source, int line, int column)
        : base(reason, source, line, [$"{source}:{line}:{column}"])
    {
        Column = column;
    }

    /// <summary>The column (from 1) on <see cref="ScriptException.Line"/>.</summary>
    public int Column { get; }
}
