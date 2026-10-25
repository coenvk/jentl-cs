using System.Numerics;

namespace Jentl.Raster;

/// <summary>
/// The picture being drawn: a packed RGBA colour per pixel, row by row from the top-left. The packing matches XNA's
/// (MonoGame's and KNI's) <c>Color.PackedValue</c>, so the pixels can go straight into a texture.
/// </summary>
public sealed class FrameBuffer
{
    public FrameBuffer(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        (Width, Height) = (width, height);
        Pixels = new uint[width * height];
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>The pixels, <c>y * Width + x</c>.</summary>
    public uint[] Pixels { get; }

    public uint this[int x, int y]
    {
        get => Pixels[(y * Width) + x];
        set => Pixels[(y * Width) + x] = value;
    }

    public void Clear(uint color) => Array.Fill(Pixels, color);
}

/// <summary>How far away each pixel's nearest surface is: 0 at the near plane, 1 at the far plane.</summary>
public sealed class DepthBuffer
{
    public DepthBuffer(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        (Width, Height) = (width, height);
        Depths = new float[width * height];
        Clear();
    }

    public int Width { get; }

    public int Height { get; }

    public float[] Depths { get; }

    /// <summary>Sets every pixel to the far plane.</summary>
    public void Clear() => Array.Fill(Depths, 1f);
}

/// <summary>Colours as 0-1 vectors (r, g, b, a) and as packed pixels.</summary>
public static class Colors
{
    /// <summary>Packs a colour as XNA does: red in the lowest byte, then green, blue and alpha.</summary>
    public static uint Pack(Vector4 color)
    {
        var c = Vector4.Clamp(color, Vector4.Zero, Vector4.One) * 255f;
        return (uint)(c.X + 0.5f) | ((uint)(c.Y + 0.5f) << 8) | ((uint)(c.Z + 0.5f) << 16) | ((uint)(c.W + 0.5f) << 24);
    }

    public static Vector4 Unpack(uint packed) =>
        new Vector4(packed & 0xFF, (packed >> 8) & 0xFF, (packed >> 16) & 0xFF, packed >> 24) / 255f;
}
