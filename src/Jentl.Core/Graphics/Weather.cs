using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Jentl.Core.Graphics;

/// <summary>What falls from the sky.</summary>
public enum Precipitation
{
    None,
    Rain,
    Snow,
}

/// <summary>
/// Rain or snow over the whole view, drawn in screen space from the clock alone (no state, nothing to update), so it
/// stays the same speed whatever the frame rate.
/// </summary>
public static class Weather
{
    /// <summary>Draws <paramref name="kind"/> over <paramref name="viewport"/> as it is <paramref name="seconds"/> in.</summary>
    public static void Draw(SpriteBatch batch, Rectangle viewport, Precipitation kind, double seconds)
    {
        ArgumentNullException.ThrowIfNull(batch);
        if (kind == Precipitation.None || viewport.Width <= 0 || viewport.Height <= 0)
        {
            return;
        }

        var snow = kind == Precipitation.Snow;
        var count = viewport.Width * viewport.Height / (snow ? 9000 : 5000);
        var (fall, drift) = snow ? (60.0, 20.0) : (540.0, 180.0);
        var color = snow ? new Color(255, 255, 255, 200) : new Color(170, 200, 240, 120);
        for (var i = 0; i < count; i++)
        {
            // Each streak starts at its own spot and moves on with the clock, wrapping round the view.
            var x = viewport.X + (int)(((i * 53) + (seconds * drift)) % viewport.Width);
            var y = viewport.Y + (int)(((i * 97) + (seconds * fall * (0.8 + (i % 5 * 0.1)))) % viewport.Height);
            batch.FillRectangle(new Rectangle(x, y, snow ? 3 : 1, snow ? 3 : 7), color);
        }
    }
}
