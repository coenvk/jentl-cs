using System.Text.Json;

namespace Jentl.Assets;

/// <summary>Something wrong with one asset file.</summary>
public sealed record AssetProblem(string Path, string Message)
{
    public override string ToString() => $"{Path}: {Message}";
}

/// <summary>
/// Looks over a folder of game assets before they ship: every image decodes, every JSON file parses, every atlas
/// fits its image and is up to date with its source sprites, and anything with a check of its own (by extension,
/// e.g. Tiled maps) passes it.
/// </summary>
public sealed class AssetCheck(string folder)
{
    /// <summary>The atlas descriptions' file name ending.</summary>
    public const string AtlasSuffix = ".atlas.json";

    /// <summary>Further checks by file extension (".tmx"): given the file's full path, they return what's wrong, or null.</summary>
    public Dictionary<string, Func<string, string?>> Checks { get; } = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<AssetProblem> Run()
    {
        var problems = new List<AssetProblem>();
        foreach (var path in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            var relative = Path.GetRelativePath(folder, path).Replace('\\', '/');
            var problem = Check(path);
            if (problem is not null)
            {
                problems.Add(new AssetProblem(relative, problem));
            }
        }

        return problems;
    }

    private string? Check(string path)
    {
        try
        {
            if (path.EndsWith(AtlasSuffix, StringComparison.OrdinalIgnoreCase))
            {
                return CheckAtlas(path);
            }

            var extension = Path.GetExtension(path);
            if (Checks.TryGetValue(extension, out var check))
            {
                return check(path);
            }

            switch (extension.ToUpperInvariant())
            {
                case ".PNG":
                    Png.Decode(File.ReadAllBytes(path));
                    break;
                case ".JSON":
                    using (JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip }))
                    {
                    }

                    break;
            }

            return null;
        }
        catch (Exception e) when (e is IOException or InvalidDataException or JsonException or FormatException or InvalidOperationException or System.Xml.XmlException)
        {
            return e.Message;
        }
    }

    private static string? CheckAtlas(string path)
    {
        var atlas = AtlasFile.FromJson(File.ReadAllText(path));
        var folder = Path.GetDirectoryName(path)!;
        var imagePath = Path.Combine(folder, atlas.Image);
        if (!File.Exists(imagePath))
        {
            return $"its image {atlas.Image} is missing";
        }

        var image = Png.Decode(File.ReadAllBytes(imagePath));
        if (image.Width != atlas.Width || image.Height != atlas.Height)
        {
            return $"says {atlas.Width}x{atlas.Height} but {atlas.Image} is {image.Width}x{image.Height}";
        }

        foreach (var (name, r) in atlas.Sprites)
        {
            if (r.X < 0 || r.Y < 0 || r.Width <= 0 || r.Height <= 0 || r.X + r.Width > atlas.Width || r.Y + r.Height > atlas.Height)
            {
                return $"sprite {name} lies outside the image";
            }
        }

        if (atlas.Source is { } source && Path.Combine(folder, source) is var sourceFolder && Directory.Exists(sourceFolder)
            && AtlasBuilder.Fingerprint(sourceFolder) != atlas.Fingerprint)
        {
            return $"out of date with {source}: rebuild it with the Assets tool";
        }

        return null;
    }
}
