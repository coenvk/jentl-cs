namespace Jentl.Rpg.Maps;

/// <summary>
/// A grid of tiles cut from one image, with optional properties per tile. Tile properties drive the
/// rules: <c>solid</c> blocks movement, <c>opaque</c> blocks sight, and <c>name</c> lets code find a tile
/// ("stone_wall") without hard-coding its number.
/// </summary>
public sealed class Tileset
{
    private readonly Dictionary<int, Properties> _tiles;
    private readonly Dictionary<string, int> _byName = new(StringComparer.Ordinal);
    private readonly Dictionary<int, IReadOnlyList<TileFrame>> _animations;

    public Tileset(
        string name,
        int tileWidth,
        int tileHeight,
        int columns,
        int tileCount,
        string image,
        IReadOnlyDictionary<int, Properties>? tiles = null,
        IReadOnlyDictionary<int, IReadOnlyList<TileFrame>>? animations = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tileWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tileHeight);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(columns);
        Name = name;
        TileWidth = tileWidth;
        TileHeight = tileHeight;
        Columns = columns;
        TileCount = tileCount;
        Image = image;
        _tiles = new Dictionary<int, Properties>(tiles ?? new Dictionary<int, Properties>());
        _animations = new Dictionary<int, IReadOnlyList<TileFrame>>(animations ?? new Dictionary<int, IReadOnlyList<TileFrame>>());
        foreach (var (id, properties) in _tiles)
        {
            if (properties.GetString("name") is { } tileName)
            {
                _byName.TryAdd(tileName, id);
            }
        }
    }

    public string Name { get; }

    public int TileWidth { get; }

    public int TileHeight { get; }

    public int Columns { get; }

    public int TileCount { get; }

    /// <summary>The image file, relative to the tileset (or map) file it came from.</summary>
    public string Image { get; }

    /// <summary>The properties of local tile <paramref name="id"/> (0 is the image's top-left tile).</summary>
    public Properties TileProperties(int id) => _tiles.GetValueOrDefault(id) ?? Properties.Empty;

    /// <summary>The local id of the tile whose <c>name</c> property is <paramref name="name"/>.</summary>
    public int Find(string name) =>
        _byName.TryGetValue(name, out var id) ? id : throw new KeyNotFoundException($"Tileset '{Name}' has no tile named '{name}'.");

    public bool TryFind(string name, out int id) => _byName.TryGetValue(name, out id);

    /// <summary>The frames of tile <paramref name="id"/>'s animation (from Tiled), or empty if it does not animate.</summary>
    public IReadOnlyList<TileFrame> Animation(int id) => _animations.GetValueOrDefault(id) ?? [];

    /// <summary>Which local tile shows for <paramref name="id"/> <paramref name="time"/> into its animation; <paramref name="id"/> itself if it does not animate.</summary>
    public int Frame(int id, TimeSpan time)
    {
        if (!_animations.TryGetValue(id, out var frames) || frames.Count == 0)
        {
            return id;
        }

        var length = frames.Sum(f => f.Milliseconds);
        if (length <= 0)
        {
            return frames[0].Tile;
        }

        var into = (long)time.TotalMilliseconds % length;
        foreach (var frame in frames)
        {
            if (into < frame.Milliseconds)
            {
                return frame.Tile;
            }

            into -= frame.Milliseconds;
        }

        return frames[^1].Tile;
    }

    /// <summary>Where local tile <paramref name="id"/> sits in the image, in tiles.</summary>
    public (int Column, int Row) Location(int id) => (id % Columns, id / Columns);
}

/// <summary>One frame of a tile animation: a local tile id shown for a number of milliseconds.</summary>
public readonly record struct TileFrame(int Tile, int Milliseconds);

/// <summary>A tileset as used by a map: global tile ids from <see cref="FirstGid"/> on belong to it.</summary>
public sealed record MapTileset(int FirstGid, Tileset Tileset)
{
    public bool Contains(int gid) => gid >= FirstGid && gid < FirstGid + Math.Max(1, Tileset.TileCount);

    public int Gid(string name) => FirstGid + Tileset.Find(name);
}
