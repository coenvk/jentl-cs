namespace Jentl.Core.Timing;

/// <summary>
/// A routine running over many frames: an iterator method that <c>yield return</c>s <see cref="Wait"/>s. Started by
/// a <see cref="Scheduler"/>, which advances it every update until it ends or is cancelled.
/// </summary>
public sealed class Coroutine
{
    private readonly Stack<IEnumerator<Wait?>> _stack = new();
    private Wait? _waiting;
    private TimeSpan _timeLeft;
    private int _framesLeft;
    private bool _stepping;

    internal Coroutine(IEnumerable<Wait?> routine) => _stack.Push(routine.GetEnumerator());

    /// <summary>The routine has ended, or was cancelled.</summary>
    public bool IsDone { get; private set; }

    /// <summary>A paused coroutine keeps its place but doesn't advance, nor does its waiting time pass.</summary>
    public bool IsPaused { get; set; }

    /// <summary>Stops the routine where it is; it never resumes. Its iterators' <c>finally</c> blocks run.</summary>
    public void Cancel()
    {
        if (IsDone)
        {
            return;
        }

        IsDone = true;

        // Cancelled from inside its own routine: the iterator is still running, so let go of it once it returns.
        if (!_stepping)
        {
            Release();
        }
    }

    /// <summary>Runs until the routine waits for something that hasn't happened yet, spending <paramref name="elapsed"/>.</summary>
    internal void Step(TimeSpan elapsed)
    {
        if (IsDone || IsPaused)
        {
            return;
        }

        _stepping = true;
        try
        {
            Run(elapsed);
        }
        catch
        {
            IsDone = true;
            throw;
        }
        finally
        {
            _stepping = false;
            if (IsDone)
            {
                Release();
            }
        }
    }

    private void Run(TimeSpan budget)
    {
        while (!IsDone)
        {
            switch (_waiting?.Kind)
            {
                case WaitKind.Time:
                    if (_timeLeft > budget)
                    {
                        _timeLeft -= budget;
                        return;
                    }

                    // The rest of this frame's time goes to whatever comes next.
                    budget -= _timeLeft;
                    break;
                case WaitKind.Frames:
                    if (--_framesLeft > 0)
                    {
                        return;
                    }

                    budget = TimeSpan.Zero;
                    break;
                case WaitKind.Until:
                    if (!_waiting.Condition!())
                    {
                        return;
                    }

                    budget = TimeSpan.Zero;
                    break;
            }

            _waiting = null;
            if (!Advance())
            {
                return;
            }
        }
    }

    private void Release()
    {
        while (_stack.Count > 0)
        {
            _stack.Pop().Dispose();
        }
    }

    /// <summary>Moves the routine on to its next wait; false when it has ended, or a frame wait began.</summary>
    private bool Advance()
    {
        while (true)
        {
            var top = _stack.Peek();
            var more = top.MoveNext();
            if (IsDone)
            {
                return false;
            }

            if (!more)
            {
                _stack.Pop().Dispose();
                if (_stack.Count == 0)
                {
                    IsDone = true;
                    return false;
                }

                continue;
            }

            var wait = top.Current ?? Wait.NextFrame;
            switch (wait.Kind)
            {
                case WaitKind.Routine:
                    _stack.Push(wait.Routine!.GetEnumerator());
                    continue;
                case WaitKind.Frames:
                    // Counted down from the next update on.
                    _waiting = wait;
                    _framesLeft = wait.FrameCount + 1;
                    return true;
                case WaitKind.Time:
                    _waiting = wait;
                    _timeLeft = wait.Time;
                    return true;
                default:
                    _waiting = wait;
                    return true;
            }
        }
    }
}

/// <summary>
/// Runs coroutines and timers. <see cref="Update"/> it once per frame (scenes and the scene manager have one,
/// <c>Timers</c>, that is updated for you).
/// </summary>
/// <example>
/// <code>
/// timers.After(TimeSpan.FromSeconds(2), () => banner.Hide());
/// timers.Start(Intro());
///
/// IEnumerable&lt;Wait?&gt; Intro()
/// {
///     title.FadeIn();
///     yield return Wait.Seconds(1);
///     yield return Wait.Until(() => input.Tap is not null);
///     scenes.Replace(new PlayScene());
/// }
/// </code>
/// </example>
public sealed class Scheduler
{
    private readonly List<Coroutine> _running = [];

    /// <summary>How fast time passes for these coroutines: 0.5 for slow motion, 0 to stop the clock (frame waits still count).</summary>
    public double TimeScale { get; set; } = 1;

    /// <summary>How many coroutines are still running.</summary>
    public int Count => _running.Count(c => !c.IsDone);

    /// <summary>
    /// Starts <paramref name="routine"/>: it runs right away up to its first wait, and from then on in each
    /// <see cref="Update"/>.
    /// </summary>
    public Coroutine Start(IEnumerable<Wait?> routine)
    {
        ArgumentNullException.ThrowIfNull(routine);
        var coroutine = new Coroutine(routine);
        coroutine.Step(TimeSpan.Zero);
        if (!coroutine.IsDone)
        {
            _running.Add(coroutine);
        }

        return coroutine;
    }

    /// <summary>Calls <paramref name="action"/> once, <paramref name="delay"/> from now. Cancel the result to call it off.</summary>
    public Coroutine After(TimeSpan delay, Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return Start(AfterRoutine(delay, action));
    }

    /// <summary>
    /// Calls <paramref name="action"/> every <paramref name="interval"/>, <paramref name="times"/> times or until
    /// cancelled. A long frame catches up on every call it spanned.
    /// </summary>
    public Coroutine Every(TimeSpan interval, Action action, int? times = null)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(interval, TimeSpan.Zero);
        return Start(EveryRoutine(interval, action, times));
    }

    /// <summary>Advances every coroutine by one frame of <paramref name="elapsed"/> (times <see cref="TimeScale"/>).</summary>
    public void Update(TimeSpan elapsed)
    {
        var scaled = elapsed * TimeScale;

        // Coroutines started during the update have already run their first stretch; they wait for the next one.
        var count = _running.Count;
        for (var i = 0; i < count; i++)
        {
            _running[i].Step(scaled);
        }

        _running.RemoveAll(c => c.IsDone);
    }

    /// <summary>Cancels every coroutine.</summary>
    public void Clear()
    {
        foreach (var coroutine in _running)
        {
            coroutine.Cancel();
        }

        _running.Clear();
    }

    private static IEnumerable<Wait?> AfterRoutine(TimeSpan delay, Action action)
    {
        yield return Wait.For(delay);
        action();
    }

    private static IEnumerable<Wait?> EveryRoutine(TimeSpan interval, Action action, int? times)
    {
        for (var i = 0; times is null || i < times; i++)
        {
            yield return Wait.For(interval);
            action();
        }
    }
}
