using Jentl.Puzzle;

namespace Jentl.Rpg.Maps;

/// <summary>One layer of tiles. A global tile id (gid) of 0 means no tile.</summary>
public sealed class TileLayer
{
    private readonly int[] _gids;

    public TileLayer(string name, int width, int height, int[]? gids = null, Properties? properties = null, bool visible = true)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        gids ??= new int[width * height];
        if (gids.Length != width * height)
        {
            throw new ArgumentException($"Layer '{name}' has {gids.Length} tiles; a {width}x{height} layer needs {width * height}.", nameof(gids));
        }

        Name = name;
        Width = width;
        Height = height;
        _gids = gids;
        Properties = properties ?? Properties.Empty;
        Visible = visible;
    }

    public string Name { get; }

    public int Width { get; }

    public int Height { get; }

    public Properties Properties { get; }

    public bool Visible { get; }

    public int this[Cell cell]
    {
        get => Contains(cell) ? _gids[cell.Y * Width + cell.X] : 0;
        set => _gids[Index(cell)] = value;
    }

    public bool Contains(Cell cell) => cell.X >= 0 && cell.Y >= 0 && cell.X < Width && cell.Y < Height;

    private int Index(Cell cell) =>
        Contains(cell) ? cell.Y * Width + cell.X : throw new ArgumentOutOfRangeException(nameof(cell), cell, $"Outside layer '{Name}'.");
}

/// <summary>
/// Something placed on the map in an object layer: a spawn point, an NPC, a door. <see cref="Type"/> is
/// Tiled's class (or older "type"), and <see cref="Cell"/> is the tile it stands on.
/// </summary>
public sealed record MapObject(int Id, string Name, string Type, Cell Cell, int Width, int Height, Properties Properties)
{
    public override string ToString() => $"{Type} '{Name}' at {Cell}";
}

public sealed record ObjectLayer(string Name, IReadOnlyList<MapObject> Objects);

/// <summary>A tile map: layers of tiles drawn bottom to top, object layers, and the tilesets they use.</summary>
public sealed class TileMap
{
    public TileMap(
        int width,
        int height,
        int tileWidth,
        int tileHeight,
        IReadOnlyList<MapTileset> tilesets,
        IReadOnlyList<TileLayer> layers,
        IReadOnlyList<ObjectLayer>? objectLayers = null,
        Properties? properties = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentNullException.ThrowIfNull(tilesets);
        ArgumentNullException.ThrowIfNull(layers);
        Width = width;
        Height = height;
        TileWidth = tileWidth;
        TileHeight = tileHeight;
        Tilesets = tilesets;
        Layers = layers;
        ObjectLayers = objectLayers ?? [];
        Properties = properties ?? Properties.Empty;
    }

    public int Width { get; }

    public int Height { get; }

    public int TileWidth { get; }

    public int TileHeight { get; }

    public IReadOnlyList<MapTileset> Tilesets { get; }

    public IReadOnlyList<TileLayer> Layers { get; }

    public IReadOnlyList<ObjectLayer> ObjectLayers { get; }

    public Properties Properties { get; }

    public IEnumerable<MapObject> Objects => ObjectLayers.SelectMany(l => l.Objects);

    public IEnumerable<Cell> Cells => Enumerable.Range(0, Width * Height).Select(i => new Cell(i % Width, i / Width));

    public bool Contains(Cell cell) => cell.X >= 0 && cell.Y >= 0 && cell.X < Width && cell.Y < Height;

    public TileLayer Layer(string name) =>
        Layers.FirstOrDefault(l => l.Name == name) ?? throw new KeyNotFoundException($"The map has no tile layer '{name}'.");

    /// <summary>The tileset a global tile id belongs to, or null for 0 or an unknown id.</summary>
    public MapTileset? TilesetFor(int gid) => gid == 0 ? null : Tilesets.LastOrDefault(t => t.FirstGid <= gid && t.Contains(gid));

    /// <summary>The properties of the tile with global id <paramref name="gid"/>.</summary>
    public Properties TileProperties(int gid) =>
        TilesetFor(gid) is { } set ? set.Tileset.TileProperties(gid - set.FirstGid) : Properties.Empty;

    /// <summary>The global id of the tile named <paramref name="name"/> in any of the map's tilesets.</summary>
    public int Gid(string name)
    {
        foreach (var set in Tilesets)
        {
            if (set.Tileset.TryFind(name, out var id))
            {
                return set.FirstGid + id;
            }
        }

        throw new KeyNotFoundException($"No tileset of this map has a tile named '{name}'.");
    }

    /// <summary>Whether any tile at <paramref name="cell"/> has <paramref name="property"/> set; outside the map counts as set.</summary>
    public bool HasFlag(Cell cell, string property) =>
        !Contains(cell) || Layers.Any(l => l[cell] != 0 && TileProperties(l[cell]).GetBool(property));

    /// <summary>A tile with the <c>solid</c> property blocks movement.</summary>
    public bool IsSolid(Cell cell) => HasFlag(cell, "solid");

    /// <summary>A tile with the <c>opaque</c> property blocks sight.</summary>
    public bool IsOpaque(Cell cell) => HasFlag(cell, "opaque");

    /// <summary>The names of the tiles at <paramref name="cell"/>, bottom layer first.</summary>
    public IEnumerable<string> TileNames(Cell cell) =>
        Layers.Where(l => l[cell] != 0).Select(l => TileProperties(l[cell]).GetString("name")).OfType<string>();
}
