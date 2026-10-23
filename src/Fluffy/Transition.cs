namespace Fluffy;

/// <summary>
/// A move from one state to another, handed to entry, exit and transition actions.
/// </summary>
/// <param name="Source">The state the machine was in (the innermost one).</param>
/// <param name="Destination">The state the machine ends up in, after following initial transitions.</param>
/// <param name="Trigger">What caused it; not meaningful when <paramref name="IsTimed"/> or <paramref name="IsStart"/>.</param>
/// <param name="IsTimed">Caused by an <c>After</c> timer rather than a trigger.</param>
/// <param name="IsStart">The machine entering its initial state.</param>
public readonly record struct Transition<TState, TTrigger>(TState Source, TState Destination, TTrigger Trigger, bool IsTimed = false, bool IsStart = false)
    where TState : notnull
    where TTrigger : notnull
{
    /// <summary>Leaves and enters the same state again.</summary>
    public bool IsReentry => EqualityComparer<TState>.Default.Equals(Source, Destination) && !IsStart;

    public override string ToString() =>
        IsStart ? $"start -> {Destination}" : $"{Source} -({(IsTimed ? "timer" : Trigger.ToString())})-> {Destination}";
}

/// <summary>A trigger fired in a state with no way to handle it.</summary>
public sealed class UnhandledTriggerException : InvalidOperationException
{
    public UnhandledTriggerException()
    {
    }

    public UnhandledTriggerException(string message)
        : base(message)
    {
    }

    public UnhandledTriggerException(string message, Exception inner)
        : base(message, inner)
    {
    }
}
