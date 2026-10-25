using System.Numerics;

namespace Jentl.Raster;

/// <summary>Small textures made in code.</summary>
public static class Textures
{
    /// <summary>
    /// A round spot of <paramref name="color"/> filling a square <paramref name="size"/> texels across and clear
    /// round it: a blob shadow under a car or a tree, on a translucent quad. <paramref name="softness"/> is how much of
    /// the radius fades out (0 a hard pixel-art edge, 1 fading from the middle).
    /// </summary>
    public static Texture Spot(int size, Vector4 color, float softness = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size);
        var pixels = new uint[size * size];
        var radius = size / 2f;
        var hard = 1 - Math.Clamp(softness, 0, 1);
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(radius)) / radius;
                var fade = d >= 1 ? 0 : d <= hard ? 1 : 1 - ((d - hard) / (1 - hard));
                pixels[(y * size) + x] = Colors.Pack(color with { W = color.W * fade });
            }
        }

        return new Texture(size, size, pixels) { Wrap = TextureWrap.Clamp };
    }
}
