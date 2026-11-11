using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Jentl.Core.Graphics;

public enum AnimationMode
{
    /// <summary>Back to the first frame after the last, forever.</summary>
    Loop,

    /// <summary>Stops on the last frame.</summary>
    Once,

    /// <summary>Forwards then backwards, forever (1 2 3 2 1 2 ...).</summary>
    PingPong,
}

/// <summary>
/// Frames shown one after another: frame numbers (into a <see cref="SpriteSheet"/>, a tileset...) each held for
/// its duration. <see cref="FrameAt"/> answers which frame shows at a given time, so an animation needs no
/// state of its own and many things can share one.
/// </summary>
public sealed record Animation(IReadOnlyList<int> Frames, IReadOnlyList<TimeSpan> Durations, AnimationMode Mode = AnimationMode.Loop)
{
    /// <summary>Frames that each last <paramref name="frameTime"/>.</summary>
    public Animation(TimeSpan frameTime, AnimationMode mode, params int[] frames)
        : this(frames, [.. frames.Select(_ => frameTime)], mode)
    {
    }

    /// <summary>One pass through the frames (forwards and back again for ping-pong).</summary>
    public TimeSpan Length => Order.Aggregate(TimeSpan.Zero, (sum, i) => sum + Durations[i]);

    private IReadOnlyList<int> Order => Mode == AnimationMode.PingPong && Frames.Count > 2
        ? [.. Enumerable.Range(0, Frames.Count), .. Enumerable.Range(1, Frames.Count - 2).Reverse()]
        : [.. Enumerable.Range(0, Frames.Count)];

    /// <summary>The frame showing <paramref name="time"/> after the animation started.</summary>
    public int FrameAt(TimeSpan time)
    {
        if (Frames.Count == 0)
        {
            throw new InvalidOperationException("An animation needs frames.");
        }

        var length = Length;
        if (length <= TimeSpan.Zero)
        {
            return Frames[0];
        }

        if (Mode == AnimationMode.Once && time >= length)
        {
            return Frames[^1];
        }

        var into = TimeSpan.FromTicks(Math.Max(0, time.Ticks) % length.Ticks);
        foreach (var index in Order)
        {
            if (into < Durations[index])
            {
                return Frames[index];
            }

            into -= Durations[index];
        }

        return Frames[^1];
    }

    /// <summary>Whether a <see cref="AnimationMode.Once"/> animation has reached its end by <paramref name="time"/>.</summary>
    public bool IsDone(TimeSpan time) => Mode == AnimationMode.Once && time >= Length;

    /// <summary>
    /// Names fired when a frame comes up, by the frame's place in <see cref="Frames"/> (not its number): a footstep on
    /// the third frame of a walk, a hit box on the fifth of a swing.
    /// </summary>
    public IReadOnlyDictionary<int, string>? Events { get; init; }

    /// <summary>The events of the frames that come up after <paramref name="from"/> and by <paramref name="to"/>, in order; a time below zero is before the first frame.</summary>
    public IEnumerable<string> EventsBetween(TimeSpan from, TimeSpan to)
    {
        if (Events is not { Count: > 0 } events || to < from || Frames.Count == 0)
        {
            yield break;
        }

        var order = Order;
        var length = Length;
        var pass = length > TimeSpan.Zero && from > TimeSpan.Zero ? from.Ticks / length.Ticks : 0;
        if (Mode == AnimationMode.Once && pass > 0)
        {
            yield break;
        }

        var start = TimeSpan.FromTicks(pass * length.Ticks);
        for (var guard = 0; guard < 100_000; pass++)
        {
            foreach (var index in order)
            {
                if (start > to)
                {
                    yield break;
                }

                if (start > from && events.TryGetValue(index, out var name))
                {
                    yield return name;
                }

                start += Durations[index];
                guard++;
            }

            if (Mode == AnimationMode.Once || length <= TimeSpan.Zero)
            {
                yield break;
            }
        }
    }
}

/// <summary>Plays one <see cref="Animation"/> at a time and keeps the clock, for a sprite that switches between animations (idle, walk, attack).</summary>
public sealed class Animator
{
    private TimeSpan _time;
    private bool _starting;

    /// <summary>Raised with a frame's event name (<see cref="Animation.Events"/>) as the frame comes up.</summary>
    public event Action<string>? FrameEvent;

    public Animation? Current { get; private set; }

    /// <summary>The frame to draw now; -1 before anything plays.</summary>
    public int Frame => Current?.FrameAt(_time) ?? -1;

    public bool IsDone => Current?.IsDone(_time) ?? true;

    /// <summary>Starts <paramref name="animation"/>; asking for the one already playing carries on, unless <paramref name="restart"/>.</summary>
    public void Play(Animation animation, bool restart = false)
    {
        ArgumentNullException.ThrowIfNull(animation);
        if (restart || animation != Current)
        {
            Current = animation;
            _time = TimeSpan.Zero;
            _starting = true;
        }
    }

    public void Update(TimeSpan elapsed)
    {
        var before = _starting ? TimeSpan.FromTicks(-1) : _time;
        _starting = false;
        _time += elapsed;
        if (FrameEvent is { } raise && Current is { } animation)
        {
            foreach (var name in animation.EventsBetween(before, _time).ToList())
            {
                raise(name);
            }
        }
    }

    /// <summary>How long the current animation has played.</summary>
    public TimeSpan Time => _time;
}

/// <summary>A texture cut into a grid of equally sized frames, numbered left to right, top to bottom.</summary>
public sealed class SpriteSheet(Texture2D texture, int frameWidth, int frameHeight)
{
    public Texture2D Texture { get; } = texture ?? throw new ArgumentNullException(nameof(texture));

    public int FrameWidth { get; } = frameWidth;

    public int FrameHeight { get; } = frameHeight;

    public int Columns => Math.Max(1, Texture.Width / FrameWidth);

    public Rectangle Source(int frame) => new(frame % Columns * FrameWidth, frame / Columns * FrameHeight, FrameWidth, FrameHeight);

    public void Draw(SpriteBatch batch, int frame, Rectangle destination, Color color, SpriteEffects effects = SpriteEffects.None)
    {
        ArgumentNullException.ThrowIfNull(batch);
        batch.Draw(Texture, destination, Source(frame), color, 0, Vector2.Zero, effects, 0);
    }
}
