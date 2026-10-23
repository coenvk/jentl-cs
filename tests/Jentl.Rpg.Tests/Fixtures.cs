using Jentl.Puzzle;
using Jentl.Rpg.Maps;
using Jentl.Rpg.World;

namespace Jentl.Rpg.Tests;

/// <summary>Small maps and content for tests.</summary>
internal static class Fixtures
{
    public static readonly Tileset Tiles = new("test", 16, 16, 4, 4, "tiles.png", new Dictionary<int, Properties>
    {
        [0] = Props(("name", "floor")),
        [1] = Props(("name", "wall"), ("solid", "true"), ("opaque", "true")),
        [2] = Props(("name", "water"), ("solid", "true")),
    });

    public const string Items = """
        { "items": [
          { "id": "potion", "name": "Healing potion", "stack": 5, "use": [ { "heal": 5 } ] },
          { "id": "coins", "name": "Coins", "stack": 99 },
          { "id": "herb", "name": "Herb", "stack": 10 },
          { "id": "sword", "name": "Sword", "slot": "weapon", "attack": 3 },
          { "id": "vest", "name": "Vest", "slot": "armor", "defense": 1 },
        ] }
        """;

    public const string Actors = """
        { "actors": [
          { "id": "player", "name": "Hero", "faction": "player", "hp": 20, "attack": 2 },
          { "id": "rat", "name": "Rat", "faction": "hostile", "hp": 3, "attack": 1, "brain": "hunter", "xp": 10,
            "drops": [ { "item": "coins", "chance": 1, "count": 2 } ] },
          { "id": "spider", "name": "Spider", "faction": "hostile", "hp": 9, "attack": 1, "sight": 6, "brain": "skittish" },
          { "id": "dummy", "name": "Dummy", "faction": "hostile", "hp": 50, "attack": 0, "brain": "still" },
          { "id": "elder", "name": "Elder", "faction": "friendly", "dialogue": "elder" },
        ] }
        """;

    public const string Quests = """
        { "quests": [
          { "id": "herbs", "title": "Herbs", "objectives": [
              { "text": "Gather two herbs", "collect": "herb", "count": 2 },
              { "text": "Return to the elder", "talk": "elder" } ],
            "rewards": [ { "give": "potion" } ] },
          { "id": "rats", "title": "Rats", "autoComplete": true, "objectives": [
              { "text": "Defeat two rats", "kill": "rat", "count": 2 } ],
            "rewards": [ { "setFlag": "rats_done" } ] },
        ] }
        """;

    public const string Dialogues = """
        { "dialogues": [ { "id": "elder", "speaker": "Elder", "nodes": {
            "start": { "goto": [
              { "if": { "quest": "herbs", "is": "ready" }, "node": "thanks" },
              { "if": { "quest": "herbs", "is": "active" }, "node": "waiting" },
              { "if": { "quest": "herbs", "is": "completed" }, "node": "done" },
              { "node": "hello" } ] },
            "hello": { "text": "Hello.", "choices": [
              { "text": "Any work?", "next": "work", "do": [ { "startQuest": "herbs" } ] },
              { "text": "Secret", "if": { "flag": "knows_secret" }, "next": "secret" },
              { "text": "Bye." } ] },
            "work": { "text": "Bring two herbs.", "next": "bye" },
            "bye": { "text": "Off you go." },
            "waiting": { "text": "Still waiting." },
            "thanks": { "text": "Thank you!", "do": [ { "take": "herb", "count": 2 }, { "completeQuest": "herbs" } ] },
            "done": { "text": "Good to see you." },
            "secret": { "text": "Shh." } } } ] }
        """;

    public static RpgContent Content() => RpgContent.FromJson(Items, Actors, Quests, Dialogues);

    /// <summary>A map from rows of text: '#' wall, '~' water, anything else floor.</summary>
    public static TileMap Map(params string[] rows)
    {
        var width = rows.Max(r => r.Length);
        var layer = new TileLayer("ground", width, rows.Length);
        for (var y = 0; y < rows.Length; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var c = x < rows[y].Length ? rows[y][x] : '#';
                layer[new Cell(x, y)] = 1 + (c == '#' ? 1 : c == '~' ? 2 : 0);
            }
        }

        return new TileMap(width, rows.Length, 16, 16, [new MapTileset(1, Tiles)], [layer]);
    }

    /// <summary>A world with one area built from <paramref name="rows"/>; '@' is the player's start, 'r' a rat, 'd' a dummy, 'e' the elder, 's' a spider, '$' coins.</summary>
    public static RpgWorld World(params string[] rows)
    {
        var world = new RpgWorld(Content(), (w, id) =>
        {
            var area = new Area(id, Map(rows));
            for (var y = 0; y < rows.Length; y++)
            {
                for (var x = 0; x < rows[y].Length; x++)
                {
                    var cell = new Cell(x, y);
                    switch (rows[y][x])
                    {
                        case '@':
                            area.Spawns["start"] = cell;
                            break;
                        case 'r':
                            w.Spawn(area, "rat", cell);
                            break;
                        case 's':
                            w.Spawn(area, "spider", cell);
                            break;
                        case 'd':
                            w.Spawn(area, "dummy", cell);
                            break;
                        case 'e':
                            w.Spawn(area, "elder", cell, "elder");
                            break;
                        case '$':
                            w.Place(area, "coins", 3, cell);
                            break;
                    }
                }
            }

            return area;
        }, seed: 1);
        world.Enter("test");
        return world;
    }

    private static Properties Props(params (string Name, string Value)[] values) =>
        new(values.ToDictionary(v => v.Name, v => v.Value));
}
