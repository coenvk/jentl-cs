using FontStashSharp.Interfaces;
using Jentl.Cards;
using Jentl.Core.Cards;

namespace Jentl.Core.UI;

/// <summary>
/// A font of the four card suits, ♠ ♥ ♦ ♣ (drawn from <see cref="SuitShape"/>), and a few symbols games use: the stars
/// ★ ☆ and the arrowheads ▲ ▼ ◀ ▶. <see cref="Fonts"/> adds it behind the game's own font, so text can show them
/// whatever font the game uses (few display fonts have them).
/// </summary>
public sealed class SuitGlyphSource : IFontSource
{
    private const int Samples = 4;

    // The suits and symbols by glyph id; a symbol has no suit.
    private static readonly (char Symbol, Suit? Suit)[] Glyphs =
    [
        ('♠', Suit.Spades), ('♥', Suit.Hearts), ('♦', Suit.Diamonds), ('♣', Suit.Clubs),
        ('★', null), ('☆', null), ('▲', null), ('▼', null), ('◀', null), ('▶', null),
    ];

    // A five-pointed star in a unit square, its points up, the inner corners at two fifths of the reach.
    private static readonly (float X, float Y)[] Star = [.. Enumerable.Range(0, 10).Select(i =>
    {
        var angle = (i * MathF.PI / 5) - (MathF.PI / 2);
        var reach = i % 2 == 0 ? 0.5f : 0.2f;
        return (0.5f + (reach * MathF.Cos(angle)), 0.54f + (reach * MathF.Sin(angle)));
    })];

    /// <summary>Stands for this font where font bytes are expected, in <see cref="Fonts"/>' loader.</summary>
    internal static byte[] Marker { get; } = [];

    /// <summary>A line of the size's height, four fifths of it above the baseline.</summary>
    public void GetMetricsForSize(float fontSize, out int ascent, out int descent, out int lineHeight)
    {
        ascent = (int)MathF.Round(fontSize * 0.8f);
        descent = ascent - (int)MathF.Round(fontSize);
        lineHeight = ascent - descent;
    }

    /// <summary>The glyph id of a suit symbol, or null for any other character.</summary>
    public int? GetGlyphId(int codepoint)
    {
        for (var i = 0; i < Glyphs.Length; i++)
        {
            if (Glyphs[i].Symbol == codepoint)
            {
                return i;
            }
        }

        return null;
    }

    /// <summary>A square about as tall as a capital letter, standing on the baseline, with a little room either side.</summary>
    public void GetGlyphMetrics(int glyphId, float fontSize, out int advance, out int x0, out int y0, out int x1, out int y1)
    {
        var side = Side(fontSize);
        var room = Math.Max(1, (int)MathF.Round(fontSize * 0.06f));
        (x0, y0, x1, y1) = (room, -side, room + side, 0);
        advance = x1 + room;
    }

    /// <summary>The suit's shape into <paramref name="buffer"/>, one byte of coverage per pixel.</summary>
    public void RasterizeGlyphBitmap(int glyphId, float fontSize, byte[] buffer, int startIndex, int outWidth, int outHeight, int outStride)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        var (symbol, suit) = Glyphs[glyphId];
        var side = Side(fontSize);
        for (var y = 0; y < outHeight; y++)
        {
            for (var x = 0; x < outWidth; x++)
            {
                var coverage = suit is { } s ? SuitShape.Coverage(s, x, y, side, Samples) : SymbolCoverage(symbol, x, y, side);
                buffer[startIndex + (y * outStride) + x] = (byte)MathF.Round(coverage * 255);
            }
        }
    }

    /// <summary>How much of pixel (x, y) a symbol covers, sampled on a grid within the pixel.</summary>
    internal static float SymbolCoverage(char symbol, int x, int y, int side)
    {
        var inside = 0;
        for (var sy = 0; sy < Samples; sy++)
        {
            for (var sx = 0; sx < Samples; sx++)
            {
                var (u, v) = ((x + ((sx + 0.5f) / Samples)) / side, (y + ((sy + 0.5f) / Samples)) / side);
                if (Inside(symbol, u, v))
                {
                    inside++;
                }
            }
        }

        return inside / (float)(Samples * Samples);
    }

    /// <summary>Whether a point of the unit square (y down) is in the symbol.</summary>
    private static bool Inside(char symbol, float u, float v) => symbol switch
    {
        '★' => InPolygon(Star, u, v),

        // The outline: the star less a smaller one within it.
        '☆' => InPolygon(Star, u, v) && !InPolygon(Star, 0.5f + ((u - 0.5f) / 0.5f), 0.54f + ((v - 0.54f) / 0.5f)),
        '▲' => v >= 0.1f && v <= 0.9f && MathF.Abs(u - 0.5f) <= (v - 0.1f) * 0.55f,
        '▼' => v >= 0.1f && v <= 0.9f && MathF.Abs(u - 0.5f) <= (0.9f - v) * 0.55f,
        '◀' => u >= 0.1f && u <= 0.9f && MathF.Abs(v - 0.5f) <= (u - 0.1f) * 0.55f,
        '▶' => u >= 0.1f && u <= 0.9f && MathF.Abs(v - 0.5f) <= (0.9f - u) * 0.55f,
        _ => false,
    };

    /// <summary>Even-odd test: a ray to the right crosses the outline an odd number of times from inside.</summary>
    private static bool InPolygon((float X, float Y)[] corners, float u, float v)
    {
        var inside = false;
        for (int i = 0, j = corners.Length - 1; i < corners.Length; j = i++)
        {
            var (a, b) = (corners[i], corners[j]);
            if ((a.Y > v) != (b.Y > v) && u < ((b.X - a.X) * (v - a.Y) / (b.Y - a.Y)) + a.X)
            {
                inside = !inside;
            }
        }

        return inside;
    }

    /// <summary>Not supported: the engine draws text as bitmaps, not distance fields.</summary>
    public void RasterizeGlyphSDF(int glyphId, float fontSize, byte[] buffer, int startIndex, int padding, byte onedge_value, float pixel_dist_scale) =>
        throw new NotSupportedException("The suits and symbols are drawn as bitmaps only.");

    /// <summary>No kerning: suits and symbols stand apart.</summary>
    public int GetGlyphKernAdvance(int previousGlyphId, int glyphId, float fontSize) => 0;

    /// <summary>One pixel per unit: the shapes have no units of their own.</summary>
    public float CalculateScaleForTextShaper(float fontSize) => 1;

    /// <summary>Nothing to free: the shapes are code.</summary>
    public void Dispose()
    {
    }

    private static int Side(float fontSize) => Math.Max(4, (int)MathF.Round(fontSize * 0.68f));
}
