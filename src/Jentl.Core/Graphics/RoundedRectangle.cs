using System.Runtime.CompilerServices;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Jentl.Core.Graphics;

/// <summary>
/// Rectangles with round corners, drawn from pieces: a body, two sides, and four corners cut from one soft-edged
/// quarter circle per radius. <see cref="Pieces"/> and <see cref="Coverage"/> are the geometry, for tests.
/// </summary>
public static class RoundedRectangle
{
    private static readonly ConditionalWeakTable<GraphicsDevice, Dictionary<int, Texture2D>> Corners = new();

    /// <summary>Which corner a piece is, or <see cref="None"/> for a plain rectangle.</summary>
    public enum Corner
    {
        None,
        TopLeft,
        TopRight,
        BottomLeft,
        BottomRight,
    }

    /// <summary>One piece of a rounded rectangle: an area, and the corner it rounds (if any).</summary>
    public readonly record struct Piece(Rectangle Area, Corner Corner);

    /// <summary>
    /// The pieces that tile <paramref name="bounds"/> with corners of <paramref name="radius"/> pixels, kept within half
    /// the shorter side; a radius of 0 gives the rectangle itself.
    /// </summary>
    public static IReadOnlyList<Piece> Pieces(Rectangle bounds, int radius)
    {
        var r = Math.Clamp(radius, 0, Math.Min(bounds.Width, bounds.Height) / 2);
        if (r == 0)
        {
            return [new Piece(bounds, Corner.None)];
        }

        var (left, top, right, bottom) = (bounds.Left, bounds.Top, bounds.Right, bounds.Bottom);
        return
        [
            new(new Rectangle(left + r, top, bounds.Width - 2 * r, bounds.Height), Corner.None),
            new(new Rectangle(left, top + r, r, bounds.Height - 2 * r), Corner.None),
            new(new Rectangle(right - r, top + r, r, bounds.Height - 2 * r), Corner.None),
            new(new Rectangle(left, top, r, r), Corner.TopLeft),
            new(new Rectangle(right - r, top, r, r), Corner.TopRight),
            new(new Rectangle(left, bottom - r, r, r), Corner.BottomLeft),
            new(new Rectangle(right - r, bottom - r, r, r), Corner.BottomRight),
        ];
    }

    /// <summary>
    /// How much of pixel (<paramref name="x"/>, <paramref name="y"/>) of a top-left corner tile of
    /// <paramref name="radius"/> pixels lies inside the rounding, from 0 to 1 (soft over one pixel).
    /// </summary>
    public static float Coverage(int x, int y, int radius)
    {
        var dx = radius - (x + 0.5f);
        var dy = radius - (y + 0.5f);
        return Math.Clamp(radius - MathF.Sqrt(dx * dx + dy * dy) + 0.5f, 0, 1);
    }

    /// <summary>Fills <paramref name="bounds"/> with corners of <paramref name="radius"/> pixels.</summary>
    public static void FillRoundedRectangle(this SpriteBatch batch, Rectangle bounds, int radius, Color color)
    {
        ArgumentNullException.ThrowIfNull(batch);
        foreach (var piece in Pieces(bounds, radius))
        {
            if (piece.Area.Width <= 0 || piece.Area.Height <= 0)
            {
                continue;
            }

            if (piece.Corner == Corner.None)
            {
                batch.FillRectangle(piece.Area, color);
                continue;
            }

            var effects = piece.Corner switch
            {
                Corner.TopRight => SpriteEffects.FlipHorizontally,
                Corner.BottomLeft => SpriteEffects.FlipVertically,
                Corner.BottomRight => SpriteEffects.FlipHorizontally | SpriteEffects.FlipVertically,
                _ => SpriteEffects.None,
            };
            batch.Draw(CornerTexture(batch.GraphicsDevice, piece.Area.Width), piece.Area, null, color, 0, Vector2.Zero, effects, 0);
        }
    }

    private static Texture2D CornerTexture(GraphicsDevice device, int radius)
    {
        var cache = Corners.GetValue(device, static _ => []);
        lock (cache)
        {
            if (cache.TryGetValue(radius, out var texture))
            {
                return texture;
            }

            var pixels = new Color[radius * radius];
            for (var y = 0; y < radius; y++)
            {
                for (var x = 0; x < radius; x++)
                {
                    pixels[y * radius + x] = new Color(Color.White, Coverage(x, y, radius));
                }
            }

            texture = new Texture2D(device, radius, radius);
            texture.SetData(pixels);
            cache[radius] = texture;
            return texture;
        }
    }
}
