using Microsoft.Xna.Framework;

namespace Jentl.Core.Graphics;

/// <summary>
/// A piece of animation on a timeline: a value tweened (a position, a scale, a colour, anything set through an
/// action), or motions one after another (<see cref="Sequence"/>), side by side (<see cref="Together"/>), with pauses
/// (<see cref="Wait"/>), calls at points in them (<see cref="Call"/>) and repeats, there and back (<see cref="Repeat"/>).
/// A motion keeps no clock of its own: <see cref="Advance"/> moves it from one time to another, and a
/// <see cref="MotionPlayer"/> keeps the time.
/// </summary>
/// <example>
/// <code>
/// var pop = Motion.Sequence(
///     Motion.Tween(s => scale = s, 0, 1.2f, 0.25, Easing.BackOut),
///     Motion.Together(
///         Motion.Tween(s => scale = s, 1.2f, 1, 0.1),
///         Motion.Tween(c => colour = c, Color.White, Color.Gold, 0.3)),
///     Motion.Call(() => sounds.Play("ding")),
///     Motion.Wait(1),
///     Motion.Tween(a => alpha = a, 1, 0, 0.5));
/// player.Play(pop);
/// </code>
/// </example>
public abstract class Motion
{
    /// <summary>The length of a motion that never ends.</summary>
    public static readonly TimeSpan Forever = TimeSpan.MaxValue;

    public abstract TimeSpan Duration { get; }

    /// <summary>
    /// Moves the motion from time <paramref name="fromTime"/> to <paramref name="toTime"/> (both from its own start): values
    /// are set for <paramref name="toTime"/>, and calls passed on the way forward are made.
    /// </summary>
    public abstract void Advance(TimeSpan fromTime, TimeSpan toTime);

    /// <summary>A float from one value to another over <paramref name="seconds"/>.</summary>
    public static Motion Tween(Action<float> set, float from, float to, double seconds, Func<float, float>? easing = null) =>
        new TweenMotion(t => set(MathHelper.Lerp(from, to, t)), Seconds(seconds), easing ?? Easing.Linear);

    public static Motion Tween(Action<Vector2> set, Vector2 from, Vector2 to, double seconds, Func<float, float>? easing = null) =>
        new TweenMotion(t => set(Vector2.Lerp(from, to, t)), Seconds(seconds), easing ?? Easing.Linear);

    public static Motion Tween(Action<Color> set, Color from, Color to, double seconds, Func<float, float>? easing = null) =>
        new TweenMotion(t => set(Color.Lerp(from, to, Math.Clamp(t, 0, 1))), Seconds(seconds), easing ?? Easing.Linear);

    /// <summary>Nothing for a while: a pause in a sequence.</summary>
    public static Motion Wait(double seconds) => new TweenMotion(_ => { }, Seconds(seconds), Easing.Linear);

    /// <summary>An action at this point of a sequence (a sound, a particle burst, the next scene).</summary>
    public static Motion Call(Action action) => new CallMotion(action);

    /// <summary>One after another.</summary>
    public static Motion Sequence(params Motion[] motions) => new SequenceMotion(motions);

    /// <summary>All at once; done when the longest is.</summary>
    public static Motion Together(params Motion[] motions) => new TogetherMotion(motions);

    /// <summary>
    /// <paramref name="motion"/> <paramref name="times"/> times (0 or less: forever); with <paramref name="yoyo"/>
    /// every other time runs backwards, there and back.
    /// </summary>
    public static Motion Repeat(Motion motion, int times = 0, bool yoyo = false) => new RepeatMotion(motion, times, yoyo);

    /// <summary>A sum of durations that stays at <see cref="Forever"/> rather than overflowing.</summary>
    private protected static TimeSpan Add(TimeSpan a, TimeSpan b) => a == Forever || b == Forever || a.Ticks > Forever.Ticks - b.Ticks ? Forever : a + b;

    private static TimeSpan Seconds(double seconds) => seconds >= 0 ? TimeSpan.FromSeconds(seconds) : throw new ArgumentOutOfRangeException(nameof(seconds), seconds, "A motion can't last less than nothing.");

    private static TimeSpan Clamp(TimeSpan time, TimeSpan duration) => time < TimeSpan.Zero ? TimeSpan.Zero : time > duration ? duration : time;

    private sealed class TweenMotion(Action<float> set, TimeSpan duration, Func<float, float> easing) : Motion
    {
        public override TimeSpan Duration => duration;

        public override void Advance(TimeSpan fromTime, TimeSpan toTime) =>
            Set(toTime);

        private void Set(TimeSpan to) =>
            set(duration <= TimeSpan.Zero ? 1 : to >= duration ? 1 : easing((float)(Clamp(to, duration) / duration)));
    }

    private sealed class CallMotion(Action action) : Motion
    {
        public override TimeSpan Duration => TimeSpan.Zero;

        public override void Advance(TimeSpan fromTime, TimeSpan toTime)
        {
            var (from, to) = (fromTime, toTime);
            // Only when crossing its moment: a time before the start is anything below zero.
            if (from < TimeSpan.Zero && to >= TimeSpan.Zero)
            {
                action();
            }
        }
    }

    private sealed class SequenceMotion(Motion[] motions) : Motion
    {
        public override TimeSpan Duration { get; } = motions.Aggregate(TimeSpan.Zero, (sum, m) => Add(sum, m.Duration));

        public override void Advance(TimeSpan fromTime, TimeSpan toTime)
        {
            var (from, to) = (fromTime, toTime);
            var start = TimeSpan.Zero;
            foreach (var motion in motions)
            {
                var end = Add(start, motion.Duration);

                // A part is moved when the span reaches into it, or passes over it (so it ends where it should).
                var touched = from < to
                    ? to >= start && from <= end
                    : from >= start && to <= end;
                if (touched)
                {
                    motion.Advance(Local(from, start, motion.Duration), Local(to, start, motion.Duration));
                }

                if (end == Forever)
                {
                    break;
                }

                start = end;
            }
        }

        private static TimeSpan Local(TimeSpan time, TimeSpan start, TimeSpan duration) =>
            time < start ? TimeSpan.Zero - TimeSpan.FromTicks(1) : Clamp(time - start, duration);
    }

    private sealed class TogetherMotion(Motion[] motions) : Motion
    {
        public override TimeSpan Duration { get; } = motions.Length == 0 ? TimeSpan.Zero : motions.Max(m => m.Duration);

        public override void Advance(TimeSpan fromTime, TimeSpan toTime)
        {
            var (from, to) = (fromTime, toTime);
            foreach (var motion in motions)
            {
                motion.Advance(Clamp(from, motion.Duration), Clamp(to, motion.Duration));
            }
        }
    }

    private sealed class RepeatMotion(Motion motion, int times, bool yoyo) : Motion
    {
        public override TimeSpan Duration => times <= 0 || motion.Duration <= TimeSpan.Zero
            ? (motion.Duration <= TimeSpan.Zero ? TimeSpan.Zero : Forever)
            : TimeSpan.FromTicks(motion.Duration.Ticks * Math.Min(times, long.MaxValue / Math.Max(1, motion.Duration.Ticks)));

        public override void Advance(TimeSpan fromTime, TimeSpan toTime)
        {
            var (from, to) = (fromTime, toTime);
            var length = motion.Duration;
            if (length <= TimeSpan.Zero)
            {
                motion.Advance(from, to);
                return;
            }

            to = Clamp(to, Duration);
            var first = from < TimeSpan.Zero ? 0 : Clamp(from, Duration).Ticks / length.Ticks;
            var passes = Duration == Forever ? long.MaxValue : Duration.Ticks / length.Ticks;
            var last = Math.Min(Math.Max(first, to.Ticks / length.Ticks), passes - 1);

            // Every pass the span crosses, each from where the span enters it to where it leaves (a span ending on
            // the start of a pass enters it, so a call there is made in that frame and not again in the next).
            for (var pass = first; pass <= last && pass - first < 10_000; pass++)
            {
                var passStart = pass * length.Ticks;
                var entered = from < TimeSpan.Zero || from.Ticks < passStart;
                var a = entered ? TimeSpan.FromTicks(-1) : TimeSpan.FromTicks(Math.Clamp(from.Ticks - passStart, 0, length.Ticks));
                var b = TimeSpan.FromTicks(Math.Clamp(to.Ticks - passStart, 0, length.Ticks));
                if (yoyo && pass % 2 == 1)
                {
                    motion.Advance(length - Clamp(a, length), length - b);
                }
                else
                {
                    motion.Advance(a, b);
                }
            }
        }
    }
}

/// <summary>Keeps the time of one <see cref="Motion"/>: <see cref="Update"/> each frame moves it on.</summary>
public sealed class MotionPlayer
{
    private TimeSpan _time;

    public Motion? Current { get; private set; }

    /// <summary>How fast time runs for the motion: 2 is double speed.</summary>
    public float Speed { get; set; } = 1;

    public bool IsDone => Current is null || (Current.Duration != Motion.Forever && _time >= Current.Duration);

    /// <summary>Starts <paramref name="motion"/> from its beginning, setting its first values at once.</summary>
    public void Play(Motion motion)
    {
        Current = motion ?? throw new ArgumentNullException(nameof(motion));
        _time = TimeSpan.Zero;
        motion.Advance(TimeSpan.Zero - TimeSpan.FromTicks(1), TimeSpan.Zero);
    }

    public void Stop() => Current = null;

    public void Update(TimeSpan elapsed)
    {
        if (Current is null || IsDone)
        {
            return;
        }

        var before = _time;
        _time += TimeSpan.FromTicks((long)(elapsed.Ticks * (double)Speed));
        Current.Advance(before, _time);
    }
}

/// <summary>Many motions at once (a menu's items flying in, cards dealt): each runs until done and is then dropped.</summary>
public sealed class Motions
{
    private readonly List<MotionPlayer> _players = [];

    public int Count => _players.Count;

    public void Play(Motion motion)
    {
        var player = new MotionPlayer();
        player.Play(motion);
        _players.Add(player);
    }

    public void Clear() => _players.Clear();

    public void Update(TimeSpan elapsed)
    {
        foreach (var player in _players.ToList())
        {
            player.Update(elapsed);
        }

        _players.RemoveAll(p => p.IsDone);
    }
}
