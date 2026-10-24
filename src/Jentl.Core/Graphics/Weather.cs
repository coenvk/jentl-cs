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
/// stays the same speed whatever the frame rate. Wind slants the rain and blows it sideways; rain also splashes on
/// the ground.
/// </summary>
public static class Weather
{
    /// <summary>Draws <paramref name="kind"/> over <paramref name="viewport"/> as it is <paramref name="seconds"/> in.</summary>
    /// <param name="batch">Where to draw.</param>
    /// <param name="viewport">The area it falls over.</param>
    /// <param name="kind">Rain, snow or nothing.</param>
    /// <param name="seconds">The clock: the same time draws the same picture.</param>
    /// <param name="wind">Sideways wind, -1 (hard from the right) to 1 (hard from the left); 0 falls straight down.</param>
    public static void Draw(SpriteBatch batch, Rectangle viewport, Precipitation kind, double seconds, float wind = 0)
    {
        ArgumentNullException.ThrowIfNull(batch);
        if (kind == Precipitation.None || viewport.Width <= 0 || viewport.Height <= 0)
        {
            return;
        }

        wind = Math.Clamp(wind, -1, 1);
        if (kind == Precipitation.Snow)
        {
            DrawSnow(batch, viewport, seconds, wind);
        }
        else
        {
            DrawRain(batch, viewport, seconds, wind);
        }
    }

    /// <summary>A gusting wind for <see cref="Draw"/>: around <paramref name="strength"/>, rising and falling with the clock.</summary>
    public static float Gust(double seconds, float strength) =>
        strength * (float)(0.75 + (0.25 * Math.Sin(seconds * 0.9)) + (0.1 * Math.Sin(seconds * 2.3)));

    private static void DrawRain(SpriteBatch batch, Rectangle viewport, double seconds, float wind)
    {
        var count = viewport.Width * viewport.Height / 2600;
        var scale = Math.Max(1, viewport.Height / 360f);
        const double fall = 620;
        var sideways = 260 * wind;

        // Streaks lean with the wind; each drop has its own speed, so the rain doesn't move as one sheet.
        var length = 10 * scale;
        var slant = new Vector2(sideways / (float)fall, 1);
        slant.Normalize();
        for (var i = 0; i < count; i++)
        {
            var speed = 0.75 + (i % 7 * 0.06);
            var x = Wrap((i * 53.7) + (seconds * sideways * speed), viewport.Width);
            var y = Wrap((i * 97.3) + (seconds * fall * speed * scale), viewport.Height);
            var from = new Vector2(viewport.X + (float)x, viewport.Y + (float)y);
            var color = i % 3 == 0 ? new Color(200, 220, 250, 150) : new Color(160, 190, 235, 110);
            batch.DrawLine(from, from + (slant * length), color, Math.Max(1, scale * 0.6f));
        }

        // Splashes: little rings that open and fade where drops land.
        var splashes = viewport.Width * viewport.Height / 14000;
        for (var i = 0; i < splashes; i++)
        {
            var life = ((seconds * 2.2) + (i * 0.37)) % 1.0;
            var cycle = Math.Floor((seconds * 2.2) + (i * 0.37));
            var x = Wrap((i * 211.0) + (cycle * 137.0), viewport.Width);
            var y = Wrap((i * 173.0) + (cycle * 251.0), viewport.Height);
            var radius = (int)Math.Round((1 + (life * 3)) * scale);
            var alpha = (byte)(140 * (1 - life));
            var centre = new Point(viewport.X + (int)x, viewport.Y + (int)y);
            var ring = new Color((byte)210, (byte)225, (byte)250, alpha);
            batch.FillRectangle(new Rectangle(centre.X - radius, centre.Y, (radius * 2) + 1, 1), ring);
            batch.FillRectangle(new Rectangle(centre.X - (radius / 2), centre.Y - Math.Max(1, radius / 2), radius + 1, 1), ring * 0.6f);
        }
    }

    private static void DrawSnow(SpriteBatch batch, Rectangle viewport, double seconds, float wind)
    {
        var count = viewport.Width * viewport.Height / 7000;
        var scale = Math.Max(1, viewport.Height / 360f);
        for (var i = 0; i < count; i++)
        {
            // Flakes drift with the wind and sway as they fall.
            var speed = 0.6 + (i % 5 * 0.1);
            var sway = Math.Sin((seconds * 1.7) + i) * 12 * scale;
            var x = Wrap((i * 53.7) + (seconds * 90 * wind * speed) + sway, viewport.Width);
            var y = Wrap((i * 97.3) + (seconds * 70 * speed * scale), viewport.Height);
            var size = (int)Math.Round((i % 4 == 0 ? 3 : 2) * scale);
            batch.FillRectangle(new Rectangle(viewport.X + (int)x, viewport.Y + (int)y, size, size), new Color(255, 255, 255, 210));
        }
    }

    private static double Wrap(double value, int size) => ((value % size) + size) % size;
}
