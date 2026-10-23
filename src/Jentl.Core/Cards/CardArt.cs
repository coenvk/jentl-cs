using Jentl.Cards;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Jentl.Core.Cards;

/// <summary>
/// Card graphics generated in code, so the kit needs no image files: a rounded card face, a patterned
/// back, an outline for empty piles, and the four suit symbols. Edges are supersampled for smoothness.
/// </summary>
public sealed class CardArt : IDisposable
{
    public const int Width = 200;
    public const int Height = 280;
    private const int SuitSize = 128;
    private const int Corner = 18;
    private const int Samples = 4;

    private readonly Dictionary<Suit, Texture2D> _suits;

    public CardArt(GraphicsDevice device)
    {
        Face = Rounded(device, (x, y, inside, edge) => edge ? new Color(196, 200, 210) : Color.White);
        Back = Rounded(device, (x, y, inside, edge) =>
        {
            if (edge)
            {
                return Color.White;
            }

            // A diagonal lattice inside a white margin.
            var margin = x < 12 || y < 12 || x >= Width - 12 || y >= Height - 12;
            if (margin)
            {
                return Color.White;
            }

            var lattice = (x + y) % 22 < 3 || (x - y + 1000) % 22 < 3;
            return lattice ? new Color(92, 128, 214) : new Color(46, 74, 156);
        });
        Slot = Rounded(device, (x, y, inside, edge) => edge ? new Color(255, 255, 255, 110) : new Color(255, 255, 255, 18), edgeWidth: 4);
        _suits = Enum.GetValues<Suit>().ToDictionary(s => s, s => SuitTexture(device, s));
    }

    public Texture2D Face { get; }

    public Texture2D Back { get; }

    /// <summary>An outline marking where an empty pile is.</summary>
    public Texture2D Slot { get; }

    /// <summary>Card width over height, for laying out piles.</summary>
    public static float AspectRatio => Width / (float)Height;

    public Texture2D SuitSymbol(Suit suit) => _suits[suit];

    public void Dispose()
    {
        Face.Dispose();
        Back.Dispose();
        Slot.Dispose();
        foreach (var texture in _suits.Values)
        {
            texture.Dispose();
        }
    }

    /// <summary>A card-sized rounded rectangle; <paramref name="paint"/> colours each pixel inside it.</summary>
    private static Texture2D Rounded(GraphicsDevice device, Func<int, int, bool, bool, Color> paint, int edgeWidth = 2)
    {
        var pixels = new Color[Width * Height];
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                var coverage = Coverage((sx, sy) => RoundedDistance(x + sx, y + sy) <= 0);
                if (coverage == 0)
                {
                    continue;
                }

                var edge = RoundedDistance(x + 0.5f, y + 0.5f) > -edgeWidth;
                var color = paint(x, y, true, edge);
                pixels[y * Width + x] = new Color(color, color.A / 255f * coverage);
            }
        }

        var texture = new Texture2D(device, Width, Height);
        texture.SetData(pixels);
        return texture;
    }

    /// <summary>Signed distance to the rounded rectangle's border: negative inside.</summary>
    private static float RoundedDistance(float x, float y)
    {
        var dx = MathF.Max(MathF.Abs(x - Width / 2f) - (Width / 2f - Corner), 0);
        var dy = MathF.Max(MathF.Abs(y - Height / 2f) - (Height / 2f - Corner), 0);
        var outside = MathF.Sqrt(dx * dx + dy * dy) - Corner;
        var inside = MathF.Min(MathF.Max(MathF.Abs(x - Width / 2f) - Width / 2f, MathF.Abs(y - Height / 2f) - Height / 2f), 0);
        return dx > 0 || dy > 0 ? outside : inside;
    }

    private static Texture2D SuitTexture(GraphicsDevice device, Suit suit)
    {
        Func<float, float, bool> shape = suit switch
        {
            Suit.Diamonds => (x, y) => MathF.Abs(x) / 0.72f + MathF.Abs(y) <= 0.95f,
            Suit.Hearts => Heart,
            Suit.Spades => (x, y) => Heart(x, -y + 0.15f) || Stem(x, y),
            _ => (x, y) => Circle(x, y, 0, -0.42f, 0.33f) || Circle(x, y, -0.4f, 0.12f, 0.33f) || Circle(x, y, 0.4f, 0.12f, 0.33f)
                || (MathF.Abs(x) < 0.09f && y > -0.2f && y < 0.4f) || Stem(x, y),
        };

        var pixels = new Color[SuitSize * SuitSize];
        for (var py = 0; py < SuitSize; py++)
        {
            for (var px = 0; px < SuitSize; px++)
            {
                // Map the pixel to [-1, 1] with y pointing down.
                var coverage = Coverage((sx, sy) => shape((px + sx) / SuitSize * 2 - 1, (py + sy) / SuitSize * 2 - 1));
                pixels[py * SuitSize + px] = new Color(Color.White, coverage);
            }
        }

        var texture = new Texture2D(device, SuitSize, SuitSize);
        texture.SetData(pixels);
        return texture;

        // The classic heart curve (x² + y² - 1)³ = x²y³, flipped to point down and scaled to fill the square.
        static bool Heart(float x, float y)
        {
            float hx = x * 1.25f, hy = -(y * 1.25f) + 0.25f;
            var a = hx * hx + hy * hy - 1;
            return a * a * a - hx * hx * hy * hy * hy <= 0;
        }

        static bool Stem(float x, float y) => y > 0.35f && y < 0.95f && MathF.Abs(x) < 0.08f + (y - 0.35f) * 0.55f;

        static bool Circle(float x, float y, float cx, float cy, float r) => (x - cx) * (x - cx) + (y - cy) * (y - cy) <= r * r;
    }

    /// <summary>Fraction of <see cref="Samples"/>² sub-pixel points inside a shape.</summary>
    private static float Coverage(Func<float, float, bool> inside)
    {
        var hits = 0;
        for (var sy = 0; sy < Samples; sy++)
        {
            for (var sx = 0; sx < Samples; sx++)
            {
                if (inside((sx + 0.5f) / Samples, (sy + 0.5f) / Samples))
                {
                    hits++;
                }
            }
        }

        return hits / (float)(Samples * Samples);
    }
}
