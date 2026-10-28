using System.Numerics;

namespace Jentl.Geometry;

/// <summary>
/// Perlin noise: a smooth random field, the same for the same seed on every machine. It wanders between about -1 and
/// 1, changing gently over one unit, so scale the coordinates to choose the size of its features (terrain heights,
/// clouds, the wobble of a camera or a flame). <see cref="Fractal(float, float, int, float, float)"/> layers octaves
/// for detail, and <see cref="Ridged"/> turns that into mountain ridges and veins.
/// </summary>
public sealed class Noise
{
    // The twelve edge midpoints of a cube; 2D noise uses their x and y.
    private static readonly Vector3[] Gradients =
    [
        new(1, 1, 0), new(-1, 1, 0), new(1, -1, 0), new(-1, -1, 0),
        new(1, 0, 1), new(-1, 0, 1), new(1, 0, -1), new(-1, 0, -1),
        new(0, 1, 1), new(0, -1, 1), new(0, 1, -1), new(0, -1, -1),
    ];

    private static readonly Vector2[] Gradients2 =
    [
        new(1, 0), new(-1, 0), new(0, 1), new(0, -1),
        new(0.7071068f, 0.7071068f), new(-0.7071068f, 0.7071068f), new(0.7071068f, -0.7071068f), new(-0.7071068f, -0.7071068f),
    ];

    private readonly byte[] _permutation = new byte[512];

    public Noise(int seed = 0)
    {
        Seed = seed;

        // A shuffle of 0..255 from SplitMix64, not System.Random, so a seed means the same field everywhere, always.
        var order = Enumerable.Range(0, 256).Select(i => (byte)i).ToArray();
        var state = (ulong)(uint)seed;
        for (var i = 255; i > 0; i--)
        {
            state += 0x9E3779B97F4A7C15;
            var z = state;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EB;
            z ^= z >> 31;
            var j = (int)(z % (ulong)(i + 1));
            (order[i], order[j]) = (order[j], order[i]);
        }

        for (var i = 0; i < 512; i++)
        {
            _permutation[i] = order[i & 255];
        }
    }

    public int Seed { get; }

    /// <summary>Noise along a line: a smooth wobble, 0 at whole numbers.</summary>
    public float At(float x)
    {
        var xi = Floor(x);
        var xf = x - xi;
        var g0 = (_permutation[xi & 255] & 1) == 0 ? 1 : -1;
        var g1 = (_permutation[(xi + 1) & 255] & 1) == 0 ? 1 : -1;

        // Scaled so a line's noise reaches about as far as the plane's.
        return 2 * Lerp(g0 * xf, g1 * (xf - 1), Fade(xf));
    }

    /// <summary>Noise over the plane, about -1 to 1.</summary>
    public float At(float x, float y)
    {
        int xi = Floor(x), yi = Floor(y);
        float xf = x - xi, yf = y - yi;
        float u = Fade(xf), v = Fade(yf);
        var n00 = Dot(Hash(xi, yi), xf, yf);
        var n10 = Dot(Hash(xi + 1, yi), xf - 1, yf);
        var n01 = Dot(Hash(xi, yi + 1), xf, yf - 1);
        var n11 = Dot(Hash(xi + 1, yi + 1), xf - 1, yf - 1);

        // The corners' gradients reach at most √½ at a cell's centre; scale that up to about 1.
        return 1.4142135f * Lerp(Lerp(n00, n10, u), Lerp(n01, n11, u), v);
    }

    /// <summary>Noise in space, about -1 to 1: use the third coordinate as time to animate a 2D field smoothly.</summary>
    public float At(float x, float y, float z)
    {
        int xi = Floor(x), yi = Floor(y), zi = Floor(z);
        float xf = x - xi, yf = y - yi, zf = z - zi;
        float u = Fade(xf), v = Fade(yf), w = Fade(zf);
        float Corner(int dx, int dy, int dz) =>
            Vector3.Dot(Gradients[_permutation[_permutation[_permutation[(xi + dx) & 255] + ((yi + dy) & 255)] + ((zi + dz) & 255)] % 12], new Vector3(xf - dx, yf - dy, zf - dz));

        var x00 = Lerp(Corner(0, 0, 0), Corner(1, 0, 0), u);
        var x10 = Lerp(Corner(0, 1, 0), Corner(1, 1, 0), u);
        var x01 = Lerp(Corner(0, 0, 1), Corner(1, 0, 1), u);
        var x11 = Lerp(Corner(0, 1, 1), Corner(1, 1, 1), u);
        return Lerp(Lerp(x00, x10, v), Lerp(x01, x11, v), w);
    }

    public float At(Vector2 point) => At(point.X, point.Y);

    /// <summary>
    /// Fractal (fBm) noise: <paramref name="octaves"/> layers, each <paramref name="lacunarity"/> times finer and
    /// <paramref name="gain"/> times fainter than the last, summed and scaled back to about -1 to 1. More octaves, more
    /// detail: 1 is plain noise, 4 to 6 looks like terrain.
    /// </summary>
    public float Fractal(float x, float y, int octaves = 5, float lacunarity = 2, float gain = 0.5f)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(octaves, 1);
        float sum = 0, amplitude = 1, total = 0, frequency = 1;
        for (var i = 0; i < octaves; i++)
        {
            // Each octave shifted, so their zeros at whole numbers don't line up.
            sum += amplitude * At(x * frequency + i * 17.31f, y * frequency + i * 9.47f);
            total += amplitude;
            amplitude *= gain;
            frequency *= lacunarity;
        }

        return sum / total;
    }

    /// <summary>
    /// Ridged fractal noise from 0 to 1: sharp crests where plain noise crosses zero, for mountain ridges, cracks,
    /// veins of ore and lightning-like rivers.
    /// </summary>
    public float Ridged(float x, float y, int octaves = 5, float lacunarity = 2, float gain = 0.5f)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(octaves, 1);
        float sum = 0, amplitude = 1, total = 0, frequency = 1;
        for (var i = 0; i < octaves; i++)
        {
            var ridge = 1 - MathF.Abs(At(x * frequency + i * 17.31f, y * frequency + i * 9.47f));
            sum += amplitude * ridge * ridge;
            total += amplitude;
            amplitude *= gain;
            frequency *= lacunarity;
        }

        return Math.Clamp(sum / total, 0, 1);
    }

    /// <summary>
    /// A <paramref name="width"/> by <paramref name="height"/> grid of <see cref="Fractal"/> noise from 0 to 1, one
    /// cell every <paramref name="scale"/> units: a height map, a cloud texture, a map of where forests grow.
    /// </summary>
    public float[,] Map(int width, int height, float scale, int octaves = 5)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(scale);
        var map = new float[width, height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                map[x, y] = Math.Clamp((Fractal(x / scale, y / scale, octaves) + 1) / 2, 0, 1);
            }
        }

        return map;
    }

    private Vector2 Hash(int x, int y) => Gradients2[_permutation[_permutation[x & 255] + (y & 255)] & 7];

    private static float Dot(Vector2 gradient, float x, float y) => gradient.X * x + gradient.Y * y;

    private static int Floor(float value) => (int)MathF.Floor(value);

    // Perlin's improved fade, 6t⁵ - 15t⁴ + 10t³: smooth in its first and second derivative at the cell edges.
    private static float Fade(float t) => t * t * t * (t * (t * 6 - 15) + 10);

    private static float Lerp(float a, float b, float t) => a + t * (b - a);
}
