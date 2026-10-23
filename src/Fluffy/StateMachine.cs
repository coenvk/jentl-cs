namespace Fluffy;

/// <summary>
/// A finite state machine with typed states and triggers (enums, strings, records: anything with equality).
/// </summary>
/// <remarks>
/// <para>
/// States can nest: a substate inherits its superstate's triggers and timers, and entering or leaving the
/// superstate runs its entry and exit actions only when crossing its border. A trigger is looked up in the
/// current state first, then in each superstate outwards; the first handler whose guard holds wins.
/// </para>
/// <para>
/// Triggers fired from inside an action are queued and handled once the current transition has finished
/// (run to completion), so actions always see a consistent state. The machine starts lazily: the initial
/// state's entry actions run on <see cref="Start"/>, or on the first <see cref="Fire"/> or <see cref="Update"/>.
/// </para>
/// </remarks>
public sealed partial class StateMachine<TState, TTrigger>
    where TState : notnull
    where TTrigger : notnull
{
    // Timers that fire into states whose timers are already due could otherwise loop forever.
    private const int MaxTimedTransitionsPerUpdate = 64;

    private static readonly EqualityComparer<TState> Same = EqualityComparer<TState>.Default;

    private readonly Dictionary<TState, StateConfiguration<TState, TTrigger>> _states = [];
    private readonly Dictionary<TState, TimeSpan> _timeIn = [];
    private readonly Queue<(TTrigger Trigger, bool Throw)> _queue = new();
    private bool _busy;

    public StateMachine(TState initial)
    {
        Initial = initial;
        State = initial;
    }

    /// <summary>Raised after every transition, internal ones included.</summary>
    public event Action<Transition<TState, TTrigger>>? Transitioned;

    public TState Initial { get; }

    /// <summary>The current, innermost state.</summary>
    public TState State { get; private set; }

    public bool IsStarted { get; private set; }

    /// <summary>Handles triggers nothing answers; without it, <see cref="Fire"/> throws <see cref="UnhandledTriggerException"/>.</summary>
    public Action<TState, TTrigger>? OnUnhandled { get; set; }

    /// <summary>The states configured so far (including superstates and destinations named in configuration).</summary>
    public IEnumerable<TState> States => _states.Keys;

    /// <summary>Gets the configuration of <paramref name="state"/>, creating it the first time.</summary>
    public StateConfiguration<TState, TTrigger> Configure(TState state)
    {
        if (!_states.TryGetValue(state, out var configuration))
        {
            configuration = new StateConfiguration<TState, TTrigger>(this, state);
            _states[state] = configuration;
        }

        return configuration;
    }

    /// <summary>Whether the machine is in <paramref name="state"/>: the current state or one of its superstates.</summary>
    public bool IsIn(TState state) => IsSelfOrAncestor(state, State);

    /// <summary>The current state followed by its superstates, innermost first.</summary>
    public IEnumerable<TState> ActiveStates => Lineage(State);

    /// <summary>How long the machine has been in <paramref name="state"/> (as counted by <see cref="Update"/>); zero if it is not in it.</summary>
    public TimeSpan TimeIn(TState state) => _timeIn.GetValueOrDefault(state);

    /// <summary>Enters the initial state, running its entry actions. Does nothing once started.</summary>
    public void Start()
    {
        if (IsStarted)
        {
            return;
        }

        IsStarted = true;
        var entered = new List<TState>();
        foreach (var state in Lineage(Initial).Reverse())
        {
            entered.Add(state);
        }

        Run(() =>
        {
            var start = new Transition<TState, TTrigger>(Initial, Initial, default!, IsStart: true);
            foreach (var state in entered)
            {
                EnterState(state, start);
            }

            State = Initial;
            var final = DescendInitial(start);
            Transitioned?.Invoke(start with { Destination = final });
        });
    }

    /// <summary>Whether <paramref name="trigger"/> would be handled now (guards included).</summary>
    public bool CanFire(TTrigger trigger) => Find(trigger, out _) is not null;

    /// <summary>The triggers that would be handled now.</summary>
    public IReadOnlyList<TTrigger> PermittedTriggers =>
        [.. ActiveStates.SelectMany(s => Config(s)?.Handlers.Keys ?? []).Distinct().Where(CanFire)];

    /// <summary>
    /// Handles <paramref name="trigger"/>. Throws <see cref="UnhandledTriggerException"/> if nothing answers it
    /// (unless <see cref="OnUnhandled"/> is set). Called from inside an action, the trigger waits until the
    /// current transition is done.
    /// </summary>
    public void Fire(TTrigger trigger) => Enqueue(trigger, throwIfUnhandled: true);

    /// <summary>Like <see cref="Fire"/>, but returns false instead of throwing when nothing answers the trigger.</summary>
    public bool TryFire(TTrigger trigger)
    {
        if (_busy)
        {
            _queue.Enqueue((trigger, false));
            return true;
        }

        Start();
        var handled = false;
        Run(() => handled = Handle(trigger, throwIfUnhandled: false));
        return handled;
    }

    /// <summary>Lets time pass for timers (<see cref="StateConfiguration{TState, TTrigger}.After"/>).</summary>
    public void Update(TimeSpan elapsed)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(elapsed, TimeSpan.Zero);
        Start();
        Run(() =>
        {
            foreach (var state in ActiveStates.ToArray())
            {
                _timeIn[state] = TimeIn(state) + elapsed;
            }

            for (var i = 0; i < MaxTimedTransitionsPerUpdate && DueTimer() is { } due; i++)
            {
                // Restart the clock of the state that owns the timer, in case the move keeps the machine in it.
                _timeIn[due.State] = TimeSpan.Zero;
                Move(due.Timer.Destination, default!, timed: true);
            }
        });
    }

    internal bool IsSelfOrAncestor(TState ancestor, TState state) => Lineage(state).Any(s => Same.Equals(s, ancestor));

    private StateConfiguration<TState, TTrigger>? Config(TState state) => _states.GetValueOrDefault(state);

    /// <summary>The state and its superstates, innermost first.</summary>
    private IEnumerable<TState> Lineage(TState state)
    {
        for (var current = state; ;)
        {
            yield return current;
            if (Config(current) is not { HasSuperstate: true } config)
            {
                yield break;
            }

            current = config.Superstate!;
        }
    }

    private void Enqueue(TTrigger trigger, bool throwIfUnhandled)
    {
        if (_busy)
        {
            _queue.Enqueue((trigger, throwIfUnhandled));
            return;
        }

        Start();
        Run(() => Handle(trigger, throwIfUnhandled));
    }

    /// <summary>Runs <paramref name="work"/>, then any triggers queued meanwhile, one at a time.</summary>
    private void Run(Action work)
    {
        if (_busy)
        {
            work();
            return;
        }

        _busy = true;
        try
        {
            work();
            while (_queue.TryDequeue(out var queued))
            {
                Handle(queued.Trigger, queued.Throw);
            }
        }
        finally
        {
            _busy = false;
            _queue.Clear();
        }
    }

    private bool Handle(TTrigger trigger, bool throwIfUnhandled)
    {
        var handler = Find(trigger, out var unmet);
        if (handler is null)
        {
            if (OnUnhandled is { } unhandled)
            {
                unhandled(State, trigger);
                return false;
            }

            if (!throwIfUnhandled)
            {
                return false;
            }

            var because = unmet.Count > 0 ? $" (guards not met: {string.Join(", ", unmet)})" : "";
            throw new UnhandledTriggerException($"No transition for {trigger} in state {State}{because}.");
        }

        switch (handler.Kind)
        {
            case StateConfiguration<TState, TTrigger>.HandlerKind.Move:
                Move(handler.Destination!(trigger), trigger, timed: false);
                break;
            case StateConfiguration<TState, TTrigger>.HandlerKind.Internal:
                var transition = new Transition<TState, TTrigger>(State, State, trigger);
                handler.Action!(transition);
                Transitioned?.Invoke(transition);
                break;
        }

        return true;
    }

    private StateConfiguration<TState, TTrigger>.Handler? Find(TTrigger trigger, out List<string> unmet)
    {
        unmet = [];
        foreach (var state in ActiveStates)
        {
            if (Config(state) is not { } config || !config.Handlers.TryGetValue(trigger, out var handlers))
            {
                continue;
            }

            foreach (var handler in handlers)
            {
                if (handler.Allows)
                {
                    return handler;
                }

                unmet.Add(handler.Description ?? "a guard");
            }
        }

        return null;
    }

    private (TState State, StateConfiguration<TState, TTrigger>.Timer Timer)? DueTimer()
    {
        foreach (var state in ActiveStates)
        {
            foreach (var timer in Config(state)?.Timers ?? [])
            {
                if (TimeIn(state) >= timer.Delay && timer.Allows)
                {
                    return (state, timer);
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Leaves states from the current one up to (not including) the closest state shared with the
    /// destination, enters states down to the destination, then follows initial transitions.
    /// A transition to the current state itself leaves and re-enters it.
    /// </summary>
    private void Move(TState destination, TTrigger trigger, bool timed)
    {
        var source = State;
        var transition = new Transition<TState, TTrigger>(source, destination, trigger, timed);
        List<TState> exit, enter;
        if (Same.Equals(source, destination))
        {
            exit = [source];
            enter = [destination];
        }
        else
        {
            // Keep the closest state both are in (if any); moving to a superstate of the current state leaves
            // down to it without leaving or re-entering it.
            var from = Lineage(source).ToList();
            var to = Lineage(destination).ToList();
            var shared = from.FindIndex(s => to.Contains(s, Same));
            exit = shared < 0 ? from : from[..shared];
            enter = [.. to.TakeWhile(t => shared < 0 || !Same.Equals(t, from[shared])).Reverse()];
        }

        foreach (var state in exit)
        {
            ExitState(state, transition);
        }

        foreach (var state in enter)
        {
            EnterState(state, transition);
        }

        State = destination;
        var final = DescendInitial(transition);
        Transitioned?.Invoke(transition with { Destination = final });
    }

    /// <summary>Follows initial transitions down from the current state; returns where it ends.</summary>
    private TState DescendInitial(Transition<TState, TTrigger> transition)
    {
        for (var depth = 0; Config(State) is { HasInitial: true } config; depth++)
        {
            if (depth > _states.Count)
            {
                throw new InvalidOperationException($"Initial transitions from {State} go round in circles.");
            }

            var child = config.Initial!;
            if (!Lineage(child).Skip(1).Any(s => Same.Equals(s, State)))
            {
                throw new InvalidOperationException($"The initial transition of {State} goes to {child}, which is not one of its substates.");
            }

            // Enter every state between this one and the chosen substate.
            foreach (var state in Lineage(child).TakeWhile(s => !Same.Equals(s, State)).Reverse())
            {
                EnterState(state, transition);
            }

            State = child;
        }

        return State;
    }

    private void EnterState(TState state, Transition<TState, TTrigger> transition)
    {
        // Entry actions see the state being entered as current.
        State = state;
        _timeIn[state] = TimeSpan.Zero;
        foreach (var action in Config(state)?.Entry ?? [])
        {
            action(transition);
        }
    }

    private void ExitState(TState state, Transition<TState, TTrigger> transition)
    {
        foreach (var action in Config(state)?.Exit ?? [])
        {
            action(transition);
        }

        _timeIn.Remove(state);
    }
}
