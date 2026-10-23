using System.Text.Json;
using Jentl.Puzzle;

namespace Jentl.Rpg.World;

public enum Faction
{
    Player,

    /// <summary>Townsfolk and the like: never attacked by bumping, talked to if they have a dialogue.</summary>
    Friendly,

    /// <summary>Monsters: attack the player, and are attacked by walking into them.</summary>
    Hostile,
}

/// <summary>An item an actor may leave behind when defeated.</summary>
public sealed record Drop(string Item, double Chance, int Count);

/// <summary>
/// A kind of actor, from the actor file:
/// <code>
/// { "actors": [
///   { "id": "rat", "name": "Rat", "sprite": "rat", "faction": "hostile", "hp": 4, "attack": 2,
///     "defense": 0, "sight": 6, "brain": "hunter", "xp": 2, "drops": [ { "item": "coins", "chance": 0.5 } ] },
///   { "id": "elder", "name": "Elder Maren", "sprite": "elder", "faction": "friendly", "dialogue": "elder" } ] }
/// </code>
/// <c>brain</c> names a behaviour registered with <see cref="Brains"/> (still, wander or hunter by default).
/// </summary>
public sealed record ActorDefinition(
    string Id,
    string Name,
    string Sprite,
    Faction Faction,
    int Hp,
    int Attack,
    int Defense,
    int Sight,
    string Brain,
    string? Dialogue,
    int Xp,
    IReadOnlyList<Drop> Drops)
{
    public static IReadOnlyList<ActorDefinition> FromJson(string json)
    {
        using var document = Json.Parse(json, "actors");
        return [.. document.RootElement.Array("actors").Select(Read)];
    }

    private static ActorDefinition Read(JsonElement e, int index)
    {
        var id = e.String("id", $"actors[{index}]");
        var where = $"actor '{id}'";
        var faction = e.OptionalString("faction") ?? "friendly";
        return new ActorDefinition(
            id,
            e.OptionalString("name") ?? id,
            e.OptionalString("sprite") ?? id,
            Enum.TryParse<Faction>(faction, ignoreCase: true, out var parsed)
                ? parsed
                : throw new ContentException($"{where}: unknown faction '{faction}' (use player, friendly or hostile)."),
            Math.Max(1, e.Int("hp", 10)),
            e.Int("attack", 1),
            e.Int("defense", 0),
            e.Int("sight", 6),
            e.OptionalString("brain") ?? "still",
            e.OptionalString("dialogue"),
            e.Int("xp", 0),
            [.. e.Array("drops").Select(d => new Drop(d.String("item", where), d.Double("chance", 1), Math.Max(1, d.Int("count", 1))))]);
    }
}

/// <summary>
/// Someone on the map: the player, a villager or a monster. Its <see cref="Id"/> stays the same for the
/// whole game, which lets a screen animate it.
/// </summary>
public sealed class Actor
{
    internal Actor(int id, ActorDefinition definition, Cell position, IBrain brain, string brainName, string? tag)
    {
        BrainName = brainName;
        Id = id;
        Definition = definition;
        Position = position;
        Home = position;
        Brain = brain;
        Tag = tag ?? definition.Id;
        Name = definition.Name;
        Dialogue = definition.Dialogue;
        MaxHp = definition.Hp;
        Hp = definition.Hp;
        Attack = definition.Attack;
        Defense = definition.Defense;
    }

    public int Id { get; }

    public ActorDefinition Definition { get; }

    public string Kind => Definition.Id;

    /// <summary>Its name on the map (the Tiled object's name), so quests can refer to one villager; the kind by default.</summary>
    public string Tag { get; }

    public string Name { get; set; }

    public string Sprite => Definition.Sprite;

    public Faction Faction => Definition.Faction;

    public Cell Position { get; internal set; }

    /// <summary>Where it started; wandering actors stay near it.</summary>
    public Cell Home { get; internal set; }

    public IBrain Brain { get; set; }

    /// <summary>The registered name of its brain (see <see cref="Brains"/>), which is what a save game records.</summary>
    public string BrainName { get; }

    public string? Dialogue { get; set; }

    public int Hp { get; internal set; }

    public int MaxHp { get; internal set; }

    /// <summary>Attack without equipment.</summary>
    public int Attack { get; internal set; }

    /// <summary>Defense without equipment.</summary>
    public int Defense { get; internal set; }

    public int Level { get; internal set; } = 1;

    public int Xp { get; internal set; }

    public bool IsAlive => Hp > 0;

    /// <summary>Restores up to <paramref name="amount"/> health; returns how much was restored.</summary>
    public int Heal(int amount)
    {
        var healed = Math.Clamp(amount, 0, MaxHp - Hp);
        Hp += healed;
        return healed;
    }

    public override string ToString() => $"{Name} #{Id} at {Position}";
}

/// <summary>Every kind of actor in the game.</summary>
public sealed class ActorCatalog
{
    private readonly Dictionary<string, ActorDefinition> _actors = new(StringComparer.Ordinal);

    public ActorCatalog(IEnumerable<ActorDefinition> actors)
    {
        ArgumentNullException.ThrowIfNull(actors);
        foreach (var actor in actors)
        {
            if (!_actors.TryAdd(actor.Id, actor))
            {
                throw new ContentException($"The actor '{actor.Id}' is defined twice.");
            }
        }
    }

    public IEnumerable<ActorDefinition> All => _actors.Values;

    public ActorDefinition this[string id] =>
        _actors.TryGetValue(id, out var actor) ? actor : throw new ContentException($"There is no actor '{id}'.");

    public bool Contains(string id) => _actors.ContainsKey(id);
}
