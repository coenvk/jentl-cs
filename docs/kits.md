# Jentl game kits

Jentl has three kits for building new games quickly, and a multiplayer layer for games with several players. Each has a framework-free core (rules, state, undo, files; plain
C#, easy to unit test) and a thin screen layer in `Jentl.Core` (drawing, layout, input, animation) that runs on
desktop and in the browser.

| Kit | Core | Screen layer | Games built on it |
|---|---|---|---|
| Puzzle | `Jentl.Puzzle` | `Jentl.Core.Puzzle.GridView` | Sokoban (`SokobanRules`), and Lights Out and a push puzzle in the tests |
| Cards | `Jentl.Cards` | `Jentl.Core.Cards` (`CardArt`, `CardRenderer`, `TableView`) | Klondike, Easthaven, Forty Thieves, FreeCell, Golf, Pyramid, Spider, TriPeaks and Yukon (each in `src/<Name>`) |
| RPG | `Jentl.Rpg` | `Jentl.Core.Rpg.MapView` | Mossbrook (`src/Mossbrook`), a village and a dungeon |

The puzzle and card cores work the same way: **rules change the state through a turn builder, and the engine records every change**.
That gives you, for free:

- rejected inputs leave no trace (any changes are rolled back),
- exact undo and redo of whole turns (redo replays the recorded changes, not the rules),
- a list of what changed in each turn, for animation, sounds and statistics,
- tags on turns (`"push"`, `"reveal"`) to count or react to.

## Puzzle kit: a new grid puzzle

Declare the tiles and pieces, a legend for level files, and the rules. Here is a complete Lights Out:

```csharp
using Jentl.Puzzle;

var level = LevelFile.ReadPack(".O.\nOOO\n.O.", LightsOut.Legend).Single();
var game = new PuzzleGame<Cell>(level.Board, new LightsOut());
game.Play(new Cell(1, 1));   // game.IsSolved == true
game.Undo();                 // back to the plus

sealed class LightsOut : IPuzzleRules<Cell>
{
    public static readonly TileKind On = new("on");
    public static readonly TileKind Off = new("off");
    public static Legend Legend { get; } = new Legend(Off, '.').Add('O', On);

    public bool Play(TurnBuilder turn, Cell tapped)
    {
        if (!turn.Board.Contains(tapped))
        {
            return false; // rejected: nothing happens
        }

        foreach (var cell in new[] { tapped }.Concat(GridDirections.All.Select(tapped.Step)))
        {
            if (turn.Board.Contains(cell))
            {
                turn.SetTile(cell, turn.Board.TileAt(cell) == On ? Off : On);
            }
        }

        return true;
    }

    public bool IsSolved(Board board) => !board.CellsOf(On).Any();
}
```

- **Pieces** (`board.Place`, `turn.Move/Add/Remove`) keep their identity across moves, undo and redo, so the screen
  can animate them. A cell holds at most one piece; solid tiles (`new TileKind("wall", IsSolid: true)`) block them.
- **Level files** are text packs: maps made of legend symbols, `; comment` or plain-text titles before a map, and
  `Key: value` metadata after it. This is Sokoban's XSB format, generalised. The first symbol defined for a
  tile-and-piece combination is the one `LevelFile.Write` uses; later ones are read-only aliases.
- **On screen**, `GridView` fits the grid into a rectangle (`Fit`), gives each cell's rectangle (`Bounds`), maps taps
  back to cells (`CellAt`) and slides pieces between cells (`Animate`, `PositionOf`). `GridView.ToGrid` turns a swipe
  into a direction. See `Sokoban.Desktop/Rendering/BoardRenderer.cs` for a full use.

## Card kit: a new card game

Create the piles, deal, and write the rules. Inputs are generic, so the table view drives any game:
`DragCards(from, index, to)` when the player drops a run of cards on a pile, and `ClickPile(pile, index)` for a tap.

```csharp
using Jentl.Cards;

var table = new Table();
var stock = table.Add("stock");
var pile = table.Add("pile");
foreach (var card in Deck.Shuffled(Deck.Standard52(), new Random(dealNumber)))
{
    stock.Deal(card, faceUp: false);
}
var game = new CardGame(table, new TurnOver());

sealed class TurnOver : ICardRules
{
    public bool Play(CardTurnBuilder turn, CardInput input)
    {
        if (input is not ClickPile { Pile.Name: "stock" } || turn.Table["stock"].IsEmpty)
        {
            return false;
        }

        turn.MoveTop(turn.Table["stock"], turn.Table["pile"]);
        turn.Flip(turn.Table["pile"], turn.Table["pile"].Count - 1, faceUp: true);
        return true;
    }

    public bool IsWon(Table table) => table["stock"].IsEmpty;

    // Optional: suggestions for a Hint button, and moves the game makes by itself.
    public IEnumerable<CardInput> Hints(Table table) => [new ClickPile(table["stock"], table["stock"].Count - 1)];
}
```

- `turn.Move(from, index, to)` moves the card at `index` and every card on top of it (a run), keeping their order.
- Games with several decks give each card a `Copy` number (`Deck.Standard52(copy: 1)`, or `Deck.Suits(...)` for
  Spider-style one-suit decks), so every card on the table is unique; rules compare `Rank` and `Suit`.
- State that is not a card, such as how often Pyramid's stock was turned over, goes in a table counter:
  `turn.Count("redeals")` in the rules, `table.Counter("redeals")` to read it. Undo reverts it with the turn.
- `Deck.Shuffled` with a seeded `Random` always gives the same deal, which makes deal numbers and tests easy.
- `CardGame.PlayAutoMove()` plays the rules' `AutoMove` (for example, finishing a solitaire whose outcome is
  settled); call it once per animation step.

On screen, place the piles each frame and let `TableView` do the rest:

```csharp
view.CardSize = new Point(width, height);
view.Clear();
view.Place(table["stock"], new Point(x, y));                         // stacked
view.Place(table["pile"], new Point(x2, y), Fan.Down, room: height); // fanned, squeezed to fit

if (view.Update(input, elapsed) is { } move)   // drags and taps become DragCards / ClickPile
{
    game.Play(move);                           // the view animates whatever moved, also on Undo
}

view.Draw(batch, cardRenderer, input.Cursor.Position);
```

`CardArt` draws card faces, backs and suits in code, so no card images are needed; `CardRenderer` takes an
`ITextDrawer` for the rank text, so the kit works with any font.
`TableView.HasSlot` hides the outline of piles that should vanish once empty, and when piles overlap (a pyramid)
taps and drops go to the pile placed last, which is drawn on top.

### A game screen in the app

In this app, `CardGameScene` (in `Sokoban.Desktop/Scenes`) provides everything card game screens share: the top bar,
New deal / Undo / Hint / Menu buttons, keys, hint messages, the timer, automatic moves (paused after an undo, so an
undone move is not immediately replayed), a message when the rules refuse a move (`DescribeRejected`), a hook to handle taps and drags itself (`OnInput`; Pyramid
uses it to pair two tapped cards), and the win and pause screens. A new game then only supplies its deal and
where its piles go; `FreeCellScene` is about 60 lines:

```csharp
internal sealed class FreeCellScene : CardGameScene
{
    public FreeCellScene(GameContext context, int? deal = null)
        : this(context, PickDeal(context, deal, 32_000)) { }

    private FreeCellScene(GameContext context, int deal)
        : base(context, "FreeCell", deal, new CardGame(FreeCellRules.Deal(deal), new FreeCellRules()), FreeCellRules.FoundationTag) { }

    protected override void LayOut(Rectangle area)
    {
        var (card, gap, x) = Columns(area, columns: 8, cardsHigh: 3.8f);
        View.Clear();
        // free cells and foundations along the top, cascades fanned below ...
    }

    protected override CardGameScene CreateNewDeal() => new FreeCellScene(Context);
}
```

## RPG kit: a turn-based RPG or roguelike

`Jentl.Rpg` has the parts most tile-based RPGs and roguelikes need, each usable on its own:

| Part | What it does |
|---|---|
| `Maps.TmxReader` | Reads [Tiled](https://www.mapeditor.org) maps (`.tmx`) and tilesets (`.tsx`): CSV, XML or base64 (zlib/gzip) layers, groups, object layers, custom properties, embedded or external tilesets |
| `Maps.TileMap` | Layers and tilesets; tile properties drive the rules: `solid` blocks movement, `opaque` blocks sight, `name` lets code find a tile |
| `Maps.FieldOfView`, `Maps.Pathfinder` | Shadowcasting field of view and line of sight; A* paths on the grid |
| `Maps.DungeonGenerator` | Seeded rooms-and-corridors floors, always connected, with an entrance and an exit |
| `Items` | An item catalogue from JSON, a slot-based `Inventory` that stacks, and `Equipment` with attack and defense bonuses |
| `Quests` | Quests from JSON with ordered steps (collect, kill, talk, reach, or any condition) and rewards |
| `Dialogue` | Conversation graphs from JSON: lines, choices shown only when their condition holds, and routes that open a conversation differently depending on the story so far |
| `Scripting` | The small JSON language dialogue, quests and items share: conditions (`{"has": "glowcap", "count": 3}`) and effects (`{"give": "potion"}`, `{"startQuest": "q"}`); unknown effect names reach the game as custom effects |
| `World.RpgWorld` | The turn engine: the player acts, then every actor's brain (`still`, `wander`, `hunter`, `skittish`, or your own) takes a turn; bump to attack or talk, walk over items to pick them up, portals between areas, triggers, loot, experience and levels |
| `ContentValidator` | Finds broken references in content (unknown dialogue nodes, items, quests, actor kinds); run it in a unit test |

Brains with more than a couple of moods are easiest to write as a state machine with Fluffy (`docs/fluffy.md`): `SkittishBrain`, used by the cave spiders, hunts, flees when hurt and rests to heal.

Unlike the puzzle and card kits there is no undo: an RPG turn is final. Every command returns a `TurnResult` listing
what happened (moves, hits, deaths, pickups), which the screen animates.

`RpgWorld.Save()` writes the whole game as JSON (a few kilobytes), and `RpgWorld.Load(content, createArea, save)`
restores it. A save records the seed, the player, bag, equipment, flags, variables, quests and recent messages, and
for each visited area its actors, floor items, fired triggers and explored cells. On load the area factory rebuilds
each area (generated floors come back from `world.Seed`), and the saved contents replace what it placed. Unvisited
areas are simply made fresh later. Monsters forget where they last saw the player. A save from an older content
version that refers to an item or actor that no longer exists throws `ContentException`, so a game can discard it.

### Content

A game's content is four JSON files plus maps. Items and actors:

```json
{ "items": [ { "id": "potion", "name": "Healing potion", "sprite": "potion", "stack": 5, "use": [ { "heal": 10 } ] },
             { "id": "sword", "name": "Short sword", "sprite": "sword", "slot": "weapon", "attack": 2 } ] }

{ "actors": [ { "id": "rat", "name": "cellar rat", "sprite": "rat", "faction": "hostile", "hp": 4, "attack": 2,
                "brain": "hunter", "xp": 3, "drops": [ { "item": "coins", "chance": 0.4, "count": 2 } ] },
              { "id": "elder", "name": "Elder Maren", "sprite": "elder", "faction": "friendly", "dialogue": "elder" } ] }
```

A quest, completed in person from a dialogue:

```json
{ "quests": [ { "id": "glowcaps", "title": "Glowcaps for Wren",
  "objectives": [ { "text": "Gather three glowcaps", "collect": "glowcap", "count": 3 },
                  { "text": "Bring them to Wren", "talk": "herbalist" } ],
  "rewards": [ { "give": "potion", "count": 2 } ] } ] }
```

A dialogue whose opening depends on that quest:

```json
{ "dialogues": [ { "id": "herbalist", "speaker": "Wren", "nodes": {
  "start": { "goto": [ { "if": { "quest": "glowcaps", "is": "ready" }, "node": "return" },
                       { "if": { "quest": "glowcaps", "is": "active" }, "node": "waiting" },
                       { "node": "hello" } ] },
  "hello": { "text": "Bring me three glowcaps?",
             "choices": [ { "text": "I'll look.", "do": [ { "startQuest": "glowcaps" } ] }, { "text": "No." } ] },
  "waiting": { "text": "Found any yet?" },
  "return": { "text": "Perfect!", "do": [ { "take": "glowcap", "count": 3 }, { "completeQuest": "glowcaps" } ] } } } ] }
```

Maps come from Tiled. Give tiles `name`, `solid` and `opaque` properties in the tileset, draw tile layers, and place
objects in an object layer by class (Tiled's "type"); `RpgWorld.LoadArea` turns them into the area:

| Class | Properties | Becomes |
|---|---|---|
| `spawn` | (its name) | An arrival point, e.g. `start` |
| `actor` | `kind` (else the object name), optional `dialogue`, `brain`, `name` | An actor; the object name is its tag, which quests use (`"talk": "elder"`) |
| `item` | `item`, `count` | Items on the floor |
| `portal` | `area`, `spawn` | Stepping here travels to that area (a house door, stairs); other actors never step onto portals, so nobody blocks a doorway |
| `trigger` | `do` (JSON effects), optional `if` (JSON condition), `repeat` | Effects when stepped on |

Map properties: `name` (shown in the top bar) and `lit` (false for dark areas, seen only around the player).

### Putting it together

```csharp
var content = RpgContent.FromJson(read("items.json"), read("actors.json"), read("quests.json"), read("dialogue.json"));
var world = new RpgWorld(content, (w, id) => id == "village"
    ? w.LoadArea(id, TmxReader.ReadMap(read("village.tmx"), read))
    : BuildDungeonFloor(w, id),                       // DungeonGenerator, then w.Spawn(...) and w.Place(...)
    seed: 42);
world.Enter("village");

var turn = world.Step(GridDirection.Up);              // or Wait(), Use("potion"), Equip("sword")
mapView.Show(turn.Events);                            // glide, flash, damage numbers
if (turn.TalkTo is { } npc)
{
    var talk = world.Talk(npc);                       // talk.Current.Text, talk.Choices, talk.Choose(i)
}
```

On screen, `MapView` draws an area around a camera that follows the player: tile layers with point sampling at whole-
number scales, fog of war in dark areas (unseen cells black, remembered ones dimmed), items, actors with health bars,
and floating damage numbers. `CellAt` maps taps to cells for tap-to-walk. In this app `AdventureScene` adds the top
bar, the message log, the buttons and the dialogue, bag and journal overlays; `src/Mossbrook` has the game's own code
(starting a game, generating the cellar floors, the victory effect) in about 150 lines.

## Multiplayer: humans and bots at one table

`Jentl.Multiplayer` (framework-free) runs turn-based games for several players on one device:

| Type | What it does |
|---|---|
| `Player` | A seat at the table: a name, and whether a human or a bot plays it |
| `TurnOrder` | Whose turn it is, clockwise or not, with skips (`Advance(2)`) and reversals |
| `ITurnBasedGame<TMove>` | The game: whose turn it is, the legal moves for a seat, and applying a move. Moves are plain values, so they can be shown, logged or one day sent over a network |
| `IBot<TGame, TMove>` | A computer player: picks one of the legal moves |
| `Match<TGame, TMove>` | Runs it all: bots move by themselves after `BotDelay` (so people can follow), humans through `Play`, and with several humans at the device it hides hands (`Viewer`) and waits for `ConfirmHandover` whenever the turn reaches a different person |

```csharp
var game = new UnoGame(players: 4, seed: 42);
var match = new Match<UnoGame, UnoMove>(game, players, seat => new SmartUnoBot());

// Each frame:
if (match.AwaitingHandover) ShowPassTheDevice(match.Current.Name);   // then match.ConfirmHandover()
else if (match.Update(elapsed) is { } botMove) Animate(botMove);
else if (match.IsHumansTurn && tapped is { } move) match.Play(move);
```

Online play would slot in as another kind of seat controller; the game only ever sees seats and moves. Uno
(`src/Uno`) is the example: `UnoGame` implements the rules, `RandomUnoBot` and `SmartUnoBot` the opponents, and
`UnoMatch` adds up rounds to 500 points.
