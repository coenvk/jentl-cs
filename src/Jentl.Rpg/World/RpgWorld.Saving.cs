using System.Text;
using System.Text.Json;
using Jentl.Puzzle;
using Jentl.Rpg.Quests;

namespace Jentl.Rpg.World;

/// <summary>
/// Saving and loading. A save records the seed, the player, the bag, equipment, flags, quests and recent
/// messages, and for every area visited: who and what is where, which triggers have fired, and what has been
/// explored. Areas are rebuilt by the game's area factory on load (so generated floors come back from the
/// seed) and then given their saved contents. Brains start afresh: a monster forgets where it last saw you.
/// </summary>
public sealed partial class RpgWorld
{
    public const int SaveVersion = 1;
    private const int SavedMessages = 12;

    /// <summary>The whole game as JSON text, for <see cref="Load"/>.</summary>
    public string Save()
    {
        using var buffer = new MemoryStream();
        using (var w = new Utf8JsonWriter(buffer))
        {
            w.WriteStartObject();
            w.WriteNumber("version", SaveVersion);
            w.WriteNumber("seed", Seed);
            w.WriteNumber("turn", Turn);
            w.WriteString("area", Area.Id);

            w.WriteStartObject("player");
            WriteActor(w, Player);
            w.WriteEndObject();

            w.WriteStartArray("inventory");
            foreach (var slot in State.Inventory.Stacks)
            {
                w.WriteStartObject();
                w.WriteString("item", slot.Item.Id);
                w.WriteNumber("count", slot.Count);
                w.WriteEndObject();
            }

            w.WriteEndArray();
            w.WriteStartObject("equipment");
            foreach (var (slot, item) in State.Equipment.Slots)
            {
                w.WriteString(slot, item.Id);
            }

            w.WriteEndObject();
            w.WriteStartArray("flags");
            foreach (var flag in State.Flags.Order(StringComparer.Ordinal))
            {
                w.WriteStringValue(flag);
            }

            w.WriteEndArray();
            w.WriteStartObject("variables");
            foreach (var (name, value) in State.Variables)
            {
                w.WriteNumber(name, value);
            }

            w.WriteEndObject();
            w.WriteStartArray("quests");
            foreach (var quest in State.Quests.All.Where(q => q.Status != QuestStatus.NotStarted))
            {
                w.WriteStartObject();
                w.WriteString("id", quest.Definition.Id);
                w.WriteString("status", quest.Status.ToString());
                w.WriteNumber("step", quest.Step);
                w.WriteStartArray("progress");
                for (var i = 0; i < quest.Definition.Objectives.Count; i++)
                {
                    w.WriteNumberValue(quest.Progress(i));
                }

                w.WriteEndArray();
                w.WriteEndObject();
            }

            w.WriteEndArray();
            w.WriteStartArray("messages");
            foreach (var message in _log.TakeLast(SavedMessages))
            {
                w.WriteStringValue(message);
            }

            w.WriteEndArray();
            w.WriteStartArray("areas");
            foreach (var area in _areas.Values)
            {
                WriteArea(w, area);
            }

            w.WriteEndArray();
            w.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>
    /// Restores a game saved with <see cref="Save"/>. Give the same content and area factory as the game was
    /// started with. Throws <see cref="ContentException"/> if the save is unreadable or refers to things the
    /// content no longer has.
    /// </summary>
    public static RpgWorld Load(RpgContent content, Func<RpgWorld, string, Area> createArea, string save, string playerKind = "player")
    {
        ArgumentNullException.ThrowIfNull(save);
        using var document = Json.Parse(save, "save");
        var root = document.RootElement;
        var version = root.Int("version", 0);
        if (version != SaveVersion)
        {
            throw new ContentException($"save: version {version} cannot be loaded (expected {SaveVersion}).");
        }

        var turn = root.Int("turn", 0);
        var world = new RpgWorld(content, createArea, playerKind, root.Int("seed", 0)) { Turn = turn };

        // Carry on with fresh rolls rather than replay the ones already used.
        world.Random = new Random(unchecked(world.Seed + turn * 7919));
        var state = world.State;
        foreach (var slot in root.Array("inventory"))
        {
            state.Inventory.Add(content.Items[slot.String("item", "save inventory")], slot.Int("count", 1));
        }

        if (root.TryGetProperty("equipment", out var equipment))
        {
            foreach (var slot in equipment.EnumerateObject())
            {
                state.Equipment.Equip(content.Items[slot.Value.GetString() ?? ""]);
            }
        }

        foreach (var flag in root.Array("flags"))
        {
            state.Flags.Add(flag.GetString() ?? "");
        }

        if (root.TryGetProperty("variables", out var variables))
        {
            foreach (var variable in variables.EnumerateObject())
            {
                state.AddVariable(variable.Name, variable.Value.GetInt32());
            }
        }

        foreach (var saved in root.Array("quests"))
        {
            var quest = state.Quests[saved.String("id", "save quests")];
            quest.Status = Enum.TryParse<QuestStatus>(saved.String("status", "save quests"), out var status)
                ? status
                : throw new ContentException($"save: quest '{quest.Definition.Id}' has an unknown status.");
            quest.Step = Math.Clamp(saved.Int("step", 0), 0, quest.Definition.Objectives.Count);
            var index = 0;
            foreach (var progress in saved.Array("progress").Take(quest.Definition.Objectives.Count))
            {
                quest.SetProgress(index++, progress.GetInt32());
            }
        }

        world._log.AddRange(root.Array("messages").Select(m => m.GetString() ?? ""));

        var player = root.TryGetProperty("player", out var p) ? p : throw new ContentException("save: no player.");
        ReadStats(world.Player, p);
        foreach (var saved in root.Array("areas"))
        {
            world.ReadArea(saved);
        }

        var area = world.GetArea(root.String("area", "save"));
        world._area = area;
        world.Player.Position = new Cell(player.Int("x", 0), player.Int("y", 0));
        area.Add(world.Player);
        world.UpdateSight();
        return world;
    }

    private static void WriteActor(Utf8JsonWriter w, Actor actor)
    {
        w.WriteNumber("x", actor.Position.X);
        w.WriteNumber("y", actor.Position.Y);
        w.WriteNumber("hp", actor.Hp);
        w.WriteNumber("maxHp", actor.MaxHp);
        w.WriteNumber("attack", actor.Attack);
        w.WriteNumber("defense", actor.Defense);
        w.WriteNumber("level", actor.Level);
        w.WriteNumber("xp", actor.Xp);
    }

    private static void ReadStats(Actor actor, JsonElement e)
    {
        actor.MaxHp = Math.Max(1, e.Int("maxHp", actor.MaxHp));
        actor.Hp = Math.Clamp(e.Int("hp", actor.Hp), 0, actor.MaxHp);
        actor.Attack = e.Int("attack", actor.Attack);
        actor.Defense = e.Int("defense", actor.Defense);
        actor.Level = Math.Max(1, e.Int("level", actor.Level));
        actor.Xp = e.Int("xp", actor.Xp);
    }

    private void WriteArea(Utf8JsonWriter w, Area area)
    {
        w.WriteStartObject();
        w.WriteString("id", area.Id);
        w.WriteString("explored", Bits(area.Map.Width, area.Map.Height, area.Explored));
        w.WriteStartArray("triggers");
        foreach (var trigger in area.Triggers)
        {
            w.WriteBooleanValue(trigger.HasFired);
        }

        w.WriteEndArray();
        w.WriteStartArray("actors");
        foreach (var actor in area.Actors.Where(a => a != Player && a.IsAlive))
        {
            w.WriteStartObject();
            w.WriteString("kind", actor.Kind);
            w.WriteString("tag", actor.Tag);
            w.WriteString("name", actor.Name);
            if (actor.Dialogue is { } dialogue)
            {
                w.WriteString("dialogue", dialogue);
            }

            w.WriteString("brain", actor.BrainName);
            w.WriteNumber("homeX", actor.Home.X);
            w.WriteNumber("homeY", actor.Home.Y);
            WriteActor(w, actor);
            w.WriteEndObject();
        }

        w.WriteEndArray();
        w.WriteStartArray("items");
        foreach (var item in area.Items)
        {
            w.WriteStartObject();
            w.WriteString("item", item.Item.Id);
            w.WriteNumber("count", item.Count);
            w.WriteNumber("x", item.Cell.X);
            w.WriteNumber("y", item.Cell.Y);
            w.WriteEndObject();
        }

        w.WriteEndArray();
        w.WriteEndObject();
    }

    private void ReadArea(JsonElement saved)
    {
        // Rebuild the area as the factory makes it, then swap in what was saved.
        var area = GetArea(saved.String("id", "save areas"));
        foreach (var actor in area.Actors.ToArray())
        {
            area.Remove(actor);
        }

        foreach (var item in area.Items.ToArray())
        {
            area.Remove(item);
        }

        foreach (var a in saved.Array("actors"))
        {
            var actor = Spawn(area, a.String("kind", "save actors"), new Cell(a.Int("x", 0), a.Int("y", 0)), a.OptionalString("tag"), a.OptionalString("brain"));
            actor.Name = a.OptionalString("name") ?? actor.Name;
            actor.Dialogue = a.OptionalString("dialogue");
            actor.Home = new Cell(a.Int("homeX", actor.Position.X), a.Int("homeY", actor.Position.Y));
            ReadStats(actor, a);
        }

        foreach (var i in saved.Array("items"))
        {
            Place(area, i.String("item", "save items"), Math.Max(1, i.Int("count", 1)), new Cell(i.Int("x", 0), i.Int("y", 0)));
        }

        var fired = saved.Array("triggers").Select(t => t.ValueKind == JsonValueKind.True).ToArray();
        for (var t = 0; t < Math.Min(fired.Length, area.Triggers.Count); t++)
        {
            area.Triggers[t].HasFired = fired[t];
        }

        area.Explored.Clear();
        area.Explored.UnionWith(Cells(area.Map.Width, area.Map.Height, saved.OptionalString("explored") ?? ""));
    }

    /// <summary>A set of cells as one bit per cell, row by row, in base64: a cellar floor in about 120 characters.</summary>
    private static string Bits(int width, int height, IReadOnlySet<Cell> cells)
    {
        var bytes = new byte[(width * height + 7) / 8];
        foreach (var cell in cells)
        {
            if (cell.X >= 0 && cell.Y >= 0 && cell.X < width && cell.Y < height)
            {
                var i = cell.Y * width + cell.X;
                bytes[i / 8] |= (byte)(1 << (i % 8));
            }
        }

        return Convert.ToBase64String(bytes);
    }

    private static IEnumerable<Cell> Cells(int width, int height, string bits)
    {
        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(bits);
        }
        catch (FormatException e)
        {
            throw new ContentException("save: an area's explored cells are unreadable.", e);
        }

        for (var i = 0; i < Math.Min(width * height, bytes.Length * 8); i++)
        {
            if ((bytes[i / 8] & (1 << (i % 8))) != 0)
            {
                yield return new Cell(i % width, i / width);
            }
        }
    }
}
