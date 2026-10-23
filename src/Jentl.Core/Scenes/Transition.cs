using Jentl.Core.Graphics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Jentl.Core.Scenes;

/// <summary>
/// A visual cover for a scene change. Coverage rises from 0 to 1 over the first half of
/// <see cref="Duration"/>, the change is applied at full coverage, then coverage falls back to 0.
/// </summary>
public abstract class Transition
{
    protected Transition(TimeSpan duration, Func<float, float>? easing = null)
    {
        if (duration < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(duration), duration, "Duration cannot be negative.");
        }

        Duration = duration;
        Easing = easing ?? Graphics.Easing.QuadInOut;
    }

    public TimeSpan Duration { get; }

    public Func<float, float> Easing { get; }

    /// <summary>Draws the cover on top of the scenes; <paramref name="coverage"/> is already eased.</summary>
    public abstract void Draw(SpriteBatch batch, Rectangle viewport, float coverage);
}

/// <summary>Fades to a colour and back.</summary>
public sealed class FadeTransition(TimeSpan duration, Color? color = null) : Transition(duration)
{
    private readonly Color _color = color ?? Color.Black;

    public override void Draw(SpriteBatch batch, Rectangle viewport, float coverage) =>
        batch.FillRectangle(viewport, new Color(_color, coverage));
}

/// <summary>A curtain sliding in from the left and back out, after Jentl's original SceneTransition.</summary>
public sealed class WipeTransition(TimeSpan duration, Color? color = null) : Transition(duration)
{
    private readonly Color _color = color ?? Color.Black;

    public override void Draw(SpriteBatch batch, Rectangle viewport, float coverage) =>
        batch.FillRectangle(
            new Rectangle(viewport.X, viewport.Y, (int)MathF.Ceiling(viewport.Width * coverage), viewport.Height),
            _color);
}
