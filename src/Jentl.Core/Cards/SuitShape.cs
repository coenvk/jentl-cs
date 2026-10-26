using Jentl.Cards;

namespace Jentl.Core.Cards;

/// <summary>
/// The four suits as shapes, drawn in code: the card art's pips and the font's ♠♥♦♣ (<see cref="UI.SuitGlyphSource"/>)
/// are cut from the same ones.
/// </summary>
public static class SuitShape
{
    /// <summary>
    /// Whether the point (<paramref name="x"/>, <paramref name="y"/>) of a square from -1 to 1 each way, y pointing down,
    /// lies inside <paramref name="suit"/>'s shape.
    /// </summary>
    public static bool Covers(Suit suit, float x, float y) => suit switch
    {
        Suit.Diamonds => MathF.Abs(x) / 0.72f + MathF.Abs(y) <= 0.95f,
        Suit.Hearts => Heart(x, y),
        Suit.Spades => Heart(x, -y + 0.15f) || Stem(x, y),
        _ => Circle(x, y, 0, -0.42f, 0.33f) || Circle(x, y, -0.4f, 0.12f, 0.33f) || Circle(x, y, 0.4f, 0.12f, 0.33f)
            || (MathF.Abs(x) < 0.09f && y > -0.2f && y < 0.4f) || Stem(x, y),
    };

    /// <summary>
    /// How much of pixel (<paramref name="px"/>, <paramref name="py"/>) of a <paramref name="size"/>-pixel square the suit
    /// covers, from 0 to 1, sampled <paramref name="samples"/>² times.
    /// </summary>
    public static float Coverage(Suit suit, int px, int py, int size, int samples)
    {
        var hits = 0;
        for (var sy = 0; sy < samples; sy++)
        {
            for (var sx = 0; sx < samples; sx++)
            {
                var x = (px + (sx + 0.5f) / samples) / size * 2 - 1;
                var y = (py + (sy + 0.5f) / samples) / size * 2 - 1;
                if (Covers(suit, x, y))
                {
                    hits++;
                }
            }
        }

        return hits / (float)(samples * samples);
    }

    // The classic heart curve (x² + y² - 1)³ = x²y³, flipped to point down and scaled to fill the square.
    private static bool Heart(float x, float y)
    {
        float hx = x * 1.25f, hy = -(y * 1.25f) + 0.25f;
        var a = hx * hx + hy * hy - 1;
        return a * a * a - hx * hx * hy * hy * hy <= 0;
    }

    private static bool Stem(float x, float y) => y > 0.35f && y < 0.95f && MathF.Abs(x) < 0.08f + (y - 0.35f) * 0.55f;

    private static bool Circle(float x, float y, float cx, float cy, float r) => (x - cx) * (x - cx) + (y - cy) * (y - cy) <= r * r;
}
