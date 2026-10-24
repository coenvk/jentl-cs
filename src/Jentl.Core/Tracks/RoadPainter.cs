using Jentl.Tracks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Jentl.Core.Tracks;

/// <summary>How <see cref="RoadPainter"/> paints a road.</summary>
/// <param name="Ground">Everything that isn't road.</param>
/// <param name="Verge">The strip alongside the road (gravel, grass, a pavement).</param>
/// <param name="Surface">The road itself.</param>
/// <param name="EdgeA">Kerb colour one.</param>
/// <param name="EdgeB">Kerb colour two, alternating with <paramref name="EdgeA"/>.</param>
/// <param name="CentreLine">The dashed line down the middle.</param>
/// <param name="Width">The road's width in pixels.</param>
/// <param name="VergeWidth">The verge's width on each side.</param>
/// <param name="KerbLength">The length of each kerb stripe.</param>
public sealed record RoadStyle(
    Color Ground, Color Verge, Color Surface, Color EdgeA, Color EdgeB, Color CentreLine, int Width, int VergeWidth, int KerbLength);

/// <summary>
/// Paints a <see cref="TrackPath"/> as a road seen from above: verge, surface, striped kerbs in the corners and a
/// dashed centre line, one world unit per pixel. Scenery around it is up to the game.
/// </summary>
public static class RoadPainter
{
    // Corners tighter than this radius get kerbs.
    private const double KerbRadius = 60;
    private const int KerbWidth = 3;
    private const int DashLength = 8;

    /// <summary>Paints the road into a pixel array covering <paramref name="worldBounds"/> (row by row).</summary>
    /// <param name="path">The road's centre line.</param>
    /// <param name="style">Colours and sizes.</param>
    /// <param name="padding">Ground to leave around the road on every side.</param>
    /// <param name="worldBounds">The world area the pixels cover; pixel (0, 0) is its top-left.</param>
    public static Color[] Paint(TrackPath path, RoadStyle style, int padding, out Rectangle worldBounds)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(style);
        var margin = (style.Width / 2) + style.VergeWidth + padding;
        var (min, max) = path.Bounds;
        var left = (int)MathF.Floor(min.X) - margin;
        var top = (int)MathF.Floor(min.Y) - margin;
        worldBounds = new Rectangle(left, top, (int)MathF.Ceiling(max.X) + margin - left + 1, (int)MathF.Ceiling(max.Y) + margin - top + 1);
        var bounds = worldBounds;
        var pixels = new Color[bounds.Width * bounds.Height];
        Array.Fill(pixels, style.Ground);

        void Disc(System.Numerics.Vector2 centre, float radius, Color color)
        {
            var r = (int)MathF.Ceiling(radius);
            var (cx, cy) = ((int)MathF.Round(centre.X) - bounds.X, (int)MathF.Round(centre.Y) - bounds.Y);
            for (var y = Math.Max(0, cy - r); y <= Math.Min(bounds.Height - 1, cy + r); y++)
            {
                for (var x = Math.Max(0, cx - r); x <= Math.Min(bounds.Width - 1, cx + r); x++)
                {
                    if (((x - cx) * (x - cx)) + ((y - cy) * (y - cy)) <= radius * radius)
                    {
                        pixels[(y * bounds.Width) + x] = color;
                    }
                }
            }
        }

        var half = style.Width / 2f;
        var steps = (int)Math.Ceiling(path.Length);
        bool InCorner(double d) => Math.Abs(path.CurvatureAt(d)) > 1 / KerbRadius;

        // Verge, then the road with kerbs where it bends, then the inside of the road over the kerbs' inner half.
        // Discs overlap a lot, so a stamp every few units (a quarter of the road's half-width) covers the band.
        var stride = Math.Max(1, (int)(half / 4));
        for (var i = 0; i <= steps; i += stride)
        {
            Disc(path.At(i).Position, half + style.VergeWidth, style.Verge);
        }

        for (var i = 0; i <= steps; i += stride)
        {
            var kerb = (i / Math.Max(1, style.KerbLength)) % 2 == 0 ? style.EdgeA : style.EdgeB;
            Disc(path.At(i).Position, half, InCorner(i) ? kerb : style.Surface);
        }

        for (var i = 0; i <= steps; i += stride)
        {
            if (InCorner(i))
            {
                Disc(path.At(i).Position, half - KerbWidth, style.Surface);
            }
        }

        for (var i = 0; i <= steps; i++)
        {
            if (i % (2 * DashLength) < DashLength)
            {
                var p = path.At(i).Position;
                var (x, y) = ((int)MathF.Round(p.X) - bounds.X, (int)MathF.Round(p.Y) - bounds.Y);
                pixels[(y * bounds.Width) + x] = style.CentreLine;
            }
        }

        return pixels;
    }

    /// <summary>Puts painted pixels on the graphics card.</summary>
    public static Texture2D ToTexture(GraphicsDevice device, Color[] pixels, Rectangle worldBounds)
    {
        var texture = new Texture2D(device, worldBounds.Width, worldBounds.Height);
        texture.SetData(pixels);
        return texture;
    }
}
