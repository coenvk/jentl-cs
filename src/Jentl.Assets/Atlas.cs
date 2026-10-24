using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jentl.Assets;

/// <summary>Where one sprite sits in an atlas image, in pixels.</summary>
public readonly record struct AtlasRegion(int X, int Y, int Width, int Height);

/// <summary>
/// A texture atlas's description, stored as JSON next to its image: the image file, its size, each sprite's
/// region by name, and where the sprites came from with a fingerprint of them, so a stale atlas can be found.
/// </summary>
public sealed record AtlasFile(
    string Image,
    int Width,
    int Height,
    IReadOnlyDictionary<string, AtlasRegion> Sprites,
    string? Source = null,
    string? Fingerprint = null)
{
    public string ToJson() => JsonSerializer.Serialize(this, AssetsJson.Default.AtlasFile);

    /// <exception cref="JsonException">The text isn't an atlas description.</exception>
    public static AtlasFile FromJson(string json) =>
        JsonSerializer.Deserialize(json, AssetsJson.Default.AtlasFile) ?? throw new JsonException("Empty atlas description.");
}

/// <summary>
/// Packs many small images into one: rows ("shelves") filled left to right, tallest images first, in the smallest
/// power-of-two square-ish sheet that holds them. Each sprite is surrounded by <c>padding</c> pixels, and its edge
/// pixels are repeated into that border so filtering at the sprite's edge never picks up a neighbour.
/// </summary>
public static class AtlasPacker
{
    /// <summary>Places rectangles of the given sizes; returns the sheet size and each one's position (by index), or throws if they don't fit in <paramref name="maxSize"/>.</summary>
    public static (int Width, int Height, AtlasRegion[] Regions) Pack(IReadOnlyList<(int Width, int Height)> sizes, int padding = 2, int maxSize = 4096)
    {
        ArgumentNullException.ThrowIfNull(sizes);
        ArgumentOutOfRangeException.ThrowIfNegative(padding);
        if (sizes.Count == 0)
        {
            return (1, 1, []);
        }

        for (var width = 64; width <= maxSize; width *= 2)
        {
            for (var height = Math.Max(64, width / 2); height <= width; height *= 2)
            {
                if (TryPack(sizes, padding, width, height) is { } regions)
                {
                    return (width, height, regions);
                }
            }
        }

        throw new InvalidOperationException($"The images don't fit in a {maxSize}x{maxSize} atlas.");
    }

    private static AtlasRegion[]? TryPack(IReadOnlyList<(int Width, int Height)> sizes, int padding, int width, int height)
    {
        var order = Enumerable.Range(0, sizes.Count).OrderByDescending(i => sizes[i].Height).ThenByDescending(i => sizes[i].Width).ThenBy(i => i);
        var regions = new AtlasRegion[sizes.Count];
        int x = 0, y = 0, shelf = 0;
        foreach (var i in order)
        {
            var (w, h) = (sizes[i].Width + 2 * padding, sizes[i].Height + 2 * padding);
            if (w > width)
            {
                return null;
            }

            if (x + w > width)
            {
                (x, y, shelf) = (0, y + shelf, 0);
            }

            if (y + h > height)
            {
                return null;
            }

            regions[i] = new AtlasRegion(x + padding, y + padding, sizes[i].Width, sizes[i].Height);
            x += w;
            shelf = Math.Max(shelf, h);
        }

        return regions;
    }
}

/// <summary>Builds an atlas image and its description from named images.</summary>
public static class AtlasBuilder
{
    public static (Image Image, AtlasFile Description) Build(IReadOnlyList<(string Name, Image Image)> sprites, string imageFile, int padding = 2, string? source = null, string? fingerprint = null)
    {
        ArgumentNullException.ThrowIfNull(sprites);
        var duplicate = sprites.GroupBy(s => s.Name, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            throw new ArgumentException($"Two sprites are called {duplicate.Key}.", nameof(sprites));
        }

        var (width, height, regions) = AtlasPacker.Pack([.. sprites.Select(s => (s.Image.Width, s.Image.Height))], padding);
        var sheet = new Image(width, height);
        for (var i = 0; i < sprites.Count; i++)
        {
            sheet.Paste(sprites[i].Image, regions[i].X, regions[i].Y);
            Extrude(sheet, regions[i], padding);
        }

        var named = sprites.Select((s, i) => (s.Name, Region: regions[i])).OrderBy(s => s.Name, StringComparer.Ordinal)
            .ToDictionary(s => s.Name, s => s.Region, StringComparer.Ordinal);
        return (sheet, new AtlasFile(imageFile, width, height, named, source, fingerprint));
    }

    /// <summary>
    /// Builds an atlas from every PNG under <paramref name="folder"/>, each named by its path inside it without
    /// the extension ("floors/floor1"), with a fingerprint of the files.
    /// </summary>
    public static (Image Image, AtlasFile Description) FromFolder(string folder, string imageFile, string? source = null, int padding = 2)
    {
        var files = SpriteFiles(folder);
        var sprites = files.Select(f => (f.Name, Png.Decode(File.ReadAllBytes(f.Path)))).ToList();
        return Build(sprites, imageFile, padding, source, Fingerprint(folder));
    }

    /// <summary>A short hash of every PNG under <paramref name="folder"/> (names and contents), to tell when an atlas is out of date.</summary>
    public static string Fingerprint(string folder)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var (name, path) in SpriteFiles(folder))
        {
            hash.AppendData(Encoding.UTF8.GetBytes(name + "\n"));
            hash.AppendData(File.ReadAllBytes(path));
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset())[..16];
    }

    private static List<(string Name, string Path)> SpriteFiles(string folder) =>
        [.. Directory.EnumerateFiles(folder, "*.png", SearchOption.AllDirectories)
            .Select(path => (Name: Path.ChangeExtension(Path.GetRelativePath(folder, path), null).Replace('\\', '/'), Path: path))
            .OrderBy(f => f.Name, StringComparer.Ordinal)];

    /// <summary>Repeats the sprite's outermost pixels into its padding.</summary>
    private static void Extrude(Image sheet, AtlasRegion r, int padding)
    {
        for (var p = 1; p <= padding; p++)
        {
            for (var x = r.X; x < r.X + r.Width; x++)
            {
                sheet[x, r.Y - p] = sheet[x, r.Y];
                sheet[x, r.Y + r.Height - 1 + p] = sheet[x, r.Y + r.Height - 1];
            }

            for (var y = r.Y - padding; y < r.Y + r.Height + padding; y++)
            {
                sheet[r.X - p, y] = sheet[r.X, y];
                sheet[r.X + r.Width - 1 + p, y] = sheet[r.X + r.Width - 1, y];
            }
        }
    }
}

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(AtlasFile))]
internal sealed partial class AssetsJson : JsonSerializerContext;
