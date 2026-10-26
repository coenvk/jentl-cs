using FontStashSharp.Interfaces;
using Jentl.Cards;
using Jentl.Core.Cards;

namespace Jentl.Core.UI;

/// <summary>
/// A font of just the four card suits, ♠ ♥ ♦ ♣, drawn from <see cref="SuitShape"/>. <see cref="Fonts"/> adds it behind
/// the game's own font, so text can show suits whatever font the game uses (few display fonts have them).
/// </summary>
public sealed class SuitGlyphSource : IFontSource
{
    private const int Samples = 4;

    // The suits by glyph id.
    private static readonly (char Symbol, Suit Suit)[] Glyphs = [('♠', Suit.Spades), ('♥', Suit.Hearts), ('♦', Suit.Diamonds), ('♣', Suit.Clubs)];

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
        var suit = Glyphs[glyphId].Suit;
        var side = Side(fontSize);
        for (var y = 0; y < outHeight; y++)
        {
            for (var x = 0; x < outWidth; x++)
            {
                buffer[startIndex + (y * outStride) + x] = (byte)MathF.Round(SuitShape.Coverage(suit, x, y, side, Samples) * 255);
            }
        }
    }

    /// <summary>Not supported: the engine draws text as bitmaps, not distance fields.</summary>
    public void RasterizeGlyphSDF(int glyphId, float fontSize, byte[] buffer, int startIndex, int padding, byte onedge_value, float pixel_dist_scale) =>
        throw new NotSupportedException("The suits are drawn as bitmaps only.");

    /// <summary>No kerning: suits stand apart.</summary>
    public int GetGlyphKernAdvance(int previousGlyphId, int glyphId, float fontSize) => 0;

    /// <summary>One pixel per unit: the shapes have no units of their own.</summary>
    public float CalculateScaleForTextShaper(float fontSize) => 1;

    /// <summary>Nothing to free: the shapes are code.</summary>
    public void Dispose()
    {
    }

    private static int Side(float fontSize) => Math.Max(4, (int)MathF.Round(fontSize * 0.68f));
}
