using Jentl.Puzzle;
using Jentl.Rpg.Items;
using Jentl.Rpg.Maps;
using Jentl.Rpg.Scripting;

namespace Jentl.Rpg.World;

/// <summary>Items lying on the floor, picked up by walking over them.</summary>
public sealed class FloorItem
{
    internal FloorItem(int id, ItemDefinition item, int count, Cell cell)
    {
        Id = id;
        Item = item;
        Count = count;
        Cell = cell;
    }

    /// <summary>Stays the same for the whole game, like an actor's.</summary>
    public int Id { get; }

    public ItemDefinition Item { get; }

    public int Count { get; internal set; }

    public Cell Cell { get; }
}

/// <summary>Stepping on <see cref="Cell"/> takes the player to the spawn point <see cref="Spawn"/> of area <see cref="Area"/>.</summary>
public sealed record Portal(Cell Cell, string Area, string Spawn);

/// <summary>Runs <see cref="Effects"/> when the player steps on <see cref="Cell"/> while <see cref="If"/> holds; once, unless <see cref="Repeat"/>.</summary>
public sealed class Trigger(Cell cell, Condition? condition, IReadOnlyList<Effect> effects, bool repeat)
{
    public Cell Cell { get; } = cell;

    public Condition? If { get; } = condition;

    public IReadOnlyList<Effect> Effects { get; } = effects;

    public bool Repeat { get; } = repeat;

    public bool HasFired { get; internal set; }
}

/// <summary>
/// One place in the world (the village, a dungeon floor): a tile map with the actors, items, portals and
/// triggers on it, and what the player has seen. Areas are kept after the player leaves, so they are as
/// they were on return.
/// </summary>
public sealed class Area
{
    private readonly List<Actor> _actors = [];
    private readonly List<FloorItem> _items = [];

    public Area(string id, TileMap map)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentNullException.ThrowIfNull(map);
        Id = id;
        Map = map;
        Name = map.Properties.GetString("name", id);
        IsLit = map.Properties.GetBool("lit", fallback: true);
    }

    public string Id { get; }

    public string Name { get; set; }

    public TileMap Map { get; }

    /// <summary>Lit areas are seen in full; dark ones only around the player, and remembered once seen.</summary>
    public bool IsLit { get; set; }

    public IReadOnlyList<Actor> Actors => _actors;

    public IReadOnlyList<FloorItem> Items => _items;

    public List<Portal> Portals { get; } = [];

    public List<Trigger> Triggers { get; } = [];

    /// <summary>Named places to arrive at, from the map's spawn objects.</summary>
    public Dictionary<string, Cell> Spawns { get; } = new(StringComparer.Ordinal);

    /// <summary>The cells the player sees right now.</summary>
    public IReadOnlySet<Cell> Visible { get; internal set; } = new HashSet<Cell>();

    /// <summary>The cells the player has ever seen here.</summary>
    public HashSet<Cell> Explored { get; } = [];

    public Actor? ActorAt(Cell cell) => _actors.FirstOrDefault(a => a.Position == cell && a.IsAlive);

    public IEnumerable<FloorItem> ItemsAt(Cell cell) => _items.Where(i => i.Cell == cell);

    public Portal? PortalAt(Cell cell) => Portals.FirstOrDefault(p => p.Cell == cell);

    /// <summary>Whether an actor could step onto <paramref name="cell"/>: on the map, not solid, and nobody there.</summary>
    public bool CanEnter(Cell cell) => Map.Contains(cell) && !Map.IsSolid(cell) && ActorAt(cell) is null;

    public bool IsVisible(Cell cell) => IsLit ? Map.Contains(cell) : Visible.Contains(cell);

    public bool IsExplored(Cell cell) => IsLit ? Map.Contains(cell) : Explored.Contains(cell);

    internal void Add(Actor actor) => _actors.Add(actor);

    internal void Remove(Actor actor) => _actors.Remove(actor);

    internal void Add(FloorItem item) => _items.Add(item);

    internal void Remove(FloorItem item) => _items.Remove(item);
}
