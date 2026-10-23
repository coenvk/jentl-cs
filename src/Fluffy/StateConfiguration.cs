namespace Fluffy;

/// <summary>
/// How one state behaves: which triggers it answers and how, what runs on entering and leaving it,
/// its timers, and where it sits in the hierarchy. Calls chain:
/// <code>
/// machine.Configure(Mood.Idle)
///     .OnEnter(_ => Yawn())
///     .Permit(Signal.Poke, Mood.Grumpy)
///     .PermitIf(Signal.Treat, Mood.Happy, () => IsHungry, "hungry")
///     .After(TimeSpan.FromSeconds(5), Mood.Asleep);
/// </code>
/// </summary>
public sealed class StateConfiguration<TState, TTrigger>
    where TState : notnull
    where TTrigger : notnull
{
    private readonly StateMachine<TState, TTrigger> _machine;
    private readonly Dictionary<TTrigger, List<Handler>> _handlers = [];
    private readonly List<Timer> _timers = [];
    private readonly List<Action<Transition<TState, TTrigger>>> _entry = [];
    private readonly List<Action<Transition<TState, TTrigger>>> _exit = [];

    internal StateConfiguration(StateMachine<TState, TTrigger> machine, TState state)
    {
        _machine = machine;
        State = state;
    }

    public TState State { get; }

    /// <summary>The state this one is part of, if any.</summary>
    public TState? Superstate { get; private set; }

    public bool HasSuperstate { get; private set; }

    /// <summary>The substate entered whenever this state is entered.</summary>
    public TState? Initial { get; private set; }

    public bool HasInitial { get; private set; }

    internal IReadOnlyDictionary<TTrigger, List<Handler>> Handlers => _handlers;

    internal IReadOnlyList<Timer> Timers => _timers;

    internal IReadOnlyList<Action<Transition<TState, TTrigger>>> Entry => _entry;

    internal IReadOnlyList<Action<Transition<TState, TTrigger>>> Exit => _exit;

    /// <summary>On <paramref name="trigger"/>, move to <paramref name="destination"/> (to this same state: leave and enter it again).</summary>
    public StateConfiguration<TState, TTrigger> Permit(TTrigger trigger, TState destination) =>
        Add(trigger, new Handler(HandlerKind.Move, _ => destination, null, null, null) { Target = destination });

    /// <summary>Like <see cref="Permit"/>, but only while <paramref name="guard"/> holds; otherwise the trigger is tried in the superstate.</summary>
    /// <param name="description">Names the guard in diagrams and error messages.</param>
    public StateConfiguration<TState, TTrigger> PermitIf(TTrigger trigger, TState destination, Func<bool> guard, string? description = null)
    {
        ArgumentNullException.ThrowIfNull(guard);
        return Add(trigger, new Handler(HandlerKind.Move, _ => destination, guard, description, null) { Target = destination });
    }

    /// <summary>On <paramref name="trigger"/>, move to whichever state <paramref name="destination"/> picks at that moment.</summary>
    public StateConfiguration<TState, TTrigger> PermitDynamic(TTrigger trigger, Func<TState> destination, Func<bool>? guard = null, string? description = null)
    {
        ArgumentNullException.ThrowIfNull(destination);
        return Add(trigger, new Handler(HandlerKind.Move, _ => destination(), guard, description, null));
    }

    /// <summary>Answers <paramref name="trigger"/> by running <paramref name="action"/>, without leaving or entering any state.</summary>
    public StateConfiguration<TState, TTrigger> InternalTransition(TTrigger trigger, Action<Transition<TState, TTrigger>> action, Func<bool>? guard = null, string? description = null)
    {
        ArgumentNullException.ThrowIfNull(action);
        return Add(trigger, new Handler(HandlerKind.Internal, null, guard, description, action));
    }

    /// <summary>Accepts <paramref name="trigger"/> and does nothing (instead of treating it as unhandled).</summary>
    public StateConfiguration<TState, TTrigger> Ignore(TTrigger trigger, Func<bool>? guard = null, string? description = null) =>
        Add(trigger, new Handler(HandlerKind.Ignore, null, guard, description, null));

    /// <summary>Runs on entering this state (also when entering one of its substates from outside).</summary>
    public StateConfiguration<TState, TTrigger> OnEnter(Action<Transition<TState, TTrigger>> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        _entry.Add(action);
        return this;
    }

    /// <summary>Runs on leaving this state.</summary>
    public StateConfiguration<TState, TTrigger> OnExit(Action<Transition<TState, TTrigger>> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        _exit.Add(action);
        return this;
    }

    /// <summary>
    /// Moves to <paramref name="destination"/> once the machine has spent <paramref name="delay"/> in this state
    /// (measured by <see cref="StateMachine{TState, TTrigger}.Update"/>; time in substates counts).
    /// </summary>
    public StateConfiguration<TState, TTrigger> After(TimeSpan delay, TState destination, Func<bool>? guard = null, string? description = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(delay, TimeSpan.Zero);
        _timers.Add(new Timer(delay, destination, guard, description));
        return this;
    }

    /// <summary>Makes this a substate of <paramref name="superstate"/>: it inherits the superstate's triggers and timers.</summary>
    public StateConfiguration<TState, TTrigger> SubstateOf(TState superstate)
    {
        if (_machine.IsSelfOrAncestor(State, superstate))
        {
            throw new ArgumentException($"{State} cannot be a substate of {superstate}: that would make a loop.", nameof(superstate));
        }

        Superstate = superstate;
        HasSuperstate = true;
        _machine.Configure(superstate);
        return this;
    }

    /// <summary>Whenever this state is entered, carry on into <paramref name="substate"/> (which must be a substate of it).</summary>
    public StateConfiguration<TState, TTrigger> InitialTransition(TState substate)
    {
        Initial = substate;
        HasInitial = true;
        return this;
    }

    private StateConfiguration<TState, TTrigger> Add(TTrigger trigger, Handler handler)
    {
        if (!_handlers.TryGetValue(trigger, out var list))
        {
            _handlers[trigger] = list = [];
        }

        list.Add(handler);
        return this;
    }

    internal enum HandlerKind
    {
        Move,
        Internal,
        Ignore,
    }

    internal sealed record Handler(
        HandlerKind Kind,
        Func<TTrigger, TState>? Destination,
        Func<bool>? Guard,
        string? Description,
        Action<Transition<TState, TTrigger>>? Action)
    {
        /// <summary>The destination when it is fixed (not dynamic), for diagrams and validation.</summary>
        public TState? Target { get; init; }

        public bool IsDynamic => Kind == HandlerKind.Move && Target is null;

        public bool Allows => Guard?.Invoke() ?? true;
    }

    internal sealed record Timer(TimeSpan Delay, TState Destination, Func<bool>? Guard, string? Description)
    {
        public bool Allows => Guard?.Invoke() ?? true;
    }
}
