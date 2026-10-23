using System.Text.Json;
using Jentl.Puzzle;
using Jentl.Rpg.Dialogue;
using Jentl.Rpg.Items;
using Jentl.Rpg.Maps;
using Jentl.Rpg.Quests;
using Jentl.Rpg.Scripting;

namespace Jentl.Rpg.World;

/// <summary>Something that happened during a turn, for the screen to show (a step to animate, a hit to flash).</summary>
public abstract record WorldEvent;

public sealed record ActorMoved(Actor Actor, Cell From, Cell To) : WorldEvent;

public sealed record ActorAttacked(Actor Attacker, Actor Target, int Damage) : WorldEvent;

public sealed record ActorDied(Actor Actor) : WorldEvent;

public sealed record ItemPickedUp(ItemDefinition Item, int Count) : WorldEvent;

public sealed record AreaEntered(Area Area) : WorldEvent;

public sealed record LevelGained(int Level) : WorldEvent;

/// <summary>The outcome of one player command.</summary>
/// <param name="TookTurn">Whether time passed (the other actors acted).</param>
/// <param name="TalkTo">The player walked into someone to talk to; open <see cref="RpgWorld.Talk"/> for them.</param>
public sealed record TurnResult(IReadOnlyList<WorldEvent> Events, bool TookTurn, Actor? TalkTo = null)
{
    public static TurnResult Nothing { get; } = new([], TookTurn: false);
}

/// <summary>
/// A turn-based world: the player acts (step, wait, use an item), then every other actor in the area
/// takes its turn through its brain. Walking into a monster attacks it, into a villager with a dialogue
/// talks to them, onto items picks them up, onto a portal travels. Areas are created on first visit by
/// the game's area factory and kept afterwards.
/// </summary>
public sealed partial class RpgWorld
{
    private const int LogLength = 60;
    private const int PlayerSight = 7;

    private readonly Func<RpgWorld, string, Area> _createArea;
    private readonly Dictionary<string, Area> _areas = new(StringComparer.Ordinal);
    private readonly List<string> _log = [];
    private readonly List<WorldEvent> _events = [];
    private int _nextId = 1;
    private Area? _area;

    /// <param name="content">Items, actors, quests and dialogues.</param>
    /// <param name="createArea">Builds an area by id on its first visit (load a map, or generate a dungeon floor).</param>
    /// <param name="playerKind">The actor definition used for the player.</param>
    /// <param name="seed">Drives combat rolls, drops and wandering, so a game can be replayed.</param>
    public RpgWorld(RpgContent content, Func<RpgWorld, string, Area> createArea, string playerKind = "player", int seed = 0)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(createArea);
        Content = content;
        _createArea = createArea;
        Seed = seed;
        Random = new Random(seed);
        State = new RpgState(content.Items, content.Quests);
        State.Said += Log;
        Player = new Actor(_nextId++, content.Actors[playerKind], default, new StillBrain(), "still", "player");
        State.Player = Player;
    }

    public RpgContent Content { get; }

    public RpgState State { get; }

    public Actor Player { get; }

    /// <summary>The seed the game started with; a game factory can lay out areas from it.</summary>
    public int Seed { get; }

    public Random Random { get; private set; }

    /// <summary>The area the player is in.</summary>
    public Area Area => _area ?? throw new InvalidOperationException("Enter an area first.");

    public IReadOnlyDictionary<string, Area> Areas => _areas;

    public int Turn { get; private set; }

    /// <summary>The player has died.</summary>
    public bool IsOver => !Player.IsAlive;

    /// <summary>Recent messages, oldest first.</summary>
    public IReadOnlyList<string> Messages => _log;

    public int AttackOf(Actor actor) => (actor ?? throw new ArgumentNullException(nameof(actor))).Attack + (actor == Player ? State.Equipment.Attack : 0);

    public int DefenseOf(Actor actor) => (actor ?? throw new ArgumentNullException(nameof(actor))).Defense + (actor == Player ? State.Equipment.Defense : 0);

    /// <summary>Experience needed for the next level.</summary>
    public int XpForNextLevel => Player.Level * 10;

    public void Log(string message)
    {
        _log.Add(message);
        if (_log.Count > LogLength)
        {
            _log.RemoveAt(0);
        }
    }

    /// <summary>Gets an area, creating it on first use.</summary>
    public Area GetArea(string id)
    {
        if (!_areas.TryGetValue(id, out var area))
        {
            area = _createArea(this, id);
            _areas[id] = area;
        }

        return area;
    }

    /// <summary>Puts the player at spawn point <paramref name="spawn"/> of area <paramref name="areaId"/>.</summary>
    public TurnResult Enter(string areaId, string spawn = "start")
    {
        Arrive(areaId, spawn);
        return Finish(tookTurn: false);
    }

    private void Arrive(string areaId, string spawn)
    {
        var area = GetArea(areaId);
        var cell = area.Spawns.TryGetValue(spawn, out var found)
            ? found
            : throw new ContentException($"Area '{areaId}' has no spawn point '{spawn}'.");
        _area?.Remove(Player);
        _area = area;
        Player.Position = Nearest(area, cell);
        area.Add(Player);
        _events.Add(new AreaEntered(area));
        UpdateSight();
        State.Notify(new ReachedTrigger(area.Id));
    }

    /// <summary>Walks, attacks or talks in <paramref name="direction"/>.</summary>
    public TurnResult Step(GridDirection direction)
    {
        if (IsOver)
        {
            return TurnResult.Nothing;
        }

        var target = Player.Position.Step(direction);
        if (Area.ActorAt(target) is { } other)
        {
            if (other.Faction == Faction.Hostile)
            {
                Strike(Player, other);
                return EndTurn();
            }

            return other.Dialogue is not null ? new TurnResult([], TookTurn: false, other) : TurnResult.Nothing;
        }

        if (!Area.CanEnter(target))
        {
            return TurnResult.Nothing;
        }

        Move(Player, target);
        PickUp(target);
        if (Area.PortalAt(target) is { } portal)
        {
            Turn++;
            Arrive(portal.Area, portal.Spawn);
            return Finish(tookTurn: true);
        }

        foreach (var trigger in Area.Triggers.Where(t => t.Cell == target && (t.Repeat || !t.HasFired) && (t.If?.Test(State) ?? true)))
        {
            trigger.HasFired = true;
            State.Run(trigger.Effects);
        }

        return EndTurn();
    }

    /// <summary>Lets a turn pass.</summary>
    public TurnResult Wait() => IsOver ? TurnResult.Nothing : EndTurn();

    /// <summary>Uses an item from the bag (drinks the potion); takes a turn.</summary>
    public TurnResult Use(string itemId)
    {
        var item = Content.Items[itemId];
        if (IsOver || !item.IsUsable || !State.Inventory.Has(itemId))
        {
            return TurnResult.Nothing;
        }

        if (item.Consumable)
        {
            State.Inventory.Remove(itemId);
        }

        State.Run(item.Use);
        State.Refresh();
        return EndTurn();
    }

    /// <summary>Wears or wields an item from the bag, putting what was in its slot back in the bag; takes a turn.</summary>
    public TurnResult Equip(string itemId)
    {
        var item = Content.Items[itemId];
        if (IsOver || !item.IsEquippable || !State.Inventory.Remove(itemId))
        {
            return TurnResult.Nothing;
        }

        if (State.Equipment.Equip(item) is { } previous)
        {
            State.Inventory.Add(previous);
        }

        Log($"You equip the {item.Name}.");
        State.Refresh();
        return EndTurn();
    }

    /// <summary>Takes off what is in <paramref name="slot"/>, if the bag has room; takes a turn.</summary>
    public TurnResult Unequip(string slot)
    {
        if (IsOver || State.Equipment[slot] is not { } item || State.Inventory.Room(item) < 1)
        {
            return TurnResult.Nothing;
        }

        State.Equipment.Unequip(slot);
        State.Inventory.Add(item);
        Log($"You take off the {item.Name}.");
        return EndTurn();
    }

    /// <summary>Starts a conversation with <paramref name="actor"/> (and tells quests about it).</summary>
    public DialogueRunner Talk(Actor actor)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var dialogue = Content.Dialogue(actor.Dialogue ?? throw new ArgumentException($"{actor.Name} has nothing to say.", nameof(actor)));
        State.Notify(new TalkedTrigger(actor.Tag, actor.Kind));
        return new DialogueRunner(dialogue, State);
    }

    /// <summary>Places a new actor of <paramref name="kind"/> in <paramref name="area"/>.</summary>
    public Actor Spawn(Area area, string kind, Cell cell, string? tag = null, string? brain = null)
    {
        ArgumentNullException.ThrowIfNull(area);
        var definition = Content.Actors[kind];
        brain ??= definition.Brain;
        var actor = new Actor(_nextId++, definition, cell, Brains.Create(brain), brain, tag);
        area.Add(actor);
        return actor;
    }

    /// <summary>Leaves <paramref name="count"/> of an item on the floor of <paramref name="area"/>.</summary>
    public FloorItem Place(Area area, string item, int count, Cell cell)
    {
        ArgumentNullException.ThrowIfNull(area);
        var placed = new FloorItem(_nextId++, Content.Items[item], count, cell);
        area.Add(placed);
        return placed;
    }

    /// <summary>
    /// Builds an area from a Tiled map, reading its object layers: <c>spawn</c> objects name arrival
    /// points; <c>actor</c> objects place an actor (property <c>kind</c>, else the object name; optional
    /// <c>dialogue</c>, <c>brain</c> and <c>name</c>); <c>item</c> objects place items (<c>item</c>,
    /// <c>count</c>); <c>portal</c> objects lead to <c>area</c> at <c>spawn</c>; <c>trigger</c> objects run the
    /// JSON effects in <c>do</c> when stepped on (optional JSON condition <c>if</c>, and <c>repeat</c>).
    /// </summary>
    public Area LoadArea(string id, TileMap map)
    {
        var area = new Area(id, map);
        foreach (var o in map.Objects)
        {
            var where = $"map '{id}', {o}";
            switch (o.Type)
            {
                case "spawn":
                    area.Spawns[o.Name] = o.Cell;
                    break;
                case "actor":
                    var actor = Spawn(area, o.Properties.GetString("kind") ?? o.Name, o.Cell, string.IsNullOrEmpty(o.Name) ? null : o.Name, o.Properties.GetString("brain"));
                    actor.Dialogue = o.Properties.GetString("dialogue") ?? actor.Dialogue;
                    actor.Name = o.Properties.GetString("name") ?? actor.Name;
                    break;
                case "item":
                    Place(area, o.Properties.GetString("item") ?? o.Name, Math.Max(1, o.Properties.GetInt("count", 1)), o.Cell);
                    break;
                case "portal":
                    area.Portals.Add(new Portal(
                        o.Cell,
                        o.Properties.GetString("area") ?? throw new ContentException($"{where}: a portal needs an 'area' property."),
                        o.Properties.GetString("spawn", "start")));
                    break;
                case "trigger":
                    using (var effects = Json.Parse(o.Properties.GetString("do", "[]"), where))
                    {
                        Condition? condition = null;
                        if (o.Properties.GetString("if") is { } test)
                        {
                            using var parsed = Json.Parse(test, where);
                            condition = Condition.Parse(parsed.RootElement, where);
                        }

                        area.Triggers.Add(new Trigger(
                            o.Cell,
                            condition,
                            [.. effects.RootElement.EnumerateArray().Select(e => Effect.Parse(e, where))],
                            o.Properties.GetBool("repeat")));
                    }

                    break;
            }
        }

        return area;
    }

    private TurnResult EndTurn()
    {
        Turn++;
        foreach (var actor in Area.Actors.Where(a => a != Player && a.IsAlive).ToArray())
        {
            if (!actor.IsAlive || IsOver)
            {
                continue;
            }

            switch (actor.Brain.Decide(actor, this))
            {
                case MoveAction move when Area.CanEnter(actor.Position.Step(move.Direction)):
                    Move(actor, actor.Position.Step(move.Direction));
                    break;
                case AttackAction attack when attack.Target.IsAlive && Pathfinder.Distance(actor.Position, attack.Target.Position) == 1:
                    Strike(actor, attack.Target);
                    break;
            }
        }

        UpdateSight();
        State.Refresh();
        return Finish(tookTurn: true);
    }

    private TurnResult Finish(bool tookTurn)
    {
        var result = new TurnResult([.. _events], tookTurn);
        _events.Clear();
        return result;
    }

    private void Move(Actor actor, Cell to)
    {
        var from = actor.Position;
        actor.Position = to;
        _events.Add(new ActorMoved(actor, from, to));
    }

    private void Strike(Actor attacker, Actor target)
    {
#pragma warning disable CA5394 // Combat rolls, not security relevant.
        var damage = Math.Max(1, AttackOf(attacker) - DefenseOf(target) + Random.Next(-1, 2));
#pragma warning restore CA5394
        target.Hp = Math.Max(0, target.Hp - damage);
        _events.Add(new ActorAttacked(attacker, target, damage));
        Log(attacker == Player ? $"You hit the {target.Name} for {damage}."
            : target == Player ? $"The {attacker.Name} hits you for {damage}."
            : $"The {attacker.Name} hits the {target.Name}.");
        if (!target.IsAlive)
        {
            Die(target, attacker);
        }
    }

    private void Die(Actor actor, Actor killer)
    {
        _events.Add(new ActorDied(actor));
        if (actor == Player)
        {
            Log("You have fallen...");
            return;
        }

        Area.Remove(actor);
        Log($"The {actor.Name} is defeated.");
#pragma warning disable CA5394 // Loot rolls, not security relevant.
        foreach (var drop in actor.Definition.Drops.Where(d => Random.NextDouble() < d.Chance))
#pragma warning restore CA5394
        {
            Place(Area, drop.Item, drop.Count, actor.Position);
        }

        if (killer == Player)
        {
            GainXp(actor.Definition.Xp);
        }

        State.Notify(new KilledTrigger(actor.Kind));
    }

    private void GainXp(int xp)
    {
        Player.Xp += xp;
        while (Player.Xp >= XpForNextLevel)
        {
            Player.Xp -= XpForNextLevel;
            Player.Level++;
            Player.MaxHp += 5;
            Player.Hp += 5;
            Player.Attack++;
            _events.Add(new LevelGained(Player.Level));
            Log($"You reach level {Player.Level}!");
        }
    }

    private void PickUp(Cell cell)
    {
        foreach (var item in Area.ItemsAt(cell).ToArray())
        {
            var left = State.Inventory.Add(item.Item, item.Count);
            var taken = item.Count - left;
            if (taken > 0)
            {
                _events.Add(new ItemPickedUp(item.Item, taken));
                Log($"You pick up {RpgState.Describe(item.Item, taken)}.");
            }

            if (left == 0)
            {
                Area.Remove(item);
            }
            else
            {
                item.Count = left;
                Log($"Your bag is full; the {item.Item.Name} stays here.");
            }
        }
    }

    private void UpdateSight()
    {
        var area = Area;
        if (area.IsLit)
        {
            return;
        }

        var visible = FieldOfView.Compute(Player.Position, PlayerSight, area.Map.IsOpaque);
        area.Visible = visible;
        area.Explored.UnionWith(visible);
    }

    // A spawn point may be occupied (someone wandered onto it); arrive next to it instead.
    private static Cell Nearest(Area area, Cell cell)
    {
        if (area.CanEnter(cell))
        {
            return cell;
        }

        for (var radius = 1; radius < 6; radius++)
        {
            for (var dy = -radius; dy <= radius; dy++)
            {
                for (var dx = -radius; dx <= radius; dx++)
                {
                    var near = new Cell(cell.X + dx, cell.Y + dy);
                    if (area.CanEnter(near))
                    {
                        return near;
                    }
                }
            }
        }

        return cell;
    }
}
