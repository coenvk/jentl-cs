using Microsoft.Xna.Framework;

namespace Jentl.Core.Graphics;

/// <summary>Animates a <see cref="Vector2"/> from one value to another over a fixed duration.</summary>
public sealed class Tween
{
    private readonly Func<float, float> _easing;
    private TimeSpan _elapsed;

    public Tween(Vector2 from, Vector2 to, TimeSpan duration, Func<float, float>? easing = null)
    {
        if (duration < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(duration), duration, "Duration cannot be negative.");
        }

        From = from;
        To = to;
        Duration = duration;
        _easing = easing ?? Easing.Linear;
    }

    public Vector2 From { get; }

    public Vector2 To { get; }

    public TimeSpan Duration { get; }

    public bool IsDone => _elapsed >= Duration;

    public Vector2 Value => IsDone ? To : Vector2.Lerp(From, To, _easing((float)(_elapsed / Duration)));

    /// <summary>A finished tween resting at <paramref name="value"/>.</summary>
    public static Tween At(Vector2 value) => new(value, value, TimeSpan.Zero);

    public void Update(TimeSpan elapsed) => _elapsed += elapsed;
}
