using System.Buffers.Binary;
using System.IO.Compression;
using StbImageSharp;

namespace Jentl.Assets;

/// <summary>An image in memory: <see cref="Width"/> × <see cref="Height"/> pixels, four bytes each (red, green, blue, alpha), rows top to bottom.</summary>
public sealed class Image
{
    public Image(int width, int height, byte[]? pixels = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        pixels ??= new byte[width * height * 4];
        if (pixels.Length != width * height * 4)
        {
            throw new ArgumentException($"A {width}x{height} image needs {width * height * 4} bytes, not {pixels.Length}.", nameof(pixels));
        }

        Width = width;
        Height = height;
        Pixels = pixels;
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>RGBA bytes, row by row.</summary>
    public byte[] Pixels { get; }

    /// <summary>Copies <paramref name="source"/> in with its top-left corner at (<paramref name="x"/>, <paramref name="y"/>).</summary>
    public void Paste(Image source, int x, int y)
    {
        ArgumentNullException.ThrowIfNull(source);
        for (var row = 0; row < source.Height; row++)
        {
            Buffer.BlockCopy(source.Pixels, row * source.Width * 4, Pixels, ((y + row) * Width + x) * 4, source.Width * 4);
        }
    }

    /// <summary>The pixel at (x, y) as RGBA.</summary>
    public (byte R, byte G, byte B, byte A) this[int x, int y]
    {
        get
        {
            var i = (y * Width + x) * 4;
            return (Pixels[i], Pixels[i + 1], Pixels[i + 2], Pixels[i + 3]);
        }

        set
        {
            var i = (y * Width + x) * 4;
            (Pixels[i], Pixels[i + 1], Pixels[i + 2], Pixels[i + 3]) = value;
        }
    }
}

/// <summary>Reads PNG (and JPEG) files and writes PNG files.</summary>
public static class Png
{
    private static readonly byte[] Signature = [137, 80, 78, 71, 13, 10, 26, 10];
    private static readonly uint[] CrcTable = BuildCrcTable();

    /// <exception cref="InvalidDataException">The bytes aren't an image.</exception>
    public static Image Decode(byte[] file)
    {
        ArgumentNullException.ThrowIfNull(file);
        try
        {
            var result = ImageResult.FromMemory(file, ColorComponents.RedGreenBlueAlpha);
            return new Image(result.Width, result.Height, result.Data);
        }
        catch (Exception e) when (e is not ArgumentNullException)
        {
            throw new InvalidDataException("Not a readable image.", e);
        }
    }

    /// <summary>A PNG file of <paramref name="image"/>: 8-bit RGBA, compressed.</summary>
    public static byte[] Encode(Image image)
    {
        ArgumentNullException.ThrowIfNull(image);

        // Each row starts with filter type 0 (none); zlib does the rest.
        var raw = new byte[image.Height * (image.Width * 4 + 1)];
        for (var y = 0; y < image.Height; y++)
        {
            Buffer.BlockCopy(image.Pixels, y * image.Width * 4, raw, y * (image.Width * 4 + 1) + 1, image.Width * 4);
        }

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            zlib.Write(raw);
        }

        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, image.Width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), image.Height);
        header[8] = 8; // bits per channel
        header[9] = 6; // RGBA

        using var file = new MemoryStream();
        file.Write(Signature);
        Chunk(file, "IHDR", header);
        Chunk(file, "IDAT", compressed.ToArray());
        Chunk(file, "IEND", []);
        return file.ToArray();
    }

    private static void Chunk(Stream file, string type, byte[] data)
    {
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(number, data.Length);
        file.Write(number);
        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        file.Write(typeBytes);
        file.Write(data);
        BinaryPrimitives.WriteUInt32BigEndian(number, Crc(typeBytes, data));
        file.Write(number);
    }

    private static uint Crc(byte[] type, byte[] data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in type.Concat(data))
        {
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }

        return crc ^ 0xFFFFFFFFu;
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (var n = 0u; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            }

            table[n] = c;
        }

        return table;
    }
}
