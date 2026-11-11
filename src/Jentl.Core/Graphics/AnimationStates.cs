namespace Jentl.Core.Graphics;

/// <summary>
/// A sprite's animations as states (idle, run, jump, hurt) and the moves between them: when a condition holds
/// (<see cref="When"/>, from one state or <see cref="WhenAny">from any</see>), or when a state's one-off animation
/// has played out (<see cref="After"/>). Moves are checked in the order they were added, the first that applies
/// wins; <see cref="Animator"/> plays the state's animation and raises its frame events.
/// </summary>
/// <example>
/// <code>
/// var states = new AnimationStates&lt;Pose&gt;(Pose.Idle)
///     .Add(Pose.Idle, idle).Add(Pose.Run, run).Add(Pose.Jump, jump).Add(Pose.Hurt, hurt)
///     .WhenAny(Pose.Hurt, () => player.WasHit)
///     .After(Pose.Hurt, Pose.Idle)
///     .When(Pose.Idle, Pose.Run, () => player.Speed > 0.1f)
///     .When(Pose.Run, Pose.Idle, () => player.Speed &lt;= 0.1f)
///     .WhenAny(Pose.Jump, () => !player.OnGround);
/// </code>
/// </example>
public sealed class AnimationStates<TState>
    where TState : notnull
{
    private readonly Dictionary<TState, Animation> _animations = [];
    private readonly List<Move> _moves = [];

    public AnimationStates(TState start) => State = start;

    public TState State { get; private set; }

    public Animator Animator { get; } = new();

    /// <summary>The frame to draw now.</summary>
    public int Frame => Animator.Frame;

    /// <summary>Raised on a change of state, with the old state and the new.</summary>
    public event Action<TState, TState>? Changed;

    public AnimationStates<TState> Add(TState state, Animation animation)
    {
        _animations[state] = animation ?? throw new ArgumentNullException(nameof(animation));
        if (EqualityComparer<TState>.Default.Equals(state, State))
        {
            Animator.Play(animation, restart: true);
        }

        return this;
    }

    /// <summary>From <paramref name="from"/> to <paramref name="to"/> when <paramref name="condition"/> holds.</summary>
    public AnimationStates<TState> When(TState from, TState to, Func<bool> condition)
    {
        _moves.Add(new Move(from, true, to, condition ?? throw new ArgumentNullException(nameof(condition)), false));
        return this;
    }

    /// <summary>From whatever state to <paramref name="to"/> when <paramref name="condition"/> holds.</summary>
    public AnimationStates<TState> WhenAny(TState to, Func<bool> condition)
    {
        _moves.Add(new Move(default!, false, to, condition ?? throw new ArgumentNullException(nameof(condition)), false));
        return this;
    }

    /// <summary>From <paramref name="from"/> to <paramref name="to"/> once <paramref name="from"/>'s animation has played through.</summary>
    public AnimationStates<TState> After(TState from, TState to)
    {
        _moves.Add(new Move(from, true, to, null, true));
        return this;
    }

    /// <summary>Changes the state now, whatever the moves say.</summary>
    public void Set(TState state)
    {
        if (EqualityComparer<TState>.Default.Equals(state, State))
        {
            return;
        }

        var old = State;
        State = state;
        if (_animations.TryGetValue(state, out var animation))
        {
            Animator.Play(animation, restart: true);
        }

        Changed?.Invoke(old, state);
    }

    public void Update(TimeSpan elapsed)
    {
        Animator.Update(elapsed);
        foreach (var move in _moves)
        {
            if (move.HasFrom && !EqualityComparer<TState>.Default.Equals(move.From, State))
            {
                continue;
            }

            if (EqualityComparer<TState>.Default.Equals(move.To, State))
            {
                continue;
            }

            if (move.AtEnd ? Animator.IsDone : move.Condition!())
            {
                Set(move.To);
                break;
            }
        }
    }

    private sealed record Move(TState From, bool HasFrom, TState To, Func<bool>? Condition, bool AtEnd);
}
