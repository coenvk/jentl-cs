using FontStashSharp;
using Jentl.Core.Graphics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Jentl.Core.UI;

/// <summary>
/// A small panel of text beside something on screen (a card, an item, a button): below it, or above when there is no
/// room below, and always inside the screen. Drawn as the theme draws panels, without the accent bar.
/// </summary>
public static class Tooltip
{
    private const int Padding = 10;
    private const int Gap = 6;

    /// <summary>Draws <paramref name="text"/>, wrapped to <paramref name="maxWidth"/>, beside <paramref name="target"/>; returns where it went.</summary>
    public static Rectangle Draw(SpriteBatch batch, SpriteFontBase font, string text, Rectangle target, Rectangle screen, int maxWidth = 400)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(font);
        var lines = font.Wrap(text, Math.Min(maxWidth, screen.Width) - 2 * Padding);
        var lineHeight = (int)font.LineHeight;
        var width = (int)lines.Select(l => font.MeasureString(l).X).DefaultIfEmpty(0).Max() + 2 * Padding;
        var bounds = Place(new Point(width, lines.Count * lineHeight + 2 * Padding), target, screen);
        Theme.Current.Panel.Draw(batch, bounds, Palette.Panel);
        for (var i = 0; i < lines.Count; i++)
        {
            batch.DrawText(font, lines[i], new Vector2(bounds.X + Padding, bounds.Y + Padding + i * lineHeight), Palette.Text, Align.Left, shadow: false);
        }

        return bounds;
    }

    /// <summary>Where a tooltip of <paramref name="size"/> goes for <paramref name="target"/>: centred below it, above it when it would leave <paramref name="screen"/> there, kept a little clear of the screen's sides.</summary>
    public static Rectangle Place(Point size, Rectangle target, Rectangle screen)
    {
        // Clear of the screen's sides by the gap, where it is narrow enough.
        var (left, right) = size.X + (2 * Gap) <= screen.Width ? (screen.Left + Gap, screen.Right - Gap) : (screen.Left, screen.Right);
        var x = Math.Clamp(target.Center.X - size.X / 2, left, Math.Max(left, right - size.X));
        var y = target.Bottom + Gap;
        if (y + size.Y > screen.Bottom && target.Top - Gap - size.Y >= screen.Top)
        {
            y = target.Top - Gap - size.Y;
        }

        y = Math.Clamp(y, screen.Top, Math.Max(screen.Top, screen.Bottom - size.Y));
        return new Rectangle(x, y, size.X, size.Y);
    }
}
