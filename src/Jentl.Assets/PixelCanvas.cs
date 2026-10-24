namespace Jentl.Assets;

/// <summary>An 8-bit colour with alpha, for art drawn in code.</summary>
/// <param name="R">Red.</param>
/// <param name="G">Green.</param>
/// <param name="B">Blue.</param>
/// <param name="A">Alpha: 0 clear, 255 opaque.</param>
public readonly record struct Rgba(byte R, byte G, byte B, byte A = 255)
{
    /// <summary>Fully transparent.</summary>
    public static readonly Rgba Clear = new(0, 0, 0, 0);

    /// <summary>An opaque colour from 0xRRGGBB.</summary>
    public static Rgba Hex(uint rgb) => new((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

    /// <summary>Whether nothing is drawn here.</summary>
    public bool IsClear => A == 0;

    /// <summary>Blends towards <paramref name="other"/> by <paramref name="t"/> (0 to 1), alpha included.</summary>
    public Rgba Mix(Rgba other, double t) => new(Lerp(R, other.R, t), Lerp(G, other.G, t), Lerp(B, other.B, t), Lerp(A, other.A, t));

    /// <summary>Towards white by <paramref name="t"/>.</summary>
    public Rgba Lighter(double t) => Mix(new Rgba(255, 255, 255, A), t);

    /// <summary>Towards black by <paramref name="t"/>.</summary>
    public Rgba Darker(double t) => Mix(new Rgba(0, 0, 0, A), t);

    /// <summary>The same colour with another alpha.</summary>
    public Rgba WithAlpha(byte alpha) => this with { A = alpha };

    private static byte Lerp(byte a, byte b, double t) => (byte)Math.Round(a + ((b - a) * t), MidpointRounding.AwayFromZero);
}

/// <summary>
/// A drawing surface for pixel art made in code (tilesets, sprites): rectangles, lines, ellipses, outlines and
/// mirroring. Drawing outside the canvas is ignored. Turns into an <see cref="Image"/> or a PNG.
/// </summary>
public sealed class PixelCanvas
{
    private readonly Rgba[] _pixels;

    /// <param name="width">Width in pixels.</param>
    /// <param name="height">Height in pixels.</param>
    public PixelCanvas(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        Width = width;
        Height = height;
        _pixels = new Rgba[width * height];
    }

    /// <summary>Width in pixels.</summary>
    public int Width { get; }

    /// <summary>Height in pixels.</summary>
    public int Height { get; }

    /// <summary>The pixel at (x, y); clear outside the canvas, where writes are ignored.</summary>
    public Rgba this[int x, int y]
    {
        get => Inside(x, y) ? _pixels[(y * Width) + x] : Rgba.Clear;
        set
        {
            if (Inside(x, y))
            {
                _pixels[(y * Width) + x] = value;
            }
        }
    }

    /// <summary>Whether (x, y) is on the canvas.</summary>
    public bool Inside(int x, int y) => x >= 0 && x < Width && y >= 0 && y < Height;

    /// <summary>Paints every pixel.</summary>
    public void Fill(Rgba color) => Array.Fill(_pixels, color);

    /// <summary>A filled rectangle.</summary>
    public void Rect(int x, int y, int width, int height, Rgba color)
    {
        for (var py = Math.Max(0, y); py < Math.Min(Height, y + height); py++)
        {
            for (var px = Math.Max(0, x); px < Math.Min(Width, x + width); px++)
            {
                _pixels[(py * Width) + px] = color;
            }
        }
    }

    /// <summary>A one-pixel line, both ends included (Bresenham).</summary>
    public void Line(int x0, int y0, int x1, int y1, Rgba color)
    {
        int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
        int dy = -Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
        var error = dx + dy;
        while (true)
        {
            this[x0, y0] = color;
            if (x0 == x1 && y0 == y1)
            {
                return;
            }

            var doubled = 2 * error;
            if (doubled >= dy)
            {
                error += dy;
                x0 += sx;
            }

            if (doubled <= dx)
            {
                error += dx;
                y0 += sy;
            }
        }
    }

    /// <summary>An ellipse around (cx, cy) with radii rx and ry, filled or as a one-pixel ring.</summary>
    public void Ellipse(int cx, int cy, int rx, int ry, Rgba color, bool filled = true)
    {
        bool In(int x, int y)
        {
            double nx = (x - cx) / (double)Math.Max(rx, 1), ny = (y - cy) / (double)Math.Max(ry, 1);
            return (nx * nx) + (ny * ny) <= 1.0001;
        }

        for (var y = cy - ry; y <= cy + ry; y++)
        {
            for (var x = cx - rx; x <= cx + rx; x++)
            {
                if (In(x, y) && (filled || !In(x - 1, y) || !In(x + 1, y) || !In(x, y - 1) || !In(x, y + 1)))
                {
                    this[x, y] = color;
                }
            }
        }
    }

    /// <summary>Paints the clear pixels that touch a drawn pixel (left, right, above, below) in <paramref name="color"/>.</summary>
    public void Outline(Rgba color)
    {
        var edge = new List<(int X, int Y)>();
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                if (this[x, y].IsClear && (!this[x - 1, y].IsClear || !this[x + 1, y].IsClear || !this[x, y - 1].IsClear || !this[x, y + 1].IsClear))
                {
                    edge.Add((x, y));
                }
            }
        }

        foreach (var (x, y) in edge)
        {
            this[x, y] = color;
        }
    }

    /// <summary>Copies the left half onto the right half, mirrored: draw half of something symmetric.</summary>
    public void MirrorX()
    {
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width / 2; x++)
            {
                this[Width - 1 - x, y] = this[x, y];
            }
        }
    }

    /// <summary>Draws <paramref name="source"/> with its top-left at (x, y), skipping its clear pixels.</summary>
    public void Blit(PixelCanvas source, int x, int y)
    {
        ArgumentNullException.ThrowIfNull(source);
        for (var sy = 0; sy < source.Height; sy++)
        {
            for (var sx = 0; sx < source.Width; sx++)
            {
                var pixel = source[sx, sy];
                if (!pixel.IsClear)
                {
                    this[x + sx, y + sy] = pixel;
                }
            }
        }
    }

    /// <summary>The canvas as an <see cref="Image"/> (e.g. for <see cref="AtlasBuilder"/>).</summary>
    public Image ToImage()
    {
        var bytes = new byte[_pixels.Length * 4];
        for (var i = 0; i < _pixels.Length; i++)
        {
            var p = _pixels[i];
            (bytes[i * 4], bytes[(i * 4) + 1], bytes[(i * 4) + 2], bytes[(i * 4) + 3]) = (p.R, p.G, p.B, p.A);
        }

        return new Image(Width, Height, bytes);
    }

    /// <summary>The canvas as a PNG file.</summary>
    public byte[] ToPng() => Png.Encode(ToImage());
}
