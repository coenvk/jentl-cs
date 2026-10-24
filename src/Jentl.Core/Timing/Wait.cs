using Jentl.Core.Graphics;

namespace Jentl.Core.Timing;

/// <summary>
/// What a coroutine waits for before it carries on: <c>yield return Wait.Seconds(0.5)</c>. Yielding
/// <see langword="null"/> waits one frame, like <see cref="NextFrame"/>.
/// </summary>
public sealed class Wait
{
    private Wait(WaitKind kind)
    {
        Kind = kind;
    }

    /// <summary>Carries on in the next update.</summary>
    public static Wait NextFrame { get; } = new(WaitKind.Frames) { FrameCount = 1 };

    internal WaitKind Kind { get; }

    internal TimeSpan Time { get; private init; }

    internal int FrameCount { get; private init; }

    internal Func<bool>? Condition { get; private init; }

    internal IEnumerable<Wait?>? Routine { get; private init; }

    /// <summary>
    /// Waits this long, in scaled scheduler time. Time left over from the frame the wait ends in carries into what
    /// follows, so a run of waits stays exact however the frames fall. A zero or negative wait lasts one frame.
    /// </summary>
    public static Wait For(TimeSpan time) =>
        time > TimeSpan.Zero ? new Wait(WaitKind.Time) { Time = time } : NextFrame;

    public static Wait Seconds(double seconds) => For(TimeSpan.FromSeconds(seconds));

    /// <summary>Carries on after <paramref name="count"/> updates.</summary>
    public static Wait Frames(int count) => new(WaitKind.Frames) { FrameCount = Math.Max(1, count) };

    /// <summary>Carries on as soon as <paramref name="condition"/> holds, checked now and then once per update.</summary>
    public static Wait Until(Func<bool> condition)
    {
        ArgumentNullException.ThrowIfNull(condition);
        return new Wait(WaitKind.Until) { Condition = condition };
    }

    /// <summary>Carries on once <paramref name="condition"/> stops holding.</summary>
    public static Wait While(Func<bool> condition)
    {
        ArgumentNullException.ThrowIfNull(condition);
        return Until(() => !condition());
    }

    /// <summary>Waits for another, separately started coroutine to finish (or be cancelled).</summary>
    public static Wait For(Coroutine coroutine)
    {
        ArgumentNullException.ThrowIfNull(coroutine);
        return Until(() => coroutine.IsDone);
    }

    /// <summary>Waits for a tween to reach its end (the tween must be updated elsewhere).</summary>
    public static Wait For(Tween tween)
    {
        ArgumentNullException.ThrowIfNull(tween);
        return Until(() => tween.IsDone);
    }

    /// <summary>Runs <paramref name="routine"/> as a step of this coroutine, carrying on when it ends.</summary>
    public static Wait For(IEnumerable<Wait?> routine)
    {
        ArgumentNullException.ThrowIfNull(routine);
        return new Wait(WaitKind.Routine) { Routine = routine };
    }
}

internal enum WaitKind
{
    Frames,
    Time,
    Until,
    Routine,
}
