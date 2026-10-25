using System.Numerics;

namespace Jentl.Raster;

/// <summary>How a texture is read between its texels.</summary>
public enum TextureFilter
{
    /// <summary>The nearest texel: hard pixel art.</summary>
    Nearest,

    /// <summary>A blend of the four nearest: soft.</summary>
    Bilinear,
}

/// <summary>What a texture does outside 0 to 1.</summary>
public enum TextureWrap
{
    /// <summary>Tiles.</summary>
    Repeat,

    /// <summary>Holds its edge texels.</summary>
    Clamp,
}

/// <summary>How texture coordinates are spread across a triangle.</summary>
public enum TextureMapping
{
    /// <summary>Correct for depth: textures stay put on surfaces seen at an angle.</summary>
    PerspectiveCorrect,

    /// <summary>Straight across the screen, as the first PlayStation did: textures swim and bend. A retro look.</summary>
    Affine,
}

/// <summary>
/// An image to put on triangles, in packed RGBA (as <see cref="FrameBuffer"/>), row by row from the top; texture
/// coordinate (0, 0) is its top-left corner, (1, 1) its bottom-right. Texels with alpha under one half are cut out.
/// </summary>
public sealed class Texture
{
    public Texture(int width, int height, uint[] pixels)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentNullException.ThrowIfNull(pixels);
        if (pixels.Length != width * height)
        {
            throw new ArgumentException($"A {width}x{height} texture needs {width * height} pixels, not {pixels.Length}.", nameof(pixels));
        }

        (Width, Height, Pixels) = (width, height, pixels);
    }

    public int Width { get; }

    public int Height { get; }

    public uint[] Pixels { get; }

    public TextureFilter Filter { get; init; } = TextureFilter.Nearest;

    public TextureWrap Wrap { get; init; } = TextureWrap.Repeat;

    /// <summary>A texture from RGBA bytes (four per pixel, as image files decode).</summary>
    public static Texture FromRgba(int width, int height, byte[] rgba)
    {
        ArgumentNullException.ThrowIfNull(rgba);
        var pixels = new uint[width * height];
        for (var i = 0; i < pixels.Length; i++)
        {
            pixels[i] = rgba[i * 4] | ((uint)rgba[(i * 4) + 1] << 8) | ((uint)rgba[(i * 4) + 2] << 16) | ((uint)rgba[(i * 4) + 3] << 24);
        }

        return new Texture(width, height, pixels);
    }

    /// <summary>The colour at texture coordinate <paramref name="uv"/>.</summary>
    public Vector4 Sample(Vector2 uv)
    {
        var x = (uv.X * Width) - 0.5f;
        var y = (uv.Y * Height) - 0.5f;
        if (Filter == TextureFilter.Nearest)
        {
            return Colors.Unpack(Texel((int)MathF.Floor(x + 0.5f), (int)MathF.Floor(y + 0.5f)));
        }

        var (x0, y0) = ((int)MathF.Floor(x), (int)MathF.Floor(y));
        var (fx, fy) = (x - x0, y - y0);
        var top = Vector4.Lerp(Colors.Unpack(Texel(x0, y0)), Colors.Unpack(Texel(x0 + 1, y0)), fx);
        var bottom = Vector4.Lerp(Colors.Unpack(Texel(x0, y0 + 1)), Colors.Unpack(Texel(x0 + 1, y0 + 1)), fx);
        return Vector4.Lerp(top, bottom, fy);
    }

    private uint Texel(int x, int y)
    {
        if (Wrap == TextureWrap.Repeat)
        {
            x = ((x % Width) + Width) % Width;
            y = ((y % Height) + Height) % Height;
        }
        else
        {
            x = Math.Clamp(x, 0, Width - 1);
            y = Math.Clamp(y, 0, Height - 1);
        }

        return Pixels[(y * Width) + x];
    }
}
