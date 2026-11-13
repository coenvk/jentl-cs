# Jentl game kits

Examples name the games built on Jentl; their code (paths such as `apps/Sokoban`, `src/Bricks`) lives in
[games-cs](https://github.com/coenvk/games-cs).

Jentl has three kits for building new games quickly, and a multiplayer layer for games with several players. Each has a framework-free core (rules, state, undo, files; plain
C#, easy to unit test) and a thin screen layer in `Jentl.Core` (drawing, layout, input, animation) that runs on
desktop and in the browser.

| Kit | Core | Screen layer | Games built on it |
|---|---|---|---|
| Puzzle | `Jentl.Puzzle` | `Jentl.Core.Puzzle.GridView` | Sokoban (`SokobanRules`), and Lights Out and a push puzzle in the tests |
| Cards | `Jentl.Cards` | `Jentl.Core.Cards` (`CardArt`, `CardRenderer`, `TableView`) | Klondike, Easthaven, Forty Thieves, FreeCell, Golf, Pyramid, Spider, TriPeaks and Yukon (each in `src/<Name>`) |
| RPG | `Jentl.Rpg` | `Jentl.Core.Rpg.MapView` | Mossbrook (`src/Mossbrook`), a village and a dungeon |
| ECS and physics | `Jentl.Ecs`, `Jentl.Physics` | `Jentl.Core.Diagnostics.PhysicsDebug` | Bricks (`src/Bricks`), a brick breaker |
| Collecting and tracks | `Jentl.Collect`, `Jentl.Tracks` | `Jentl.Core.Tracks.RoadPainter`, `Camera2D.FollowHeading` | Paddock (`src/Paddock`), a car collecting and racing game |

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
- `Deck.Shuffled` with a seeded `Random` always gives the same deal, which makes deal numbers and tests easy. When a
  player must not be able to work out the rest of the deck from their own cards (bots, money, online play), shuffle
  with a `Jentl.Collect.KeyedRandom` instead: a seeded `Random` has only 2^31 seeds, few enough to try them all.
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

### A card game from a ruleset

A card game can also be written down instead of programmed: a JSON ruleset names the deck, how many play, the piles
with their deal and rules, what cards do, and how the game is won, and `RulesetGame` plays it. Jentl comes with
nine (`RulesetExamples`): the solitaires `klondike`, `freecell`, `spider`, `golf`, `yukon` and `fortythieves`, and
for players `crazyeights`, `uno` and `maumau`. Copy one to start a new game.

```csharp
var ruleset = CardRuleset.Parse(File.ReadAllText("rules/klondike.json"));   // or RulesetExamples.Load("klondike")
var game = new RulesetGame(ruleset, seed: deal);       // deals; game.Game is the CardGame: play, undo, hints, auto-moves
RulesetLayout.Solitaire(view, game, area);             // each frame: the piles where the ruleset puts them
```

```json
{
  "name": "Klondike",
  "deck": "standard",
  "piles": [
    { "name": "stock", "at": [0, 0], "deal": { "rest": true, "faceUp": false }, "click": { "to": "waste", "redealFrom": "waste" } },
    { "name": "waste", "at": [1, 0], "take": "top" },
    { "name": "foundation", "count": 4, "at": [3, 0], "goal": true, "build": { "rank": "up", "suit": "same", "empty": "A" }, "take": "top" },
    { "name": "column", "count": 7, "at": [0, 1.25], "fan": "down", "deal": { "cards": "index+1", "faceUp": "top" },
      "build": { "rank": "down", "suit": "alternate", "empty": "K" }, "take": "run" }
  ]
}
```

- **Deck**: `"standard"`, `{ "standard": 2 }` (two decks), `{ "standard": 4, "suits": ["spades", "hearts"] }` (two-suit
  Spider), `"uno"`, or one of its own: `suits` (name, symbol, colour group, ink and fill as `#rrggbb`), `ranks` (name,
  label, order, points, copies per suit, tags) and `extra` cards of no suit (wilds); `"jokers": 2` adds jokers to any.
  Every card has a `CardFace` saying all of this, standard cards too, and `CardRenderer` draws a face of its own as a
  card in its fill with its label in its ink.
- **Piles**: a `name` (a group of `count` piles is named `column0`, `column1` ...; `perPlayer` makes one per player),
  `goal` for foundations, and:
  - `deal`: `cards` (a number, a list by pile, or `"index+1"`), `rest` for the stock, `faceUp` (`true`, `false`, `"top"`),
    `faceDown` (counts by pile, Yukon's `"index"`), `not` (a rank or tag never dealt there: no wild to start a discard);
    `"faceUp": "top"` turns up only the top card, `"faceUp": "all"` covered piles' cards too.
  - `build`, what may be put on it: `rank` (`up`, `down`, `updown`, `same`, `any`; `wrap` from king to ace), `suit`
    (`same`, `alternate` colours, `samecolour`, `different`, `any`), `empty` (the rank an empty pile takes, or `none`),
    `max` cards (1 for a free cell), `from` (only from these piles), `runs` (whether several cards at once; goal piles
    take one at a time), or for shedding games `"match": "suitOrRank"` with `wild` ranks or tags that go on anything.
  - `take`, what may be picked up: `top`, `run` (in the pile's building order, or `{ "kind": "run", "rank": "down",
    "suit": "same", "limit": "freecells" }`), `any` face-up card with what is on it (Yukon), `one` card from a hand.
  - `click`, what a tap does: deal `deal` cards `to` a pile (or one to `each` of a group, Spider's `requireFilled`),
    and when empty turn `redealFrom` back over, `redeals` times (-1: always).
  - `complete`: a run of `length` cards in order leaves for a goal pile (Spider). `flipTop`: a face-down card left on
    top turns over (yes by default).
  - `at`, `step` and `fan` (`stacked`, `down`, `right`): where it goes, in card sizes, for `RulesetLayout`; `places`
    lists each pile's spot (from `at`) for layouts that aren't rows.
  - `coveredBy`, for overlapping cards (TriPeaks, Pyramid): for each pile of the group, the piles of it lying over it.
    A covered pile is dealt face down (unless the deal says `"faceUp": "all"`), can't be taken from, and turns up once
    everything over it has gone; the `tripeaks` and `pyramid` examples are built this way.
- **Pairs**: `"pairs": { "sum": 13, "from": ["peak", "waste"], "to": "discard", "alone": [13] }` lets cards leave in
  twos (Pyramid): drop one free card from those piles on another and, when their ranks add up to the sum, both go to
  the `to` pile; a rank in `alone` goes by itself with a tap. Hints and the bot take pairs first.
- **Players**: `1` (a solitaire) or `{ "min": 2, "max": 8, "default": 4 }`. With players, `turns` names the `stock`,
  `discard` and `hand` piles, how many cards a `draw` takes, and `afterDraw`: `mayPlay` the card drawn, `pass`, or
  `untilPlayable`; `afterPenalty` the same after a draw a script made bigger (Mau-Mau's sevens: `pass`); the discards are shuffled back into an empty stock. `effects` say what cards do, chosen by `rank`,
  `suit` or `tag`: `skip` players, `reverse` play (a skip for two), make the next player `draw`, `chooseSuit`, or
  `playAgain`.
- **Win**: `"goals"` (every card on goal piles, for solitaires), `{ "empty": ["column"] }` (Golf), or `"emptyHand"`
  (the first to shed their cards wins and scores the points left in the other hands).

- **Script**: for what the fields can't say, a ruleset carries Jentl.Script code (`"script"`, one string or a list of
  lines). A pile's `build` may name a `check` that must also say yes (`fn check(card, pile, from)`), an effect may name
  a `script` to call when its cards are played (`fn name(card, seat)`), and these functions run if the script has them:
  `played(card, seat)` after any card is played, `moved(from, to, count)` after a solitaire move, `drawing(seat)` for
  how many cards a draw takes, `won()` for whether a solitaire is won, and `score(seat)` for a winner's score. Scripts
  read the table any time: `pile(name)`, `piles(group)`, `hand(seat)`, `seat()`, `players()`, `direction()`, `suit()`
  and `counter(name)`; cards are maps (`rank`, `suit`, `label`, `order`, `points`, `colour`, `tags`, `code`, `faceUp`)
  and piles objects (`name`, `group`, `index`, `count`, `empty`, `top`, `cards`). While a move is played they may act,
  through the turn so undo takes it back: `skip(n)`, `reverse()`, `again()`, `chooseSuit()`, `draw(seat, n)`,
  `move(from, to, n)`, `flip(pile, up)`, `count(name, amount)` and `say(text)` (a message for the screen). Keep
  state in counters, not in script variables, so undo takes it back too. Golf's `check` puts nothing on a king;
  Mau-Mau's sevens stack:

```json
"script": [
  "fn played(card, seat)",
  "  if card.rank == \"7\" then count(\"penalty\", 2) end",
  "end",
  "fn drawing(seat)",
  "  let penalty = counter(\"penalty\")",
  "  if penalty == 0 then return 1 end",
  "  count(\"penalty\", -penalty)",
  "  return penalty",
  "end"
]
```

  A mistake in the script is found when the ruleset is read (`ruleset.script: Mau-Mau:12: expected 'end' to close 'if'`),
  as is a function named but missing; one while playing is a `ScriptException` with the line, and the move is undone.

Mistakes say where they are, and unknown fields are mistakes, so a typo never quietly changes a game:
`ruleset.piles[2].build.rank: 'upp' isn't a rank (use any, up, down, updown, same).`

A solitaire plays as any `CardGame`: drags, taps (a card goes to a goal pile if it can, else where it builds; a stock
deals), hints that rank goal moves and turning cards over first, and automatic finishing once nothing is hidden. A
game for players is an `ITurnBasedGame<CardInput>`: the player whose turn it is taps a card in their hand to play it
(`ClickPile`), taps the stock to draw, `PassTurn`s to keep a drawn card, or names a suit (`ChooseSuit`), and
`LegalMoves` lists what they may do. `RulesetBot` plays any of them, so a `Match` runs the bots; `RulesetLayout.Table`
lays out the table with the other hands face down (`TableView.Hidden`). Turn state (whose turn, direction, the named
suit) lives in table counters, so undo takes it back with the cards. Layouts with cards covering each other (Pyramid,
TriPeaks) still need C# rules.

### Chips

Betting games need money on the table. `Chips.Break(amount)` gives the fewest chips, largest value first, and
`Chips.Stacks(chips, maxPerStack)` splits them into stacks for the screen; `Chips.StacksOf(amount, maxPerStack)` and
`Chips.StackCount` give the same stacks without allocating, for drawing every frame. `ChipRenderer` draws the stacks
in colours per denomination, and `ChipFlight` slides a pile of chips from one spot to another for a pot being won or a
bet placed. `Card.Symbol` writes a card as "K♠" and `Card.Symbols(cards)` a row of them, for logs and labels.

| Part | What it does |
|---|---|
| `Chips` | The standard denominations (1 to 25000); `Break(amount)` into chips, and `Stacks(chips, maxPerStack)` into stacks |
| `ChipRenderer` | Draws stacks of chips seen from a little above, in their denomination's colour (`ColorOf`) with stripes round the edge (`StripeColorOf`, `EdgeStripe` for the geometry), a lighter rim, an inlay and a shadow under each chip: `Width` and `StackHeight` for layout, `DrawAmount` to draw; nothing is allocated per call |
| `ChipFlight` | A pile of chips (`Amount`) easing from one point to another: `Update` each frame, then draw at `Position` until `IsDone` |

```csharp
var chips = Chips.Break(1_275);                                     // 1000 x1, 100 x2, 25 x3
ChipRenderer.DrawAmount(batch, potCentre, 1_275, chipWidth: 24);    // the stacks, side by side
var flight = new ChipFlight(1_275, potCentre, winnerSeat, TimeSpan.FromSeconds(0.6));
```

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

Online play is in the next section; the game only ever sees seats and moves. Uno
(`src/Uno`) is the example: `UnoGame` implements the rules, `RandomUnoBot` and `SmartUnoBot` the opponents, and
`UnoMatch` adds up rounds to 500 points. Texas Hold'em (`src/Poker`) is the second: `HoldemHand` is one
hand (blinds, betting rounds, side pots), `HoldemTable` the sit-and-go around it, and `PokerBot` the opponents.

### Timed decisions and fair seatings

A bot in another process may be slow, or answer with a move that is not allowed. `Decisions.DecideAsync` waits for the
answer no longer than a limit, and falls back to a move of the game's choosing when it is late, illegal, or the agent
is gone. `Decided<TMove>` says which: its `Fault` is `None` (the agent's own answer), `Late`, `Illegal` or `Gone`.
`Seatings.Duplicate(seats)` gives the seatings for playing the same game several times so that every bot sits in every
seat once; for two seats, `[[0, 1], [1, 0]]`.

```csharp
var decided = await Decisions.DecideAsync(
    ask: async _ => ParseMove(await agent.AskAsync(StateJson(game, seat), TimeSpan.FromSeconds(2))),
    limit: TimeSpan.FromSeconds(2),
    isLegal: move => game.LegalMoves(seat).Contains(move),
    fallback: () => game.LegalMoves(seat)[0],
    isGone: () => agent.HasExited);
game.Apply(seat, decided.Move);
if (decided.Fault != Fault.None) { ShowNotice($"{seat.Name}: {decided.Fault}"); }
```

## UI toolkit

`Jentl.Core.UI` holds the widgets every screen in this repo is built from, so a new game gets them for free:

| Type | What it is |
|---|---|
| `Fonts` | A TrueType font in the UI's sizes (title, heading, body, small), scaled to the screen: 1 at 1280x800, smaller on phones (by width when upright). The card suits ♠ ♥ ♦ ♣ always work: `SuitGlyphSource` draws them (from `SuitShape`, as the card art does) behind any font that lacks them. `FontTextDrawer` hands it to kits that only know `ITextDrawer` |
| `TextExtensions` | `DrawText` with alignment and an optional shadow, and `Wrap` to break text into lines that fit (with a font, or with any `measure` function, for tests) |
| `Palette` | The colours widgets draw with; set them at start-up to restyle everything |
| `Theme`, `SurfaceStyle` | The rest of the look: how panels and controls (buttons, form rows, the selected menu item) are drawn — corner radius, a border in a colour, a drop shadow, in design pixels — and whether panels have their accent bar. Set `Theme.Current` at start-up; the default is the plain look. `Theme.DrawPanel` draws a panel of your own screen the way the theme does, and `SurfaceStyle.Draw` any other surface (a score box, a seat) |
| `RoundedRectangle` | `batch.FillRoundedRectangle(bounds, radius, color)`: soft-edged round corners, from one cached quarter circle per radius. `batch.FillEllipse(centre, radii, color)` (and `FillCircle`) draws soft-edged ellipses |
| `Menu` | A vertical list of pill-shaped items: keys, taps, mouse hover. Taller than its space, it scrolls: the selection stays in view, arrows show there is more, and it can be dragged or scrolled with the mouse wheel An item can be `Dimmed`: drawn faded, still chosen (to say why it isn't available yet) |
| `ButtonBar` | A centred row of buttons that squeezes its gaps on narrow screens |
| `Form` with `ToggleRow`, `SliderRow`, `ButtonRow`, `TextRow`, `BindingRow` | Settings panels: up/down move the focus, left/right change values, confirm toggles or presses; tap a row, or drag a slider. A `TextRow` takes typed text (a name, a room code): chosen, it has the keyboard until Enter keeps the text or Escape drops it, with a length limit and an optional filter (`allow: char.IsDigit`). A `BindingRow` rebinds a control (see [Gamepad](#gamepad)). While a row has the keys, `IsEditing` tells the screen to skip its own Back. Given a `maxHeight`, a form scrolls like a menu. A row's `Hint` shows below the form in an `OverlayPanel` while it has the focus |
| `Tooltip` | A small panel of text beside a target rectangle: below it, or above when there is no room, and kept on screen |
| `NineSlice` | A frame cut from a picture that stretches without stretching its corners. Set it as a `SurfaceStyle`'s `Art` to draw panels and controls with it, tinted with the palette (art drawn in white and greys) or, with `TintArt = false`, in its own colours |
| `OverlayPanel` | Dims the screen and draws a panel with a title, lines of text and a `Menu` or `Form`, fitting the menu to the screen; a line too wide for the panel wraps, and a title too wide is drawn in the body font |
| `Input.Clipboard` | `TrySetText` puts text on the system clipboard on the desktop (through SDL2, which MonoGame loads); false where there is none (the browser), so the game shows the text instead |

`JentlGame` feeds the window's text input to `InputState.Typed` (the characters typed this frame, as the keyboard
layout makes them) and the mouse wheel to `InputState.Wheel` (steps, positive up); override `ReadWheel` for scripted
input, or call `Input.Type(c)` in tests.

Widgets take an `ActionMap<UiAction>` for their keys and buttons (`UiKeys.Default` has arrows/WASD, Enter/Space and
Escape, and the D-pad/left stick, A and B/Start) and
play `UiSounds.Navigate` through a `SoundPlayer` (`SoundPlayer.Silent()` for none). Hit areas are wherever the widget
was last drawn, so layout can change every frame:

```csharp
var form = new Form(sounds,
    new ToggleRow("Sound", () => settings.Sound, on => settings.Sound = on),
    new SliderRow("Volume", () => settings.Volume, v => sounds.Volume = settings.Volume = v),
    new ButtonRow("Back", () => scenes.Pop()));

// Update:  form.Update(input, UiKeys.Default, elapsed);
// Draw:    OverlayPanel.Draw(batch, viewport, fonts, "Options", [], form);
```

## Accessibility

Settings for players who need the game to behave differently. Each lives with what it changes, so offer them in the
options and save them with the rest:

| Setting | What it does |
|---|---|
| `Accessibility.ReduceMotion` | Less movement: `Camera2D.Shake` does nothing and a `WipeTransition` fades instead. Check `Accessibility.Motion(amount)` for the game's own motion (screen flashes, bouncing, parallax): it gives the amount, or 0 while motion is reduced |
| `Fonts.TextScale` | Bigger text, from 1 to 2: the title, heading, body and small fonts grow, and menus, forms and panels with them (they lay out by their font). `Px` lengths and `Sized` fonts stay as they are |
| `PaletteColors` | `Palette.Capture()` takes the game's colours at start-up; `WithHighContrast()` pushes text and surfaces apart (white or black text, whichever stands out more), and `WithColorBlindSafe()` shows success in blue rather than green. `Apply()` puts a set in place, so switching the setting off applies the captured one again. `PaletteColors.Contrast(a, b)` gives the WCAG ratio (4.5 is enough for text, 7 good), to check a game's own colours |
| `ActionMap.SetToggle(action, true)` | A held action (run, aim) becomes a toggle: pressed once it stays down until pressed again. Saved with the bindings |
| `Rumble.Enabled`, `Rumble.Strength` | Vibration off, or softer (see [Gamepad](#gamepad)) |

```csharp
var colors = Palette.Capture();
void ApplyAccessibility(Settings s)
{
    Accessibility.ReduceMotion = s.ReduceMotion;
    fonts.TextScale = s.TextScale;
    var palette = s.HighContrast ? colors.WithHighContrast() : colors;
    (s.ColorBlind ? palette.WithColorBlindSafe() : palette).Apply();
}
```

## Audio

`Jentl.Core.Audio` plays sound effects and music, and can make both in code.

`Synth` renders tunes written as text: each voice is a line of steps where a note ("C4", "F#3", "Bb2") starts a
note, "-" holds it, "." rests and "x" strikes a drum. An `Instrument` is a waveform (square, pulse, triangle,
sine, saw or noise) with an attack/decay/sustain/release envelope, vibrato and a pitch slide (a sine sliding down
two octaves is a kick drum). Loops wrap their last notes' tails round to the start so they repeat without a click,
and the same tune always renders the same samples.

```csharp
var lead = new Instrument(Wave.Pulse, Volume: 0.16f, Vibrato: 0.08f);
var bass = new Instrument(Wave.Triangle, Volume: 0.45f);
var tune = new Tune(100,
    new Voice(lead, "E5 - G5 - C6 - B5 A5 | G5 - E5 - D5 - C5 -"),
    new Voice(bass, "C3 - - - G2 - - - | A2 - - - E2 - - -"));

sounds.AddMusic("menu", () => Synth.Render(tune, loop: true));       // rendered on first play
sounds.Add("blip", Synth.Render(new Tune(900, new Voice(lead, "E6 B6"))));
```

Beyond the bare waves, `Synth` has instruments made the way real ones sound: `Wave.Pluck` is a plucked string
(Karplus-Strong: a burst of noise ringing round a loop one period long, losing its highs as it goes), `Wave.Fm`
bends a sine with another (brass with whole ratios, bells and celestas with odd ones), `Wave.Partials` adds up
harmonics (organs, pianos, reeds; the higher ones may fade faster), and `Wave.Choir` sings "ah". Any instrument can
play as several detuned copies (`Unison`, `Detune`: strings, a choir), through a low-pass filter that can open
with the envelope (`Cutoff`, `CutoffEnvelope`), with breath noise, and with a decay that rings away like a struck
thing (`Ring`). A tune's `Reverb` puts it in a room, from a hall (0.3) to a cathedral (0.6); a loop's echo wraps
round to its start. `Swing` puts every second step a little late (a third of a step for a shuffle), the beats
staying on time. `Instruments` has the presets, matched in volume: harp, lute, guitar, harpsichord, pizzicato,
upright bass, strings, violin, cello, flute, recorder, pan pipes, clarinet, oboe, accordion, horn, trumpet, brass,
choir, organ, piano, electric piano, celesta, bells, marimba, timpani, frame drum, kick, snare, shaker, cymbal and
the chiptune voices (`Instruments.All` lists them by name).

```csharp
var tune = new Tune(96,
    new Voice(Instruments.Flute, "E5 - G5 - A5 - - . | G5 - E5 - D5 - - ."),
    new Voice(Instruments.Harp, "A3 C4 E4 C4 A3 C4 E4 C4 | G3 B3 D4 B3 G3 B3 D4 B3"),
    new Voice(Instruments.Cello, "A2 - - - - - - - | G2 - - - - - - -")) { Reverb = 0.3f };
```

Rendering a long song takes a moment, so `SoundPlayer` renders a track on a worker the first time it is asked for,
playing the old one until the new one is ready; `PrepareMusic` starts that early.

A `Song` is longer than a loop of a few bars: its parts (lead, bass, drums, each an instrument), its sections (an
intro, A, B, each some bars with what every part plays, a part left out resting) and the order to play them in,
four beats to a bar or three (`BeatsPerBar`, for a waltz), and a `Reverb`.
`ToTune` joins each part up through the order, filling a section out with rests, and says which part of which
section is too long. `Chords` writes accompaniment from chord symbols, so only the melody needs writing note by
note: a progression is bars between "|", each with one or more chords (`"C | Am F | Dm7 | G7"`; m, 7, maj7, m7, dim,
sus2, sus4 and 5), and a pattern names chord tones per step (1, 3, 5 and 7, 8 the octave, 10 the third above it,
-5 the fifth below, "." and "-"), repeated over each chord:

```csharp
var verse = "C | Am | F | G7";
var song = new Song("Open Doors", 108,
    [new SongPart("lead", lead), new SongPart("bass", bass), new SongPart("arp", arp)],
    [
        new Section("a", 4, new Dictionary<string, string>
        {
            ["lead"] = "E5 - G5 - C6 - B5 A5 | G5 - E5 - D5 - C5 - | A4 - C5 - F5 - E5 D5 | D5 - - - . . . .",
            ["bass"] = Chords.Play(verse, 2, "1 . 5 . 8 . 5 ."),        // root, fifth, octave
            ["arp"] = Chords.Play(verse, 4, "1 3 5 3"),
        }),
        new Section("b", 4, new Dictionary<string, string> { ["arp"] = Chords.Hold("F | G | Em | Am", 4, 1) }),
    ],
    ["a", "a", "b", "a"]);
sounds.AddMusic("open-doors", () => Synth.Render(song.ToTune(), loop: true));
```

A `Composition` is a song as a music maker writes it, in the manner of Bosca Ceoil: channels, each an `Instruments`
preset at a volume, with patterns a bar long (notes by step, MIDI pitch and length), and an arrangement saying which
pattern each channel plays in each bar. `ToTune` makes it a `Tune` (a chord becomes several voices; `from` and `to`
bars play part of it), `ToJson` and `FromJson` save it, and `ToCode` writes the C# of the tune for a game to play.
A channel's `Custom` is its preset edited (wave, envelope, filter and the rest); `InstrumentText` keeps an instrument
as JSON and writes it as the preset `with` the settings that differ.
`Synth.ToWav` turns samples into a WAV file, and `SoundPlayer.ReplaceMusic` swaps a track for a new rendering of it
(the song after an edit). games-cs's Composer app is the editor.

`SoundPlayer` plays effects by name (recorded `.wav` files or rendered ones) and loops music: `PlayMusic` fades
the new track in while the old one fades out, and asking for the track already playing does nothing, so every
screen can simply name its music in `OnEnter`. Effects and music each have a switch and a volume; call `Update`
every frame for the fades. In a browser, music that could not start yet (pages may only make sound after the
first touch) is retried until it plays.

`SoundPlayer.Mixer` holds the buses sounds play on, under a `Master`: `effects`, `music`, `voice`, `ui`, and any other
a game names (`Mixer["engines"]`). Each has a `Volume` and `Muted`, so players can get a slider per bus; the old
`Volume`, `Enabled`, `MusicVolume` and `MusicEnabled` are the effects' and music's. `Play` takes a pan, a pitch and the
bus; `PlayAt` places a sound in the world, fainter with distance from the listener and panned to its side. Ducking
rules turn one bus down while another plays, fading back after:

```csharp
sounds.Mixer.AddDucking(AudioMixer.Voice, AudioMixer.Music, level: 0.3f);  // music under the words
sounds.Play("greeting", bus: AudioMixer.Voice);
sounds.PlayAt("explosion", blast, camera.Position, range: 900);
sounds.Play("coin", pitch: 0.2f * combo);                                  // higher with each coin in a row
```

## Particles and animation

`Jentl.Core.Graphics` has a pooled `ParticleSystem` and ready-made `ParticleEffects`: `Confetti` (tumbling paper
flakes), `Sparkles`, `Rise` (sparks drifting up), `Hit`, `Smoke` and `Dust`. An effect is a record (count, speed,
life, size from start to end, colours, direction and spread, gravity, drag, spin), so new ones are a `with` away.
Every `SceneManager` has an `Effects` system drawn above all scenes, for effects that should show over a win panel:

```csharp
scenes.Effects.EmitArea(new Rectangle(0, -10, scenes.Viewport.Width, 8), ParticleEffects.Confetti);
scenes.Effects.Emit(cardCentre, ParticleEffects.Sparkles);
```

`SceneManager.Overlay` draws over every scene too, after the effects: a notice that should outlive the screen it
came from ("You have unlocked a new music track") draws there.

`Animation` is a list of frames with durations played as a loop, once, or ping-pong; `FrameAt(time)` says which
frame shows, so many sprites can share one animation. `Animator` keeps the clock for a sprite that switches between
animations, and `SpriteSheet` cuts a texture into numbered frames. An animation's `Events` name frames (a footstep
on the third, a hit box on the fifth), and the `Animator` raises `FrameEvent` as they come up. `AsepriteSheet.Parse`
reads the JSON Aseprite exports with a sheet (array or hash): each frame's place and duration, and its tags as
animations by name, forwards, backwards or ping-pong.

`AnimationStates<TState>` is a sprite's poses as states, each with its animation, and the moves between them: when a
condition holds (from one state, or `WhenAny`), or `After` a one-off animation has played out. The first move that
applies wins; `Changed` says when.

```csharp
var states = new AnimationStates<Pose>(Pose.Idle)
    .Add(Pose.Idle, idle).Add(Pose.Run, run).Add(Pose.Hurt, hurt)
    .WhenAny(Pose.Hurt, () => player.WasHit)
    .After(Pose.Hurt, Pose.Idle)
    .When(Pose.Idle, Pose.Run, () => player.Speed > 0.1f)
    .When(Pose.Run, Pose.Idle, () => player.Speed <= 0.1f);
states.Animator.FrameEvent += name => sounds.Play(name);   // "step"
```

`Motion` puts tweens on a timeline: a float, `Vector2` or `Color` tweened through a setter with an easing, motions
in a `Sequence` or `Together`, `Wait`s, `Call`s at points along the way, and `Repeat` (a number of times or forever,
there and back with `yoyo`). A motion keeps no clock; a `MotionPlayer` plays one, and `Motions` plays many, dropping
each when it ends. `Easing` has quad, cubic and sine curves in and out, back (an overshoot), elastic and bounce.

```csharp
motions.Play(Motion.Sequence(
    Motion.Tween(v => card.Position = v, deck, slot, 0.4, Easing.BackOut),
    Motion.Call(() => sounds.Play("card")),
    Motion.Tween(s => card.Scale = s, 1, 1.1f, 0.1),
    Motion.Tween(s => card.Scale = s, 1.1f, 1, 0.1)));
```

For feel: `Flash` turns a sprite's colour to white (or any) on a hit and back, `SquashStretch` gives a scale that is
wide and short on landing, narrow and tall on a jump, springing back, and `Shake` throws anything off by a smooth,
fading wobble. The Lab's Animation demo has them all.

The RPG kit reads Tiled's tile animations (`<animation>` frames in a tileset), and `MapView` plays them: Mossbrook's
water ripples, torches flicker and glowcaps pulse. A character tile with a `walk` property names its stride frame,
shown while the character moves between cells; standing still, characters bob gently. `MapView.Burst(cell, effect)`
puts particles on the map, moving with the camera: hits spark, the defeated vanish in smoke, pickups sparkle and
level-ups rise.

## Gamepad

`JentlGame` reads the first connected controller every frame next to the keyboard and pointer, and `InputState`
answers `IsDown(Buttons)` and `WasPressed(Buttons)`. The thumbsticks and triggers count as buttons too
(`Buttons.LeftThumbstickUp`, `Buttons.RightTrigger`...) once past their dead zone, so a stick can stand in for the
D-pad. An `ActionMap` binds buttons beside keys, and `ActionRepeater` repeats held buttons like held keys:

```csharp
var map = new ActionMap<Move>(
    new Dictionary<Move, Keys[]> { [Move.Up] = [Keys.Up, Keys.W], [Move.Jump] = [Keys.Space] },
    new Dictionary<Move, Buttons[]> { [Move.Up] = [Buttons.DPadUp, Buttons.LeftThumbstickUp], [Move.Jump] = [Buttons.A] });

if (map.WasPressed(input, Move.Jump)) { ... }
```

Players can rebind a map. `Bind(action, key, slot)` puts a key (or button) in one of an action's slots (0 is the main
key, 1 the alternative), and takes it from any other action that had it, so one key never does two things; `Reset`
goes back to the bindings the map was made with. `Save` writes only the actions that differ from those as JSON, and
`Load` reads them back, skipping actions and keys the game no longer has. Rebind a `Copy()` of a shared map, not the
map itself. In a settings `Form`, a `BindingRow` shows the key in one slot and, chosen, waits for the next one
pressed (Escape cancels, Backspace clears); `InputNames` gives keys and buttons short names ("Left Shift", "LB"):

```csharp
var controls = Controls.Default.Copy();
if (store.ReadText("controls.json") is { } saved) controls.Load(saved);
void Save() => store.WriteText("controls.json", controls.Save());

var form = new Form(sounds,
    new BindingRow<Move>("Jump", controls, Move.Jump, changed: Save),
    new BindingRow<Move>("Jump (pad)", controls, Move.Jump, device: BindingDevice.GamePad, changed: Save),
    new ButtonRow("Reset", () => { controls.Reset(); Save(); }));

// While a row waits for a key, form.IsEditing is true: skip the screen's own Back then.
```

Screens made for the mouse or touch (dragging cards, tapping the map) work with `PadCursor`: the right stick moves an
on-screen pointer (slowly at a slight tilt, for aiming) and the right trigger or a stick click presses it. While it is
active it *is* the pointer, so taps, drags, hover and gestures need no extra code; it wakes up where the mouse was and
steps aside the moment the mouse or a finger moves. `JentlGame.PadCursor` draws it above everything. Override
`ReadGamePad` to feed scripted pad input.

`JentlGame.Rumble` shakes the controller being read. `Play` starts a `RumbleEffect` (the heavy and light motors from
0 to 1, for a time, dying away unless `fade: false`); overlapping effects take the strongest of each motor, and the
motors stay off while the window is in the background. `RumbleEffect.Tap`, `Bump`, `Hit` and `Crash` are ready-made.
Give players `Enabled` and `Strength` in the options: some find it uncomfortable.

```csharp
game.Rumble.Play(RumbleEffect.Bump);                                // a crate lands on its target
game.Rumble.Play(0.6f, 0.2f, TimeSpan.FromSeconds(1), fade: false); // an engine running
```

## Recording and replaying input

`JentlGame` can record what the player does and play it back: a bug exactly as it happened, a ghost of a best run, or
a test of a real-time game. `StartRecording()` captures every frame from the next update on (the keys down, the
pointer as the game saw it, the gamepad, the wheel, the text typed, and how long the frame lasted) and
`StopRecording()` hands it over. `Replay(recording)` plays one back: its frames stand in for the devices and the clock,
one a frame, until `ReplayFinished`, when the devices take over again.

A replay does the same things again only in a game that runs the same way: start it in the state the recording
started from, seed its randomness, take time only from the elapsed time `Update` is given, and step physics with
`PhysicsWorld.Update` (its fixed step makes the same elapsed times give the same simulation). `InputRecording.Save`
writes text, one line per frame and a run of the same frame on one line, which `Load` reads back. Without a
`JentlGame`, `InputState.Capture(elapsed)` and `Apply(frame)` record and replay directly, as the tests do:

```csharp
// F5 starts recording, F6 saves it, F7 replays the saved one from a fresh level.
if (input.WasPressed(Keys.F5)) game.StartRecording();
if (input.WasPressed(Keys.F6) && game.StopRecording() is { } run) store.WriteText("run.txt", run.Save());
if (input.WasPressed(Keys.F7) && store.ReadText("run.txt") is { } saved)
{
    scenes.Reset(new LevelScene(seed));
    game.Replay(InputRecording.Load(saved));
}
```

## Camera and resolution independence

`Camera2D` decides which part of a world shows in a screen viewport: `Position` is the world point in the middle,
`Zoom` the screen pixels per world unit, and `Rotation` turns the view. Draw with its `Transform` and turn clicks
back into the world with `ScreenToWorld`:

```csharp
camera.Viewport = viewport;
camera.Bounds = new Rectangle(0, 0, worldWidth, worldHeight);   // stop at the world's edges; centre a small world
camera.Follow(player.Position, elapsed, rate: 8, deadZone: new Vector2(24, 16));
camera.Update(elapsed);                                          // shakes
batch.Begin(transformMatrix: camera.Transform, samplerState: SamplerState.PointClamp);

if (input.Tap is { } tap) { var world = camera.ScreenToWorld(tap); ... }
camera.Shake(strength: 6, TimeSpan.FromMilliseconds(250));       // a hit, an explosion
```

`Follow` eases towards its target (quick at first, settling gently), optionally only once the target leaves a dead
zone; `LookAt` jumps. `RoundToPixels` snaps the view to whole screen pixels so scaled pixel art doesn't shimmer, and
`VisibleArea` tells which part of the world to draw. The RPG kit's `MapView` uses one (`MapView.Camera`): it follows
the player through the map, stops at its edges, and shakes when the player is hit.

Jentl games normally lay themselves out for whatever window they get (scenes receive the viewport, and `Fonts`
scale with it). A game designed for one resolution sets `JentlGame.VirtualScreen` instead:

```csharp
VirtualScreen = new VirtualScreen(320, 180, ScaleMode.PixelPerfect) { BarColor = Color.Black };
```

Scenes then draw into a render target of that size (their viewport is `VirtualScreen.Bounds`), which is scaled
into the window, and the pointer (mouse, touch, gamepad cursor) arrives in virtual pixels. `Fit` scales as large
as fits and letterboxes, `PixelPerfect` keeps to whole-number scales for crisp pixel art, and `Expand` fills the
window by showing more of the world instead of bars. `JentlGame.ScreenBounds` is the area to lay out in either
way. The app can try it: `--virtual 640x400:expand`.

## Timers and coroutines

`Jentl.Core.Timing.Scheduler` runs timers and coroutines. Every scene has one (`Timers`) that runs only while the
scene is on top, so a pause menu stops its clock, and that is cancelled when the scene goes. The `SceneManager` has
one more that always runs, even through transitions:

```csharp
Timers.After(TimeSpan.FromSeconds(2), () => _message = null);
var blink = Timers.Every(TimeSpan.FromMilliseconds(400), () => _cursorOn = !_cursorOn);
blink.Cancel();
```

A coroutine is an iterator method that yields what it waits for, so a sequence reads top to bottom instead of
being spread over flags and countdowns in `Update`:

```csharp
Timers.Start(Ending());

IEnumerable<Wait?> Ending()
{
    yield return Wait.Seconds(0.35);
    Celebration.Sparkle(Scenes, padlock, ParticleEffects.Rise);
    yield return Wait.Until(() => !view.IsAnimating);
    yield return Wait.For(Fanfare());         // another routine, run in line
    Scenes.Push(new EndScene());
}
```

The waits are `Wait.Seconds`/`Wait.For(TimeSpan)`, `Wait.NextFrame` (or `yield return null`), `Wait.Frames(n)`,
`Wait.Until`/`Wait.While` a condition, and `Wait.For` a `Tween`, a started `Coroutine` or a nested routine. A
coroutine runs at once up to its first wait. Time left over when a wait ends carries into the next one, so a run of
waits (or `Every`) stays exact however the frames fall, and a long frame catches up. `Coroutine.Cancel` stops one
(its `finally` blocks run, even when it cancels itself), `IsPaused` holds it, and `Scheduler.TimeScale` slows
time down for slow motion. The code lock's win (sparks, then the end panel) and the second confetti wave are
coroutine timers.

## Pools

Things made and dropped many times a second (bullets, sparks, floating damage numbers, lists for a path search)
feed the garbage collector, whose pauses show as hitches. A `Pool<T>` keeps them for reuse: `Get` takes one (making
it when none is free), `Return` gives it back, cleaned by the reset you gave; returning one twice throws, since it
would then be handed out twice. `Prewarm` makes them up front, behind a loading screen, and `Created` growing for
ever means something is never returned. An `ActivePool<T>` also keeps the live ones: `Spawn`, then `DespawnWhere`
once a frame lets go of the finished, and enumerating gives the rest:

```csharp
var bullets = new ActivePool<Bullet>(() => new Bullet(), b => b.Age = 0);
bullets.Spawn().Fire(muzzle, aim);
foreach (var b in bullets) b.Move(elapsed);
bullets.DespawnWhere(b => b.Age > b.Life || !arena.Contains(b.Position));
```

## Debug overlay

F3 (or the game's own switch; the app has one in Options) shows `SceneManager.Debug`, a panel over everything:
frame rate with average, worst, update and draw times, a graph of the last two seconds of frames (marks at 60
and 30 frames a second), memory and garbage collections, the window and virtual screen, the pointer and gamepad,
the scene stack, and the particle and timer counts. Give it an `ITextDrawer` (`Debug.Text`) for the text; without
one only the graph shows.

Scenes add their own lines and shapes, shown only while the panel is up:

```csharp
public override IEnumerable<string> DebugInfo => [$"Level {level}", $"Enemies {enemies.Count}"];

public override void DrawDebug(SpriteBatch batch, Rectangle viewport)
{
    foreach (var enemy in enemies) batch.StrokeRectangle(enemy.Bounds, Color.Red, 2);
}
```

`DrawDebug` draws in the scene's own coordinates (inside the virtual screen when there is one). The kits have
ready-made ones: `TableView.DrawDebug` outlines every pile and the card under the pointer, and `MapView.DrawDebug`
marks portals, unfired triggers and the cell under the pointer. For values that aren't tied to a scene there are
watches and a log: `Debug.Watch("Seed", () => seed)` is read every time the panel draws, and `Debug.Log("Saved")`
adds a line that fades after a few seconds. `JentlGame.DebugInfo` can be overridden to add game-wide lines.

Where the frame's time goes shows under "Profile": `Debug.Profiler` times named scopes, averaged and at their worst
over the last 60 frames. `JentlGame` measures `update` and `draw`; measure the parts of your own inside them, and
scopes inside scopes nest (`update/physics/contacts`, indented). A scope run several times a frame adds up and says
how often (`x3`):

```csharp
using (Scenes.Debug.Profiler.Measure("physics")) world.Update(elapsed);
using (Scenes.Debug.Profiler.Measure("ai")) foreach (var m in monsters) m.Mind.Tick(m, elapsed);
```

## Inspector

F4 opens the inspector: a panel on the right that looks inside the top scene and changes it while the game is
paused. It lists what the scene offers in `Scene.Inspectables`, by default the scene itself, so every scene can be
inspected with no work: its public properties and all its fields, private ones too (`_speed` shows as `speed`;
a field behind a property that can be set is left to the property).

| Keys and pointer | What they do |
|---|---|
| Up, Down, PageUp, PageDown | Choose a value |
| Left, Right (Shift ×10, Ctrl ÷10), or the wheel over it | Change it: a number by a tenth of its size (10 for 420), a whole number by one, a switch flips, an enum steps, a time by a tenth of a second |
| Enter | Open an object, a struct or a list; flip a switch; or type a new value (Enter sets it, Escape drops it) |
| Backspace, Escape | Back up a level; Escape at the top closes the panel |
| Click, drag | Click a thing in the scene to inspect it; drag it to move it |
| P, N | Run the game with the panel open, or step it one frame while paused |
| C | Copy the change log (`Paddle.Speed: 300 → 420`) to put the tuned values in the code |
| Z | Take the last change or drag back, one at a time |
| M | Move the panel to the other side, to see what it covers |

A member of a struct writes the whole struct back (a vector's `X`, a colour's `G`). While the panel is open the game
gets no keys of its own, so they never do two things. Offer the things in a scene with bounds, in the scene's
coordinates, to outline them and make them clickable, and a move to make them draggable; `Inspectable.Bodies` does
that for every body of a physics world, and `Inspectable.Entities` lists an ECS world's entities with their
components (edits to a struct component go back into the world):

```csharp
public override IEnumerable<Inspectable> Inspectables =>
[
    new(nameof(LevelScene), this),
    new("Player", player, () => player.Bounds, delta => player.Position += delta),
    .. Inspectable.Bodies(world, ToScreen, scale),
    .. Inspectable.Entities(ecs, entity => ecs.Has<Name>(entity) ? ecs.Get<Name>(entity).Text : $"entity {entity.Id}"),
];
```

It edits the running game, not files: the change log is how a tuned value finds its way back into the code.

## Render targets, lighting and post effects

`RenderLayer` is an offscreen picture: `Render(batch, size, clear, draw)` switches to its target (made and resized
as needed, keeping its contents), draws, and switches back. Scenes draw their layers in `Scene.DrawLayers`, which
runs for every visible scene before anything goes on screen, then use them as textures in `Draw`. `BlendModes` adds
`Multiply`, `Add`, `Subtract` and `Screen` to the built-in blend states.

`LightMap` is 2D lighting built on that. Render the lights over an `Ambient` colour, then lay the map over the
area: unlit parts sink into the ambient, lit ones keep (and take on) the light's colour.

```csharp
public override void DrawLayers(SpriteBatch batch, Rectangle viewport) =>
    _lights.Render(batch, area, [new Light(player, radius: 200, Color.Wheat), new Light(torch, 120, Color.Orange)]);

// Draw: draw the area, then
_lights.Apply(batch);
```

`MapView.Lights` gives the RPG kit's lights: one carried by the player (`CarriedLight`, in tiles) and one per tile
with a `light` property (radius in tiles, `light_color` as `#rrggbb`), all flickering gently. Mossbrook's cellar
uses them: torches glow orange, glowcaps teal, and the player's lantern lights the way.

Whole-screen effects go in `SceneManager.Post.Effects` and run in order over the finished frame (the game draws
offscreen only while one is active): `Bloom` (bright parts bleed light; `Threshold`, `Intensity`), `Vignette`,
`Scanlines`, `Pixelate`, and `ShaderPass` for a compiled shader. `Post.Flash(color, duration)` washes the screen
and fades out (the adventure flashes red when the player is hit). The app's Options has a "Retro screen" switch
(bloom, scanlines and a vignette). A custom effect overrides `PostEffect.Apply` and draws with `PostContext.Draw`
and `Fill`.

Everything above uses only render targets and blend states, so it runs on desktop and in the browser without
compiling shaders. Custom shaders need compiling first, for each platform: MonoGame's `mgfxc` for the desktop
(needs Wine on macOS and Linux) and KNI's content pipeline for the browser. A loaded `Effect` then goes in a
`ShaderPass`, with `Prepare` setting its parameters.

## ECS and physics

Two more framework-free cores, for games of many moving things rather than turns.

**`Jentl.Ecs`** is an entity component system. An `Entity` is an id with a generation (so a stale handle never
reaches the entity that reuses its id); what it *is* lives in its components, plain structs or classes stored per type
in sparse sets: packed for fast loops, with constant-time lookup and removal. Systems run the rules over queries:

```csharp
var world = new World();
var ball = world.Spawn().With(new Position(10, 20)).With(new Velocity(100, 0)).Entity;

world.ForEach((Entity e, ref Position p, ref Velocity v) => { p.X += v.X * dt; p.Y += v.Y * dt; });
foreach (var e in world.Query<Brick, Physical>()) { ... }

world.Get<Position>(ball).X = 0;         // components by reference
world.SetResource(physics);              // one shared value per type
world.Emit(new BrickHit(...));           // events, taken with world.Drain<BrickHit>()
```

Destroying or changing entities inside a query is safe: each entity is checked again just before its turn. A
`SystemGroup` runs systems (classes or lambdas) in order and can switch them off by name.

**`Jentl.Physics`** is 2D rigid-body physics on `System.Numerics`: circles and axis-aligned boxes that don't rotate,
which covers most arcade games. Bodies are static (walls), dynamic (moved by gravity, forces and collisions) or
kinematic (moved by their velocity and never pushed: platforms, paddles). Each has mass, restitution (bounciness;
the higher of two counts), friction (the geometric mean), gravity scale, damping, collision layers and masks, and can be
a sensor that reports overlaps without colliding.

```csharp
var physics = new PhysicsWorld { Gravity = new Vector2(0, 500) };
physics.Add(new Body(new Box(400, 20), new Vector2(0, 100), BodyType.Static));
var crate = physics.Add(new Body(new Box(20, 20), Vector2.Zero) { Restitution = 0.3f });
physics.ContactBegan += contact => { ... };      // and ContactEnded
physics.Update(elapsed);                         // whole fixed steps (120 a second)
physics.Raycast(origin, direction, 500);         // also QueryArea and QueryPoint
```

`Update` runs whole fixed steps, so the simulation is the same at any frame rate (`Alpha` tells how far into the next
step it is, to draw in between). Each step applies gravity and forces, finds pairs by sweep and prune (sorted along x,
so only neighbours are tested), settles contacts with impulses over a few iterations, moves everything and nudges
overlaps apart. Slow impacts don't bounce, so resting things stay put.

**Bricks** puts them together: paddle, balls, bricks, power-ups and walls are entities, each with a `Physical`
component holding its body (whose `UserData` points back to the entity). Systems in order steer the kinematic
paddle, carry balls stuck to it, step the physics, apply the rules to the contacts (break bricks, catch power-ups,
angle the ball off the paddle), keep balls at speed and move on when a wall is cleared, emitting events the screen
turns into sounds and sparks. Balls float (gravity scale 0) while power-ups fall. `PhysicsDebug.Draw` outlines
every body and contact for a scene's `DrawDebug`.

### Joints and fast bodies

Bodies don't rotate, so joints hold distances between body centres, or between a body and a fixed point (the
`Anchor`, movable): `DistanceJoint` is a rigid rod, `RopeJoint` lets the ends come together but never further apart
than its length (`IsTaut` says when it is pulled straight), and `SpringJoint` pulls towards its rest length with a
stiffness and a damping that lets it settle. Leave the length out and a joint keeps the distance its ends have when
made. Joints are solved with the contacts, then their lengths corrected; removing a body removes its joints, and
`PhysicsDebug` draws them in violet.

```csharp
var ball = world.Add(new Body(new Circle(12), new Vector2(200, 80)) { Mass = 20 });
world.Add(new RopeJoint(ball, anchor: new Vector2(200, 0)));          // a wrecking ball on a chain
world.Add(new SpringJoint(car, trailer, stiffness: 400, damping: 30));  // a springy tow bar
```

A body moving further in one step than a wall is thick jumps right through it. Mark the few fast ones `IsBullet`:
their every move is swept against the other solid bodies, stopping (or bouncing, by their restitution) at the first
one in the way. A circle meeting a box treats the box's corners as square, so it stops a hair early there.

## AI: steering and behaviour trees

`Jentl.Ai` is framework-free, on `System.Numerics`. A `Vehicle` steers itself, Reynolds style: each behaviour returns
the force that would turn its velocity towards what it wants (reaching it in about 1 / `Responsiveness` seconds, as
far as `MaxForce` allows), the game adds up the ones that matter, weighted, and `Move` applies the sum. `Seek`, `Flee`
(within a panic distance), `Arrive` (slowing to stop on the spot), `Pursue` and `Evade` (aiming where a mover will
be), `Wander` (a smooth amble), `Avoid` (round obstacles ahead), and for crowds `Separation`, `Alignment`, `Cohesion`
and all three as `Flock`. To steer a physics body, copy its position and velocity in and apply the force to the body.

```csharp
foreach (var sheep in flock)
    sheep.Move(sheep.Flock(flock, radius: 80) + sheep.Flee(dog.Position, panicDistance: 120) * 2, seconds);
```

A `BehaviourTree<T>` is a mind over a game's own `T` (the monster and what it sees), ticked once a frame. `Bt` builds
it: a `Selector` tries its children in order and takes the first that doesn't fail, checking the higher priorities
again every tick (so attacking takes over from a running patrol); a `Sequence` runs steps in order, picking up where
it was while one runs; `While` drops a running plan the moment its reason stops holding. `If` and `Do` are the leaves,
`Wait`, `Not`, `Try`, `Repeat` and `Cooldown` (by the tree's own time, so it runs out while its branch isn't reached)
the rest. `Best` is utility AI: it scores each option every tick and runs the highest, with a stickiness so a running
choice isn't dropped for one barely better.

```csharp
var mind = new BehaviourTree<Goblin>(Bt.Selector(
    Bt.While<Goblin>(g => g.SeesPlayer, Bt.Sequence(
        Bt.Cooldown(TimeSpan.FromSeconds(3), Bt.Do<Goblin>(g => g.Throw())),
        Bt.Do<Goblin>((g, elapsed) => g.Chase(elapsed)))),
    Bt.Best<Goblin>(0.1f,
        (g => g.Hunger, Bt.Do<Goblin>((g, elapsed) => g.Forage(elapsed))),
        (g => 0.3f, Bt.Do<Goblin>((g, elapsed) => g.Patrol(elapsed))))));

mind.Tick(goblin, elapsed);
```

## Scripting

`Jentl.Script` is a small language for the parts of a game that are content rather than engine: cutscenes, dialogue,
events, level logic, tuning. Scripts are text the game loads at run time (from an asset, a level file, a JSON field),
so they change without a rebuild, and with `Reload` while the game runs. It is framework-free and runs in the browser
too: an interpreter, nothing generated at run time.

```
# A cutscene. Comments start with #.
let elder = actors.elder

fn walk(who, x)
  while who.x < x do
    who.x += 2
    wait()                          # a frame
  end
end

say(elder, "You came back, {player.name}.")
walk(elder, 120)
if has("amulet") then
  say(elder, "And you found it!")
  gold += 50
elif chance(0.5) then
  say(elder, "Still nothing?")
else
  let answer = ask("Keep looking?", ["Yes", "No"])
  if answer == "No" then return end
end
for i, line in ["Off you go.", "Mind the bridge."] do
  say(elder, line)
  wait(0.5)
end
```

- **Values**: `nil`, `true`/`false`, numbers (all doubles; `1_000`, `2.5e3`), text in `"double"` or `'single'`
  quotes (double ones fill in `{expressions}`; `\n`, `\{` and kin escape), lists `[1, 2]` (from 0; `list[-1]` is the
  last), maps `{name: "Ash", "max hp": 20, [key]: value}` (`map.name` or `map["max hp"]`; a missing key is nil, and
  setting nil removes it), functions, and the game's objects.
- **Statements**: `let name = value` (a new variable), `name = value` and `+= -= *= /=`, `if ... then ... elif ...
  else ... end`, `while ... do ... end`, `for item in list do ... end` (or `for i, item in`; over a map, `for key in`
  or `for key, value in`; also text and `range(stop)`, `range(start, stop, step)`), `break`, `continue`, `fn name(a,
  b) ... end`, `return`. A statement is a call or an assignment, so `x == 1` alone is a mistake, not a no-op.
- **Operators**: `+ - * / %` (`%` takes the sign of the divisor, so `-1 % 4` is 3; dividing by zero is an error),
  `+` also joins text (with anything) and lists, `== != < <= > >=` (numbers and text), `and or not` (`and` and `or`
  give one of their operands: `name or "stranger"`). Only nil and false are false.
- **Functions** are values: `fn(x) return x * 2 end`, closing over the variables around them. Functions made in a
  loop share its variable, so pass it as an argument instead (`spawn(fn(n) ... end, i)`).
- **Scope**: `let` and `fn` at the top level of a script make the script's globals, which the game reads and calls;
  inside a block they are local to it. Any other name is a global: the script's, then the engine's. Assigning to a
  name nobody declared is an error, so typos don't make new variables.

The standard functions: `print`, `str`, `num`, `type`; `upper`, `lower`, `trim`, `split`, `join`, `replace`,
`startsWith`, `endsWith`; `floor`, `ceil`, `round(x, digits)`, `abs`, `sqrt`, `sin`, `cos`, `atan2`, `sign`, `min`,
`max` (of numbers or a list), `clamp`, `lerp`, `pi`; `random()` (0 to 1), `random(n)` (0 to n - 1), `random(a, b)`
(a to b), `chance(p)`, `pick(list)`, `shuffle(list)` (by the engine's `Random`, seeded for repeatable runs); `len`,
`range`, `list(anything a for loop goes through)`, `push(list, items...)`, `pop`, `insert(list, at, item)`,
`remove(list, at)` or `remove(map, key)`, `contains`, `indexOf`, `keys`, `values`, `copy`, `slice(list, from, to)`,
`sort(list, key)`, `map(list, fn)`, `filter(list, fn)`; `wait(seconds)` (no seconds: one frame), `waitUntil(fn)`,
`spawn(fn, args...)`, `stop(thread)`, and `error(message)`.

The game hosts scripts in a `ScriptEngine`. `Define` gives scripts a function (reading arguments from the
`ScriptCall`, which says what was wrong with a wrong one), `Expose` one of the game's objects (its public properties,
fields and methods, by name in any case: `player.health` is `Health`; enums by name; its lists index and its
dictionaries with text keys read like maps; a struct comes back as a copy, so set it whole), and the indexer a shared
value the game and scripts both read and change.

```csharp
var scripts = new ScriptEngine();
scripts.Expose("player", player);
scripts["gold"] = 10;
scripts.Define("has", call => inventory.Contains(call.Text(0)));
scripts.Define("say", call =>
{
    dialogue.Show(call.Host<Actor>(0), call.Text(1));
    call.WaitUntil(() => dialogue.IsClosed);           // the script carries on when the box is closed
});
scripts.Define("ask", call =>
{
    var choosing = call.Suspend();                     // ask returns what the player picks
    menu.Open(call.Text(0), call.List(1).Items.Select(item => item.ToString()), choice => choosing.Resolve(choice));
    return ScriptValue.Nil;
});

var level = scripts.Run(File.ReadAllText("level1.script"), "level1");   // runs the top level: lets, fns
level.Call("on_enter");                                                 // at once; may not wait
level.Start("intro");                                                   // as a thread; may wait
if (scripts.Evaluate("gold >= 10 and player.health > 0").IsTruthy) ...  // a condition in a level file

// Once a frame, so waiting threads carry on:
scripts.Update(gameTime.ElapsedGameTime);
```

A thread is a script function running on its own stack: when it calls `wait`, or a game function that waits
(`call.Wait`, `call.WaitUntil`, or `call.Suspend` until the game resolves the promise with the call's answer), it
stops there and carries on in a later `Update` while the game and other threads go on. Threads start in the next
`Update`; `ScriptThread` has the status, the result and `Stop`. A function the game calls directly (`Call`,
`Evaluate`, `Run`) needs its answer at once, so waiting there is an error.

Mistakes say where they are. One in the text (`level1:12: expected 'end' to close 'if', got the end of the script`)
is a `ScriptSyntaxException` from `Load`, with the line and column; one while running (`level1:30: can't add number 3
and nil`) is a `ScriptException` with the calls it was in (`Trace`). A thread that fails in `Update` raises `Failed`,
or, with no handler, `Update` throws. A thread that runs more than `InstructionLimit` steps without waiting (an endless
loop) fails rather than freezing the game, as do calls deeper than `MaxDepth`.

Functions made in a loop keep that time round's variables (`for i in items do push(fns, fn() return i end) end`
gives each its own `i`), and setting a part of one of the game's structs in one go (`thing.position.x = 5`) puts the
changed copy back; a copy kept in a variable is the script's own.

The RPG kit's content files take scripts as conditions and effects, `{"script": "var(\"gold\") >= 10"}` and
`{"script": "give(\"potion\", 2)"}`, run by whatever is set as `RpgState.Scripts`. `Jentl.Core.Rpg.RpgScripts` runs
them with Jentl.Script, giving scripts the state: `flag`, `setFlag`, `clearFlag`, `var`, `addVar`, `has`, `give`,
`take`, `quest` (its state as text), `startQuest`, `completeQuest`, `heal` and `say`:

```csharp
state.Scripts = new RpgScripts(state);   // its Engine takes more functions for a game's own content
```

## Online multiplayer

`Jentl.Net` (framework-free) plays turn-based games over the internet through a room server. Players connect with
a WebSocket, one of them creates a room and shares its four-digit code, the others join with it, and the host
starts: bots take the seats nobody joined (and the seat of anyone who leaves mid-game). The server is
authoritative: it holds the game, checks every action against the rules, and sends each seat only what that seat may
see, so a modified client still can't peek at other hands.

| Piece | What it does |
|---|---|
| `Protocol` | JSON messages, one per WebSocket text frame: `CreateRoom`, `JoinRoom`, `StartGame`, `PlayerAction` and `LeaveRoom` up; `RoomJoined`, `LobbyChanged`, `GameStarted`, `GameState`, `ServerError` and `RoomClosed` down. Game moves and states travel as the game's own JSON inside `PlayerAction` and `GameState` |
| `IRoomGame` | A game the server can host: `Start` with the seats, `Act` for a seat's action (returning why not, if it isn't allowed), `Update` for bots, `Replace` a leaver with a bot, and `StateFor(seat, since)`: that seat's view and the events since the last state |
| `RoomServer` | Rooms, codes, the lobby, starting, passing actions in and states out (only when the game's `Version` changes), bots for leavers, closing empty rooms. Thread-safe; knows nothing about any particular game |
| `SocketConnection` | Serves one player's WebSocket for the room server, with a size limit on messages |
| `NetClient` | The device's side: connects (desktop and browser alike), sends in the background, and queues what arrives for the game loop to take with `TryReceive` once per frame |

`src/Jentl.Server` is the server itself, a small ASP.NET Core app: WebSockets on `/play`, a status line on `/`, and
a ticker that lets bots move ten times a second. Run it with `dotnet run --project src/Jentl.Server` (it listens on
port 5080), or build the container with `docker build -f src/Jentl.Server/Dockerfile -t jentl-server .` from the
repository root. Hosting it publicly means putting it behind TLS (browsers on an `https` page may only open `wss://`
connections), for example behind a reverse proxy.

Uno is the first game online. `UnoView` is a round as one seat sees it (its own hand, everyone else's card count,
its legal moves); the local table and the server both hand the screen one, so the same screen plays local and
online games through `IUnoTable`. `UnoRoomGame` is the server side. A new game needs its own `IRoomGame` and a line
in the server's game list.

There are no accounts: players are "Player 1" to "Player 4" by seat, and a dropped connection can't rejoin its seat
(a bot carries on for it).

### Agents in their own process

A bot can be a program of its own, in any language: it reads one line on stdin and writes one line on stdout. Every
wait has a time limit, so a silent or crashed agent costs a turn, not the game. Nothing throws for a misbehaving agent:
`Problem` says what went wrong (`"timed out"`, `"exited with code 3"`), and `ErrorOutput` keeps the last lines it wrote
to stderr. An agent that floods its output costs little: at most `MaxLines` (1,024) lines wait to be read, the oldest
dropped first, and a line is cut at `MaxLineLength` (1 MiB) characters.

| Part | What it does |
|---|---|
| `AgentCommand` | What to run: the program, its arguments (passed as they are, no shell) and the working directory |
| `AgentProcess.Start` | Starts the agent; never throws, a command that cannot start is an agent that has already exited |
| `AgentCommand.DotNet(dll, args)` | Runs a .NET program through its apphost when it has one (which finds the runtime without `dotnet` on the PATH), else as `dotnet dll` |
| `AgentCommand.Find(name, folders)`, `SearchFolders()` | The full path of a program in the first folder that has it; the PATH's folders, and on macOS `/usr/local/bin` and `/opt/homebrew/bin`, which an app started from the Finder does not have on its PATH |
| `AskAsync(line, limit)` | Throws away the lines the agent sent before (late answers to earlier questions), sends `line`, and returns its answer, or null when none came in time |
| `Send`, `ReadAsync(limit)` | Sends a line without ever blocking; reads the next line with a time limit |
| `Problem`, `ErrorOutput`, `Stop` | What is wrong (or null), the last stderr lines, and killing the agent with everything it started |
| `IAgent` | What a game asks of a bot, wherever it runs: `Send`, `ReadAsync`, `AskAsync`, `HasExited`, `Problem`, `ErrorOutput`, `Dispose`. `AgentProcess` and `RemoteAgent` implement it. `IsAway` (false unless an implementation says otherwise) is true while a remote bot's connection is down, so a table can fold its seat at once |

```csharp
using var agent = AgentProcess.Start(new AgentCommand("python3", ["bots/random_bot.py"], WorkingDirectory: botsDir));
var answer = await agent.AskAsync(StateJson(game, seat), TimeSpan.FromSeconds(2));   // null when it was late
```

### Agents on their owners' computers

An agent can also run on its owner's computer and play on a server: a runner starts it and relays its lines over a
WebSocket. The server says when to start and stop it and sends it lines; the runner sends back the agent's lines, in
the order it wrote them, and its exit.

| Part | What it does |
|---|---|
| `Relay` | The JSON messages (all a `RelayMessage`), one per text frame with a `type`: `RelayHello` (token and `Relay.Version`), `RelayLine` and `RelayExited` up; `RelayWelcome`, `RelayRefused`, `RelayStart`, `RelayLine`, `RelayStop` and `RelayStatus` (the game's own JSON, for the runner to show) down. `RelayStart` carries a run number, which the runner echoes in the `RelayLine` and `RelayExited` it sends, so the server can drop what a stopped process wrote late. `Write`, and `Read`, which gives null for anything it does not know |
| `SocketLines` | A WebSocket as lines: `Send` queues without blocking, `ReadAllAsync` reads until the socket closes, `CloseAsync` sends what is queued and closes. A message over `maxLength` bytes (64 KiB) or in more than `MaxFrames` (1,024) frames, more than `maxPerSecond` (200) a second besides its answers, or a binary message closes the socket with a policy violation, and `Problem` says which. Every line sent lets the peer send one more, so a peer that answers each line is never too fast; the rest is a token bucket of `maxPerSecond` a second with bursts of as many (`int.MaxValue`: no rate limit) |
| `AgentRelay` | The runner: `RunAsync` connects, says hello and does what the server asks; a dropped connection is tried again after 1, 2, 4 … 30 seconds while the agent keeps running, and only `RelayRefused` (or cancelling) ends it. `Status` tells the owner what happens ("connected as Bot", "reconnecting in 4 s", a line dropped). The server's messages are limited in length but not in rate: it sets the pace, and a match between fast bots sends hundreds of lines a second. The runner keeps to the server's limits itself, so a bot that prints more than it is asked loses lines, not its connection: a line that would make a message over `MaxLength` bytes (64 KiB; set it to the server's) is dropped, and so are lines beyond `ExtraPerSecond` (100) a second besides the answers (each message from the server allows one). A start for the run its agent already has (sent again after a reconnect) keeps that agent running |
| `RelayHost` | The server: `RunAsync(socket, aborted)` serves one runner's connection (hello within 5 seconds, token checked through the `nameOfToken` function you give it, and once more just before the connection is attached, in one step with it that `Drop` cannot come between: a token replaced meanwhile is refused with "token replaced" and never takes the seat) and ends when it closes, answering the limit the runner broke if that closed it (for your log). `Agent(name)` is the bot's `RemoteAgent`, null until its runner first connects. One connection per bot: a second takes over and the first is told "connected elsewhere". `Connected` and `Disconnected` say when a bot comes or goes. A runner that breaks `MaxLength` or `MaxPerSecond` loses only its own connection. `Drop(name, reason)` closes a bot's connection and refuses the runner with the reason (after its token was replaced, or a ban: the token is checked only at hello). Run numbers start at a random number for each host, so after a server restart a runner never keeps a process from the old server's match, and its late lines are ignored |
| `RemoteAgent` | A bot on another computer, as an `IAgent`. While no runner is connected it is away: `Send` drops the line and reads answer null at once. `Restart` has the runner start a fresh process for a new match; `Stop` stops it; either is sent again on reconnect until answered. Lines from before the first start are ignored; the lines that wait are bounded (1,024 lines, `MaxWaiting` characters) and forgotten when the runner goes. The same instance stays the bot's throughout, so a reconnecting runner keeps its seat. `ErrorOutput` is always empty: stderr stays with the owner. `Status(json)` shows the owner something |

```csharp
using var relay = new AgentRelay(async cancel =>
{
    var socket = new ClientWebSocket();
    await socket.ConnectAsync(new Uri("wss://example.org/bots"), cancel);
    return socket;
}, token, new AgentCommand("python3", ["my_bot.py"]));
relay.Status += Console.WriteLine;
await relay.RunAsync(cancel);
```

On the server, the host takes the sockets and the game asks the bot as it would a local agent:

```csharp
var host = new RelayHost(token => tokens.GetValueOrDefault(token));   // the bot's name, or null for an unknown token
// in the endpoint for "/bots": await host.RunAsync(await context.WebSockets.AcceptWebSocketAsync(), context.RequestAborted);

if (host.Agent("Bot") is { IsAway: false } bot)
{
    bot.Restart();                                                    // a fresh process for a new match
    var answer = await bot.AskAsync(StateJson(game, seat), TimeSpan.FromSeconds(2));   // null when late, gone or away
}
```

## Leaderboards

High-score tables on the device, and online ones on the game server.

| Part | What it does |
|---|---|
| `Jentl.Collect.Leaderboard` | The best scores, best first, up to a capacity: `ScoreOrder.HigherIsBetter` (points) or `LowerIsBetter` (time, moves). `Submit(entry)` returns the place it took (0 is the top) or null; equal scores keep the order they came in; `bestPerName` keeps one line per name. `ToJson`/`FromJson` for the save file (the order and capacity stay in code) |
| `Jentl.Collect.PlayerNames` | Made-up names such as "Swift Otter 27" (adjective, animal, 10 to 99): `Make(random)` for a new one, `IsValid(name)` to check one. No typing, and nothing of a player's own wording on a public board |
| `Jentl.Net.ScoreServer` | The online boards: `BoardRules` per board name (order, the range a real score falls in, capacity), one line per name, kept in a JSON file. Turns away unknown boards, names `PlayerNames` would not make and scores out of range. Give it to the `RoomServer` (`scores:`), which passes it `SubmitScore` and `GetScores`; the answer is `Scores`: the top ten, and the player's place and line |
| `Jentl.Core.UI.LeaderboardTable` | Draws a board: `Rows(top, rank, mine, format)` numbers the lines and picks out the player's (adding it after a gap when they are further down), `Draw` puts them on screen |

```csharp
var board = Leaderboard.FromJson(saved, ScoreOrder.HigherIsBetter);
if (board.Submit(new LeaderboardEntry(name, score, DateTimeOffset.Now)) is { } place) { /* a new high score, at place */ }
client.Send(new SubmitScore("bricks", name, score));          // online, if there is a server
var rows = LeaderboardTable.Rows(board.Entries, place, null, s => s.ToString("N0"));
```

The server can't see the game being played, so a player who wants to can send a made-up score inside a board's range;
keep the ranges tight.

## Localization

`Jentl.Core.Localization.Localizer` translates gettext style: the English text is the key, so code reads naturally
and anything untranslated simply shows in English. A language is a JSON object from English text to translation
(keys starting with `//` are notes for translators):

```json
{ "Select level": "Kies level", "{0} wins the round!": "{0} wint de ronde!", "{0} move": "{0} stap", "{0} moves": "{0} stappen" }
```

```csharp
var nl = Localizer.Load("nl", json);
nl["Select level"];                              // "Kies level"
nl.Format("{0} wins the round!", "Ruby");        // "Ruby wint de ronde!"
nl.Plural(count, "{0} move", "{0} moves");       // "1 stap", "3 stappen"
nl.Format("Score {0:N0}", 1234567);              // "Score 1.234.567": numbers the language's way
Localizer.Pick(CultureInfo.CurrentUICulture, ["en", "nl"]);   // "nl" for nl-BE, "en" when nothing fits
```

`Missing` collects texts that were asked for but have no translation. The engine's own widgets say little; what
they do say (`UiText.On`, `UiText.Off`) can be set, and `ChoiceRow` offers a pick-one row for forms (a language,
a difficulty).

The app writes `T("...")`, `T("{0} wins", name)` and `N(count, "{0} move", "{0} moves")` (`Sokoban.Desktop.UI.Tr`),
with translations in `src/Sokoban.Desktop/Assets/lang/<language>.json`. A test scans the source for every such text
and checks each language file has them all, keeps their placeholders, and holds nothing unused, so a new text
can't slip through untranslated. Whole sentences are translated, never pieced together, so word order and verb forms
can change ("You play" and "Ruby plays" are separate texts). Adding a language means adding its file and a line in
`Tr.Languages`. The adventure's story (dialogue, quests, items) lives in its content files and stays in English.

## Saves

`Jentl.Core.Storage` keeps small save files: `SaveStore` in a folder on the desktop (`SaveStore.ForApp(name)` for
the user's application data), writing through a temporary file so a crash never leaves half a save, and
`Quarantine` to move an unreadable file aside. A browser game gives its own `ISaveStore` over local storage.

A save outlives the version of the game that wrote it. `SaveMigrations` brings an old JSON save up to date: the save
carries its version in a property (`"version"`, or a name you choose; none means 0), and each step turns the JSON of
one version into the next, so a save from any older version passes through every step after it. `Stamp` writes the
current version into a save; `Upgrade` runs the steps before the game parses it and throws `InvalidDataException`
for a save from a newer version, one with a missing step, or one a step could not change:

```csharp
static readonly SaveMigrations Migrations = new SaveMigrations(2)
    .Add(0, save => { save["coins"] = save["gold"]?.DeepClone(); save.Remove("gold"); })   // 0 → 1: renamed
    .Add(1, save => save["level"] = new JsonObject { ["number"] = save["level"]?.DeepClone() });  // 1 → 2

store.WriteText("progress.json", Migrations.Stamp(JsonSerializer.Serialize(progress)));
var progress = JsonSerializer.Deserialize<Progress>(Migrations.Upgrade(store.ReadText("progress.json")!));
```

The RPG kit's `RpgWorld.Save` has a `"version"` of its own, for its format; give the game's migrations another
property (`new SaveMigrations(3, "game")`) so the two never mix.

## Asset tooling

`Jentl.Assets` (framework-free) and the `tools/Assets` command line look after the files a game ships.

**Atlases.** Many small sprites are better as one texture: one file for a browser to download instead of dozens,
and one texture for the GPU to switch to. The tool packs a folder of PNGs into an image and a description:

```bash
dotnet run --project tools/Assets -- atlas src/Sokoban.Desktop/Art/sprites src/Sokoban.Desktop/Assets/sprites.atlas.json
```

Sprites are named by their path in the folder without the extension (`crates/crate3`), placed on shelves in the
smallest sheet that fits, and padded with their edge pixels repeated, so scaling never bleeds a neighbour in. The
description records the source folder and a fingerprint of it. The game loads it with `Assets.Atlas(path)` and draws
`atlas["crates/crate3"]`, a `Sprite` (a texture and a source rectangle; `batch.Draw(sprite, rect, color)`). Sokoban's
46 tile sprites live in `Art/sprites` (not shipped) and ship as one 512x512 `Assets/sprites.png`.

**Checks.** `dotnet run --project tools/Assets -- check src/Sokoban.Desktop/Assets` makes sure every image decodes,
every JSON file parses, every atlas fits its image and is up to date with its sprites, and Tiled maps and tilesets
load with every tile known and every image present (`MapCheck` in the RPG kit). A test runs the same check over the
game's assets, so a broken file or a forgotten atlas rebuild fails the build. `AssetCheck.Checks` takes more checks
by file extension.

**The asset store and hot reload.** `JentlGame.Assets` loads textures, atlases and text by path, once each, and
disposes them with the game. Started with `--watch src/Sokoban.Desktop` (desktop), it watches the game's source
folder: files there are read in preference to the built copies, and edits show up while the game runs. Textures and
atlases are refreshed in place (so sprites already handed out stay valid); editing a sprite in `Art/sprites` repacks
its atlas in memory, as the tool would; other files are re-read the next time they're asked for. `Assets.Changed`
names each reloaded file for whatever the game must redo itself: the app reloads its language file this way. Run the
`atlas` command afterwards to keep the edit (the check reminds you).

## Collecting and tracks: a collecting game with races

Paddock (a car collecting game) is built on these.

| Part | What it does |
|---|---|
| `Jentl.Collect.WeightedTable<T>` | Draws items in proportion to their weights (pack contents, loot); `Chance(item)` for showing the odds. The same `Random` seed draws the same items |
| `Jentl.Collect.RechargingCounter` | Tickets, energy or lives that come back one per interval on the wall clock, also while the game is closed. Save its `RechargeState` (a count and a time); `Count`, `NextIn` and `TrySpend` work it out at any moment. Setting the clock back gives nothing back |
| `Jentl.Collect.StableSeed` | A `Random` seed from text that is the same on every platform and run (FNV-1a), for seeding by an id |
| `Jentl.Collect.Rotation` | Equal periods from an epoch, for things on a calendar (this week's events): `PeriodAt(time)` and `Pick(items, time, offset)` give every device the same answer without a server |
| `Jentl.Collect.RunningStats` | The mean, standard deviation and 95% margin of a series of numbers, updated one value at a time (`Add`): `Count`, `Mean`, `StandardDeviation`, `Margin95` (how far the mean may be off). For comparing results of simulated runs |
| `Jentl.Collect.Glicko2` | Ratings for players of uneven strength, after Glickman's "Example of the Glicko-2 system". A `GlickoRating` is a strength (1500 is average), how unsure that is (`Deviation`, at most 350) and `Volatility`; `GlickoRating.New` is a player nobody has seen. `Glicko2.Update(player, results, tau)` rates one rating period from a list of `GlickoResult` (the opponent's rating and a score of 1, 0.5 or 0); without results only the deviation grows |
| `Jentl.Collect.KeyedRandom` | A `Random` from a secret key and a label (HMAC-SHA256 of the label and a counter): the same numbers on every platform and run, every label its own, and neither the key nor another label's numbers can be worked out from them. For deals and seeds that players must not be able to predict |
| `Jentl.Tracks.TrackPath` | A path built from `Straight` and `Arc` pieces (positive degrees turn left on a screen whose y points down): `At(distance)` gives position and heading, `CurvatureAt` the bend, `Polyline` points for drawing. A path whose end meets its start is a circuit and wraps |
| `Jentl.Assets.PixelCanvas` | Pixel art drawn in code: rectangles, lines, ellipses, outlines, mirroring; `ToImage()` for `AtlasBuilder`, `ToPng()` for a file |
| `Jentl.Core.Tracks.RoadPainter` | Paints a `TrackPath` as a road from above (verge, surface, kerbs in corners tighter than 60 units, a dashed centre line) into pixels, and `ToTexture` puts them on the GPU |
| `RoadPainter.PaintMap` | A small overview map of a path, and where a world position lands on it (for a mini-map) |
| `Jentl.Core.Graphics.Weather`, `ParticleEffects.Spray`, `ParticleEffects.Kickup` | Rain (slanted by gusting wind, with splashes) or drifting snow over the view, drawn from the clock alone; water spray and grit for behind wheels |
| `Camera2D.FollowHeading` | Follows a car and turns the view with it, so the road runs up the screen; the car stays at a chosen spot on the screen (lower shows more road ahead) |

```csharp
var tickets = new RechargingCounter(Max: 5, Interval: TimeSpan.FromMinutes(30));
var state = tickets.Full(DateTimeOffset.UtcNow);            // keep this in the save file
if (tickets.TrySpend(ref state, DateTimeOffset.UtcNow)) { /* race */ }

var oval = TrackPath.Build([new Straight(200), new Arc(40, 180), new Straight(200), new Arc(40, 180)]);
var pose = oval.At(distance);                               // where a car is, and which way it points

var payouts = new RunningStats();
foreach (var run in simulatedRuns) { payouts.Add(run.Payout); }
var average = $"{payouts.Mean:F1} ± {payouts.Margin95:F1}";  // the mean, with its 95% margin
```

## Geometry: shapes, tests and meshes

`Noise` is Perlin noise: a smooth random field that wanders between about -1 and 1 over a unit, the same for a seed
on every machine (it shuffles with its own generator, not `System.Random`). `At(x)`, `At(x, y)` and `At(x, y, z)`
sample a line, the plane or space (the third coordinate as time animates a 2D field); `Fractal` layers octaves for
detail, `Ridged` makes crests for mountains, cracks and rivers, and `Map` fills a grid from 0 to 1:

```csharp
var noise = new Noise(world.Seed);
var height = noise.Fractal(x / 64f, y / 64f);           // rolling terrain
var wobble = noise.At(time * 3f) * 4;                    // a hand-held camera
var clouds = noise.Map(256, 256, scale: 40, octaves: 6); // a cloud texture
```

`Jentl.Geometry` is the maths a renderer (or a game) needs on top of `System.Numerics`, which already has vectors,
matrices, quaternions and `Plane`. Right-handed, y up; a triangle's front is the side its corners run
counter-clockwise; `System.Numerics` projection conventions (row vectors, depth 0 to 1).

| Part | What it does |
|---|---|
| `Ray`, `Segment2`, `Segment3`, `Planes` | A ray's points; where segments cross; closest point on a segment; planes through a point or three points, signed distance, where a segment or ray meets one |
| `Triangle` | Normal, area, centroid, barycentric weights, containment, ray hits from either side (Möller–Trumbore) |
| `Polygon2` | Signed area and winding, point-in-polygon (concave too), ear-clipping triangulation that always gives counter-clockwise triangles; `Outline` traces a picture's solid pixels into one simple polygon (the biggest piece, holes filled, corners only); `Inset` moves a convex polygon's sides in (a window in its frame) |
| `Box2`, `Box3` | Axis-aligned boxes: contains, overlaps, union, corners, transformed bounds, ray entry (slab test) |
| `Frustum` | The six planes of a view-projection matrix; whether a point or box is in view |
| `Clipping.ClipPolygon` | Cuts a polygon of any vertex type by a plane (Sutherland–Hodgman), interpolating the new vertices |
| `Edge` | The edge function rasterizers walk (value, step per pixel) and a top-left fill rule so shared edges are drawn once |
| `Mesh`, `MeshBuilder` | Triangle meshes with normals (worked out if not given) and texture coordinates; boxes, planes, spheres, cylinders, cones, upright quads (`Billboard.Upright` turns one to face the camera) and extruded 2D outlines (their caps mapped across the outline or a picture you give, their sides reading colours just inside the outline or deeper, past a dark border); `Transform` moves a copy, `Combine` joins placed meshes into one |

```csharp
var car = MeshBuilder.Extrude(sideProfile, depth: 1.8f).Transform(Matrix4x4.CreateRotationY(MathF.PI / 4));
var frustum = Frustum.From(view * projection);
if (frustum.Test(car.Bounds) != Containment.Outside) { /* draw it */ }
```

## Raster: a software renderer

`Jentl.Raster` draws triangles on the CPU into a `FrameBuffer`, for small low-resolution pictures with a retro look
(the GPU draws everything else). Its pixels are packed like XNA's `Color`, so they go straight into a texture.

| Part | What it does |
|---|---|
| `FrameBuffer`, `DepthBuffer`, `Colors` | The picture (packed RGBA, top-left first) and the depth per pixel (0 near, 1 far); packing colours |
| `Camera` | Position, target, field of view, near and far; view, projection and view-projection matrices |
| `Renderer.DrawTriangle` | One triangle with vertex colours: transformed, clipped against the view (triangles through the camera's plane are cut, not smeared), back faces culled, pixel centres filled with the top-left rule (shared edges drawn once), depth-tested, colours blended perspective-correctly |
| `Renderer.DrawMesh` | A `Jentl.Geometry` mesh with a `Material` (colour and `Shading`: unlit, flat per face, or smooth per vertex) under a `Lighting` (one directional light and ambient) |
| `Texture` | An image on triangles (packed RGBA): nearest or bilinear sampling, repeat or clamp; texels with alpha under one half are cut out (nothing drawn, no depth written). A `Material` or `DrawTriangle` takes one, tinted by the colour; meshes from `MeshBuilder` all have texture coordinates (an extruded outline takes the texture across its caps, and its sides take the colours along their edge, like pixel art extruded into blocks) |
| `Renderer.Mapping` | `PerspectiveCorrect` (textures stay put on surfaces at an angle) or `Affine` (spread straight across the screen, as the first PlayStation did: the textures swim, a retro look) |
| `MeshModel`, `MeshModelBuilder`, `DrawModel` | A thing of several materials (paint, glass, tyres): add placed meshes with their materials, and pieces of one material are joined into one mesh; translucent parts draw last |
| `Material.Translucent` | Blended over what is behind by alpha, writing no depth: shadows (`Textures.Spot` makes a round one), glass, water; draw after the solid things |
| `Sky`, `Renderer.Clear(sky)` | Clears to a sky as the camera sees it: the zenith's colour overhead fading to the horizon's at eye level, the ground's below |
| `Renderer.Fog` | Distance fog: from `Start` to `End` in front of the camera things fade into its colour; clear to the same colour and the horizon melts into the sky (and far triangles need no clipping by eye) |
| `Models.FromPicture` | A sprite made solid: its opaque pixels' outline extruded, the picture on both sides and its colours along the edges, standing on its lowest row. A side view of a car becomes a blocky 3D car |
| `Renderer.DrawPoint`, `Particles` | Particles in 3D: `Particles` emits, moves (gravity, drag, an optional floor), ages and fades them, replacing the oldest when full; each is drawn as a square facing the camera, blended by its alpha, hidden by nearer things but hiding nothing |
| `RenderStats` | Triangles and pixels drawn since the last `Clear` |

```csharp
var camera = new Camera { Position = new Vector3(4, 3, 6), Target = Vector3.Zero };
var renderer = new Renderer(new FrameBuffer(320, 180)) { ViewProjection = camera.ViewProjection(320 / 180f) };
renderer.Clear(Colors.Pack(new Vector4(0.45f, 0.6f, 0.85f, 1)));
renderer.DrawMesh(MeshBuilder.Extrude(carProfile, 1.6f), Matrix4x4.CreateRotationY(angle), new Material(red, Shading.Flat));
```

A textured scene of about 300 triangles at 320×180 takes about 1 ms a frame on a desktop and about 3 ms in the
browser (15 ms when .NET is interpreted there rather than compiled ahead of time). Once warmed up, drawing makes no
garbage, so the collector never stops a frame. Each row of a triangle steps its
values by a fixed amount per pixel, and nearest texels on power-of-two textures are read with a mask.

`Jentl.Core.Raster` puts 3D on screen. An `IMeshView` draws meshes into a picture that a scene shows as a texture:
`SoftwareView` with `Jentl.Raster` on the CPU (pick a low resolution, e.g. 320×180, and draw it scaled up with point
sampling), or `GpuView` with MonoGame's `BasicEffect` on the graphics card, into its own render target with depth.
Both take the same meshes, `Material`s, `Lighting`, `Fog` and camera (`view.Look(camera)` sets the view and the
view-projection; the graphics card needs the view on its own to measure fog), so a scene can switch between them.
`RasterTextures.From` turns an image from `AssetStore.Image` (or a sprite's region of an atlas image) into a
`Jentl.Raster.Texture`. `Begin(sky)` clears to a `Sky` on either view (set the camera first). `DrawPoint` (and `view.DrawParticles(particles)`) draws particles on either view, at `Finish`
after the meshes. Call
`Begin`, `DrawMesh`, `Finish` from `Scene.DrawLayers` (the sprite batch is not begun there), and draw `Texture` in
`Draw`.

## 3D scenes: nodes, glTF models and animation

`Jentl.Scene3D` (framework-free) puts 3D content together the way engines and modelling tools do, and both mesh views
draw it.

- **Nodes.** A `Node` has a position, rotation (a quaternion) and scale relative to its parent, any number of
  `MeshPart`s (a mesh in a material), children that move with it, `Visible` and a `Tag` for the game's own object.
  `World`, `WorldPosition`, `WorldRotation` and `Forward` (+Z, as glTF models face) say where it is; `Local` gets or
  sets the three as one matrix; `Turn` rotates it about its own axis, `LookAt` faces a point in the world under any
  parent; `Find`, `SelfAndDescendants`, `WorldBounds` and `Clone` (sharing the meshes) do the rest.
- **Drawing.** `view.DrawScene(root, camera, list)` looks through the camera and draws what it sees. The
  `RenderList` behind it leaves out meshes whose boxes are outside the view, draws solid meshes first, grouped by
  material and nearest first, then see-through ones furthest first, and counts what it drew and culled.
- **Materials and lights.** `Material` also takes `Emissive` (a glow, whatever the lights), `Specular` and
  `Shininess` (a shine; on the graphics card, worked out per pixel) and `DoubleSided` (leaves, flags, sails).
  `Lighting.Fill` adds up to two more directional lights to the main one (a bounce from the ground, a rim light);
  `ParallelLight` is one. The software renderer takes all of these except the shine.
- **glTF models.** `Gltf.Load(path)` (or bytes, with a way to find the files a `.gltf` refers to) reads glTF 2.0,
  `.gltf` or `.glb`, as Blender and most tools export it: meshes (triangles, strips and fans, with normals and
  texture coordinates; flat when they have no normals), materials (base colour and texture, emission, blending or
  cut-out alpha, double sides, a shine guessed from roughness and metalness, `KHR_materials_unlit`), textures in
  PNG or JPEG (baseline or progressive) with their filtering and wrapping, the node tree (by translation, rotation and
  scale or a matrix), animations of position, rotation and scale (linear, step or cubic spline), and skins. Morph
  targets, cameras and lights are left out, each noted in `Warnings`. `model.Instantiate()` makes a copy to put in a
  scene: its `Root`, and its `Nodes` by their index in the file, all sharing the meshes.
- **Skins.** A skinned node has a `Skin` (its joint nodes and their inverse bind matrices) and parts with
  `SkinWeights` (four joints and weights a vertex). After the animation has moved the joints, `instance.UpdateSkins()`
  (or `skin.Deform(node)`) bends the mesh on the CPU into a shape made with `Mesh.Deform`, which keeps its
  `Original`: the software renderer draws it as any mesh, and the GPU view refills one buffer for all its shapes. A
  skin can be made in code too, as the Lab's swaying stalk is.
- **Animation.** An `AnimationClip` is a set of `AnimationChannel`s, each moving one node's position, rotation or
  scale through keys (`AnimationChannel.Move`, `Turn` and `Resize` make one in code). An `AnimationPlayer` plays clips on a
  list of nodes: looping or once, at a `Speed`, cross-fading from the clip before over a time (`Play(run, fade:
  TimeSpan.FromSeconds(0.3))`), so a walk turns into a run without a jump.
- **Cameras and picking.** An `OrbitCamera` circles a target (`Rotate` by a drag, `Zoom` by the wheel, `Pan`,
  `Frame` a box) and a `FollowCamera` trails a node from a place of its own relative to it, catching up smoothly; both
  set a `Camera`. `camera.RayThrough(point, size)` is the ray under the pointer, and `root.Pick(ray)` the nearest
  mesh it hits: the node, the part, the distance and the point.

```csharp
var windmill = Gltf.Load("Content/windmill.glb");
var world = new Node("world");
world.Add("field", MeshBuilder.Plane(40, 40), grass);
var mill = windmill.Instantiate("mill");
world.Add(mill.Root).Position = new Vector3(5, 0, 2);
var sails = mill.CreatePlayer();
sails.Play(windmill.Animation("turn")!);
var orbit = new OrbitCamera { Target = Vector3.Zero, Distance = 20 };

// Update:
sails.Update(elapsed);
orbit.Rotate(drag.X * -0.01f, drag.Y * 0.01f);
if (input.Tap is { } tap && world.Pick(camera.RayThrough(tap, screen)) is { } hit)
    Select(hit.Node);

// DrawLayers:
orbit.Apply(camera);
view.Look(camera);
view.Begin(sky);
view.DrawScene(world, camera, list);
view.Finish();
```

`Jentl.Assets.Jpeg.Decode` reads JPEG pictures for any other use too: baseline and progressive, grey or colour, any
chroma subsampling, restart markers. Lossless, arithmetic-coded, 12-bit and CMYK files are refused.

## Traffic: roads, junctions and vehicles

`Jentl.Traffic` (framework-free, seeded) simulates a town's traffic, for watching or for a game where the player
builds the roads. A `NetworkBuilder` takes nodes and roads, in metres with x east and y north, and builds the lanes:

```csharp
var b = new NetworkBuilder();
var lights = b.Node(0, 0);
var round = b.Node(260, 0);
b.Road(lights, round, lanes: 2, speedKmh: 50);              // two lanes each way
b.Road(round, b.Node(480, 0), 1, 70);                       // a node with one road is an edge: traffic comes and goes there
b.Road(lights, b.Node(-200, 120), 1, 50, new Vector2(-120, 30)); // bending smoothly through a point
b.Signals(lights, green: 18);                               // lights; or b.Roundabout(node), b.Priority(node, roadA, roadB)
var sim = new TrafficSimulation(b.Build(), seed: 1);
sim.SetDemand(150);                                         // vehicles an hour coming in at every edge
sim.Step(elapsedSeconds);                                   // in steps of a tenth of a second
var stats = sim.Stats();                                    // on the road, arrived, per hour, trip time, delay, stops, standing
```

- **Lanes** lie either side of each road's centre (driving on the right) and stop short of junctions. Each junction
  gets a turning lane for every way through it: left turns from the leftmost lane, right turns from the rightmost,
  straight on from any; a roundabout gets a ring going anticlockwise with an entry and an exit per road. Bends are
  taken no faster than a comfortable sideways pull allows, so turns and rings are slower.
- **Junctions**: uncontrolled ones give priority from the right, and a left turn gives way to oncoming traffic;
  `Priority` names two roads that go first; on a roundabout the ring goes first; `Signals` makes lights with a
  phase per axis (north and south green together), each green, amber, then all red; `actuated: true` (or
  `SignalPlan.Actuated`) has them follow the traffic: a phase keeps green while cars come, between `MinGreen` and
  `Green`, gives up early when its approaches are empty and another phase waits, skips phases no one waits for, and
  rests on green when nobody else is waiting. The builder finds every place two
  turning paths cross or merge (`Lane.Conflicts`) and settles which goes first.
- **Vehicles** (`VehicleKind`: car, van, truck, bus, motorbike, in a town's mix) follow the one ahead by the
  Intelligent Driver Model, each driver a little keener or calmer. At a junction they stop for red, and for amber if
  they can; they wait while anyone is on a crossing path or a priority vehicle will be there before they are through
  (working out both times from speed and acceleration); they don't go in while the way out is backed up; and when four
  wait for each other at a plain crossroads, the one waiting longest goes. Each vehicle takes the quickest route from
  its edge to another (`Destinations` limits where generated traffic goes); lanes aren't changed on the way, so it
  starts in the lane its route needs. With `AvoidJams`, routes are chosen by the travel times measured as it runs
  (`TravelTime(lane)`, which counts vehicles still stuck on a lane), so traffic goes round a jam.
- **Measurements**: `Stats()` gives the vehicles on the road, arrivals and arrivals per hour (over ten minutes), the
  average trip time, delay over empty roads, and stops, how many stand still, and how many wait to come in at the
  edges; `Flow(lane)` counts what drove along a lane, `On(lane)` lists who is on it, front first.

`Jentl.Core.Traffic.TrafficView` draws it from above: asphalt, centre and lane dashes, roundabout islands, stop lines
in the colour of their lights, and every vehicle to size, coloured by kind; `Fit` shows the whole network, and
`Centre` and `Scale` zoom and pan. The Lab's Traffic demo puts a small town on it, and games-cs's Roadworks is a game
of building one.
