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
}

/// <summary>Plays one <see cref="Animation"/> at a time and keeps the clock, for a sprite that switches between animations (idle, walk, attack).</summary>
public sealed class Animator
{
    private TimeSpan _time;

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
        }
    }

    public void Update(TimeSpan elapsed) => _time += elapsed;
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
