using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Jentl.Core.UI;

public enum Align
{
    Left,
    Center,
    Right,
}

public static class TextExtensions
{
    private static readonly Vector2 ShadowOffset = new(0, 3);

    /// <summary>Draws text with its top edge at <paramref name="position"/>, aligned horizontally around it.</summary>
    public static void DrawText(
        this SpriteBatch batch,
        SpriteFontBase font,
        string text,
        Vector2 position,
        Color color,
        Align align = Align.Left,
        bool shadow = true)
    {
        ArgumentNullException.ThrowIfNull(font);
        var width = font.MeasureString(text).X;
        position.X -= align switch
        {
            Align.Center => width / 2,
            Align.Right => width,
            _ => 0,
        };
        position = new Vector2(MathF.Round(position.X), MathF.Round(position.Y));

        if (shadow)
        {
            font.DrawText(batch, text, position + ShadowOffset, Palette.Shadow);
        }

        font.DrawText(batch, text, position, color);
    }

    /// <summary>Breaks <paramref name="text"/> into lines no wider than <paramref name="width"/>, at spaces.</summary>
    public static IReadOnlyList<string> Wrap(this SpriteFontBase font, string text, float width)
    {
        ArgumentNullException.ThrowIfNull(font);
        return Wrap(text, width, line => font.MeasureString(line).X);
    }

    /// <summary>
    /// Breaks <paramref name="text"/> into lines no wider than <paramref name="width"/> as <paramref name="measure"/>
    /// measures them, at spaces; a word wider than the whole width gets a line of its own.
    /// </summary>
    public static IReadOnlyList<string> Wrap(string text, float width, Func<string, float> measure)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(measure);
        var lines = new List<string>();
        var line = "";
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = line.Length == 0 ? word : $"{line} {word}";
            if (line.Length > 0 && measure(candidate) > width)
            {
                lines.Add(line);
                line = word;
            }
            else
            {
                line = candidate;
            }
        }

        if (line.Length > 0)
        {
            lines.Add(line);
        }

        return lines;
    }

    /// <summary>Draws text vertically centred in <paramref name="bounds"/>.</summary>
    public static void DrawText(
        this SpriteBatch batch,
        SpriteFontBase font,
        string text,
        Rectangle bounds,
        Color color,
        Align align = Align.Center,
        bool shadow = true)
    {
        ArgumentNullException.ThrowIfNull(font);
        var x = align switch
        {
            Align.Center => bounds.Center.X,
            Align.Right => bounds.Right,
            _ => bounds.Left,
        };
        var y = bounds.Center.Y - font.LineHeight / 2f;
        batch.DrawText(font, text, new Vector2(x, y), color, align, shadow);
    }
}
