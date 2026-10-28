namespace Jentl.Ai;

/// <summary>How a node did this tick.</summary>
public enum Status
{
    /// <summary>Done, and it worked.</summary>
    Success,

    /// <summary>Done, and it didn't (or couldn't).</summary>
    Failure,

    /// <summary>Still at it: tick it again next frame.</summary>
    Running,
}

/// <summary>A tick's time: how long the frame was, and the tree's time now (for cooldowns that run while a branch isn't reached).</summary>
public readonly record struct Clock(TimeSpan Elapsed, TimeSpan Now);

/// <summary>
/// One node of a behaviour tree over a game's <typeparamref name="T"/> (the monster, the world it sees). A node is
/// ticked every frame it is in play and says how it did; one that was running and is no longer reached is
/// <see cref="Reset"/>, so it starts afresh next time. Build trees with <see cref="Bt"/>.
/// </summary>
public abstract class Node<T>
{
    public abstract Status Tick(T context, Clock clock);

    /// <summary>Forgets any progress (a sequence's place, a wait's clock).</summary>
    public virtual void Reset()
    {
    }
}

/// <summary>
/// A behaviour tree: a monster's mind as a tree of conditions and actions. Each frame <see cref="Tick"/> walks it from
/// the root: a <see cref="Bt.Selector{T}"/> tries its children until one doesn't fail (the first plan that works), a
/// <see cref="Bt.Sequence{T}"/> runs them in order until one fails (the steps of a plan), and a
/// <c>Bt.Best</c> picks the child that scores highest right now (utility AI).
/// </summary>
public sealed class BehaviourTree<T>(Node<T> root)
{
    public Node<T> Root { get; } = root ?? throw new ArgumentNullException(nameof(root));

    /// <summary>How the root did on the last tick.</summary>
    public Status Last { get; private set; } = Status.Success;

    /// <summary>The tree's own time: the elapsed times it was ticked with, added up.</summary>
    public TimeSpan Now { get; private set; }

    /// <summary>Runs the tree one frame of <paramref name="elapsed"/>. A tree that finished starts over from the root next time.</summary>
    public Status Tick(T context, TimeSpan elapsed)
    {
        Now += elapsed;
        Last = Root.Tick(context, new Clock(elapsed, Now));
        if (Last != Status.Running)
        {
            Root.Reset();
        }

        return Last;
    }

    public void Reset()
    {
        Root.Reset();
        Last = Status.Success;
    }
}

/// <summary>Builds behaviour tree nodes.</summary>
public static class Bt
{
    /// <summary>
    /// Runs the children in order while they succeed; fails at the first that fails. While one runs it picks up there
    /// next tick, so earlier steps are not done again (and earlier conditions not checked again: guard a step that
    /// must stop when something changes with <see cref="While{T}"/>).
    /// </summary>
    public static Node<T> Sequence<T>(params Node<T>[] children) => new SequenceNode<T>(children);

    /// <summary>
    /// Tries the children in order until one succeeds or runs; fails only if all fail. It tries again from the first
    /// every tick, so a higher priority (attack, once the player is seen) takes over from a lower one that was running
    /// (patrol), which is reset.
    /// </summary>
    public static Node<T> Selector<T>(params Node<T>[] children) => new SelectorNode<T>(children);

    /// <summary>
    /// Runs the child while <paramref name="condition"/> holds, checking it every tick: the moment it fails, the
    /// child is reset and this fails, so a running plan stops when what it was for no longer holds.
    /// </summary>
    public static Node<T> While<T>(Func<T, bool> condition, Node<T> child) => new WhileNode<T>(condition, child);

    /// <summary>
    /// Utility choice: scores every option each tick and runs the best (scores of 0 or less are never chosen; fails
    /// when none scores). While its choice runs, a new one takes over only if it scores more than
    /// <paramref name="stickiness"/> higher, so the mind doesn't flicker between two close options.
    /// </summary>
    public static Node<T> Best<T>(float stickiness, params (Func<T, float> Score, Node<T> Node)[] options) => new Utility<T>(options, stickiness);

    /// <summary><c>Best</c> without stickiness.</summary>
    public static Node<T> Best<T>(params (Func<T, float> Score, Node<T> Node)[] options) => new Utility<T>(options, 0);

    /// <summary>Succeeds when <paramref name="condition"/> holds, fails otherwise.</summary>
    public static Node<T> If<T>(Func<T, bool> condition) => new Leaf<T>((c, _) => condition(c) ? Status.Success : Status.Failure);

    /// <summary>Does something that may take several frames, saying how it went.</summary>
    public static Node<T> Do<T>(Func<T, TimeSpan, Status> action) => new Leaf<T>((c, clock) => action(c, clock.Elapsed));

    /// <summary>Does something at once, and succeeds.</summary>
    public static Node<T> Do<T>(Action<T> action) => new Leaf<T>((c, _) =>
    {
        action(c);
        return Status.Success;
    });

    /// <summary>Runs for <paramref name="time"/>, then succeeds.</summary>
    public static Node<T> Wait<T>(TimeSpan time) => new Timer<T>(time);

    /// <summary>Success becomes failure and failure success; running stays running.</summary>
    public static Node<T> Not<T>(Node<T> child) => new Decorator<T>(child, s => s switch
    {
        Status.Success => Status.Failure,
        Status.Failure => Status.Success,
        _ => Status.Running,
    });

    /// <summary>Succeeds whenever the child finishes, however it did: an optional step.</summary>
    public static Node<T> Try<T>(Node<T> child) => new Decorator<T>(child, s => s == Status.Running ? Status.Running : Status.Success);

    /// <summary>Lets the child run only once every <paramref name="time"/>: fails meanwhile (a spell's cooldown).</summary>
    public static Node<T> Cooldown<T>(TimeSpan time, Node<T> child) => new CooldownNode<T>(time, child);

    /// <summary>Runs the child again each time it succeeds, <paramref name="times"/> times in all; fails if it fails.</summary>
    public static Node<T> Repeat<T>(int times, Node<T> child) => new RepeatNode<T>(times, child);

    private sealed class Leaf<T>(Func<T, Clock, Status> tick) : Node<T>
    {
        public override Status Tick(T context, Clock clock) => tick(context, clock);
    }

    private sealed class SequenceNode<T>(Node<T>[] children) : Node<T>
    {
        private int _current;

        public override Status Tick(T context, Clock clock)
        {
            while (_current < children.Length)
            {
                var status = children[_current].Tick(context, clock);
                if (status != Status.Success)
                {
                    if (status == Status.Failure)
                    {
                        Reset();
                    }

                    return status;
                }

                children[_current].Reset();
                _current++;
            }

            Reset();
            return Status.Success;
        }

        public override void Reset()
        {
            if (_current < children.Length)
            {
                children[_current].Reset();
            }

            _current = 0;
        }
    }

    private sealed class SelectorNode<T>(Node<T>[] children) : Node<T>
    {
        private int _running = -1;

        public override Status Tick(T context, Clock clock)
        {
            for (var i = 0; i < children.Length; i++)
            {
                var status = children[i].Tick(context, clock);
                if (status == Status.Failure)
                {
                    continue;
                }

                // A higher priority took over: the one that was running gives up.
                if (_running >= 0 && _running != i)
                {
                    children[_running].Reset();
                }

                _running = status == Status.Running ? i : -1;
                return status;
            }

            Reset();
            return Status.Failure;
        }

        public override void Reset()
        {
            if (_running >= 0)
            {
                children[_running].Reset();
            }

            _running = -1;
        }
    }

    private sealed class WhileNode<T>(Func<T, bool> condition, Node<T> child) : Node<T>
    {
        public override Status Tick(T context, Clock clock)
        {
            if (condition(context))
            {
                return child.Tick(context, clock);
            }

            child.Reset();
            return Status.Failure;
        }

        public override void Reset() => child.Reset();
    }

    private sealed class Utility<T>((Func<T, float> Score, Node<T> Node)[] options, float stickiness) : Node<T>
    {
        private int _running = -1;

        public override Status Tick(T context, Clock clock)
        {
            var best = -1;
            var bestScore = 0f;
            for (var i = 0; i < options.Length; i++)
            {
                var score = options[i].Score(context) + (i == _running ? stickiness : 0);
                if (score > bestScore)
                {
                    (best, bestScore) = (i, score);
                }
            }

            if (best != _running && _running >= 0)
            {
                options[_running].Node.Reset();
            }

            if (best < 0)
            {
                _running = -1;
                return Status.Failure;
            }

            var status = options[best].Node.Tick(context, clock);
            _running = status == Status.Running ? best : -1;
            return status;
        }

        public override void Reset()
        {
            if (_running >= 0)
            {
                options[_running].Node.Reset();
            }

            _running = -1;
        }
    }

    private sealed class Timer<T>(TimeSpan time) : Node<T>
    {
        private TimeSpan _waited;

        public override Status Tick(T context, Clock clock)
        {
            _waited += clock.Elapsed;
            if (_waited < time)
            {
                return Status.Running;
            }

            _waited = TimeSpan.Zero;
            return Status.Success;
        }

        public override void Reset() => _waited = TimeSpan.Zero;
    }

    private sealed class Decorator<T>(Node<T> child, Func<Status, Status> map) : Node<T>
    {
        public override Status Tick(T context, Clock clock) => map(child.Tick(context, clock));

        public override void Reset() => child.Reset();
    }

    /// <summary>Goes by the tree's time, so the cooldown runs out while the branch isn't reached too.</summary>
    private sealed class CooldownNode<T>(TimeSpan time, Node<T> child) : Node<T>
    {
        private TimeSpan? _readyAt;

        public override Status Tick(T context, Clock clock)
        {
            if (clock.Now < _readyAt)
            {
                return Status.Failure;
            }

            var status = child.Tick(context, clock);
            if (status != Status.Running)
            {
                _readyAt = clock.Now + time;
            }

            return status;
        }

        // The cooldown outlasts a reset: leaving the branch doesn't make the spell ready again.
        public override void Reset() => child.Reset();
    }

    private sealed class RepeatNode<T>(int times, Node<T> child) : Node<T>
    {
        private int _done;

        public override Status Tick(T context, Clock clock)
        {
            while (true)
            {
                var status = child.Tick(context, clock);
                if (status != Status.Success)
                {
                    if (status == Status.Failure)
                    {
                        _done = 0;
                    }

                    return status;
                }

                child.Reset();
                if (++_done >= times)
                {
                    _done = 0;
                    return Status.Success;
                }
            }
        }

        public override void Reset()
        {
            child.Reset();
            _done = 0;
        }
    }
}
