namespace Jentl.Rpg.Maps;

/// <summary>Checks Tiled files before they ship, for asset checking tools: they load, use only known tiles, and their images exist.</summary>
public static class MapCheck
{
    /// <summary>What's wrong with the map at <paramref name="path"/> (its tilesets read from beside it), or null if nothing.</summary>
    public static string? Map(string path)
    {
        var folder = Path.GetDirectoryName(path)!;
        var map = TmxReader.ReadMap(File.ReadAllText(path), name => File.ReadAllText(Path.Combine(folder, name)));
        foreach (var layer in map.Layers)
        {
            foreach (var cell in map.Cells)
            {
                if (layer[cell] is var gid and not 0 && map.TilesetFor(gid) is null)
                {
                    return $"layer {layer.Name} uses tile {gid} at {cell.X},{cell.Y}, which no tileset has";
                }
            }
        }

        return map.Tilesets.Select(t => t.Tileset.Image).FirstOrDefault(image => !File.Exists(Path.Combine(folder, image))) is { } missing
            ? $"its tileset image {missing} is missing"
            : null;
    }

    /// <summary>What's wrong with the tileset at <paramref name="path"/>, or null if nothing.</summary>
    public static string? Tileset(string path)
    {
        var tileset = TmxReader.ReadTileset(File.ReadAllText(path));
        return File.Exists(Path.Combine(Path.GetDirectoryName(path)!, tileset.Image)) ? null : $"its image {tileset.Image} is missing";
    }
}
