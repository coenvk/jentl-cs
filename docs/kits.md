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

Online play is in the next section; the game only ever sees seats and moves. Uno
(`src/Uno`) is the example: `UnoGame` implements the rules, `RandomUnoBot` and `SmartUnoBot` the opponents, and
`UnoMatch` adds up rounds to 500 points. Texas Hold'em (`src/Poker`) is the second: `HoldemHand` is one
hand (blinds, betting rounds, side pots), `HoldemTable` the sit-and-go around it, and `PokerBot` the opponents.

## UI toolkit

`Jentl.Core.UI` holds the widgets every screen in this repo is built from, so a new game gets them for free:

| Type | What it is |
|---|---|
| `Fonts` | A TrueType font in the UI's sizes (title, heading, body, small), scaled to the screen: 1 at 1280x800, smaller on phones (by width when upright). `FontTextDrawer` hands it to kits that only know `ITextDrawer` |
| `TextExtensions` | `DrawText` with alignment and an optional shadow, and `Wrap` to break text into lines that fit |
| `Palette` | The colours widgets draw with; set them at start-up to restyle everything |
| `Menu` | A vertical list of pill-shaped items: keys, taps, mouse hover. Taller than its space, it scrolls: the selection stays in view, arrows show there is more, and it can be dragged |
| `ButtonBar` | A centred row of buttons that squeezes its gaps on narrow screens |
| `Form` with `ToggleRow`, `SliderRow`, `ButtonRow` | Settings panels: up/down move the focus, left/right change values, confirm toggles or presses; tap a row, or drag a slider |
| `OverlayPanel` | Dims the screen and draws a panel with a title, lines of text and a `Menu` or `Form`, fitting the menu to the screen |

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

`SoundPlayer` plays effects by name (recorded `.wav` files or rendered ones) and loops music: `PlayMusic` fades
the new track in while the old one fades out, and asking for the track already playing does nothing, so every
screen can simply name its music in `OnEnter`. Effects and music each have a switch and a volume; call `Update`
every frame for the fades. In a browser, music that could not start yet (pages may only make sound after the
first touch) is retried until it plays.

## Particles and animation

`Jentl.Core.Graphics` has a pooled `ParticleSystem` and ready-made `ParticleEffects`: `Confetti` (tumbling paper
flakes), `Sparkles`, `Rise` (sparks drifting up), `Hit`, `Smoke` and `Dust`. An effect is a record (count, speed,
life, size from start to end, colours, direction and spread, gravity, drag, spin), so new ones are a `with` away.
Every `SceneManager` has an `Effects` system drawn above all scenes, for effects that should show over a win panel:

```csharp
scenes.Effects.EmitArea(new Rectangle(0, -10, scenes.Viewport.Width, 8), ParticleEffects.Confetti);
scenes.Effects.Emit(cardCentre, ParticleEffects.Sparkles);
```

`Animation` is a list of frames with durations played as a loop, once, or ping-pong; `FrameAt(time)` says which
frame shows, so many sprites can share one animation. `Animator` keeps the clock for a sprite that switches between
animations, and `SpriteSheet` cuts a texture into numbered frames.

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

Screens made for the mouse or touch (dragging cards, tapping the map) work with `PadCursor`: the right stick moves an
on-screen pointer (slowly at a slight tilt, for aiming) and the right trigger or a stick click presses it. While it is
active it *is* the pointer, so taps, drags, hover and gestures need no extra code; it wakes up where the mouse was and
steps aside the moment the mouse or a finger moves. `JentlGame.PadCursor` draws it above everything. Override
`ReadGamePad` to feed scripted pad input.

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
```

## Geometry: shapes, tests and meshes

`Jentl.Geometry` is the maths a renderer (or a game) needs on top of `System.Numerics`, which already has vectors,
matrices, quaternions and `Plane`. Right-handed, y up; a triangle's front is the side its corners run
counter-clockwise; `System.Numerics` projection conventions (row vectors, depth 0 to 1).

| Part | What it does |
|---|---|
| `Ray`, `Segment2`, `Segment3`, `Planes` | A ray's points; where segments cross; closest point on a segment; planes through a point or three points, signed distance, where a segment or ray meets one |
| `Triangle` | Normal, area, centroid, barycentric weights, containment, ray hits from either side (Möller–Trumbore) |
| `Polygon2` | Signed area and winding, point-in-polygon (concave too), ear-clipping triangulation that always gives counter-clockwise triangles; `Outline` traces a picture's solid pixels into one simple polygon (the biggest piece, holes filled, corners only) |
| `Box2`, `Box3` | Axis-aligned boxes: contains, overlaps, union, corners, transformed bounds, ray entry (slab test) |
| `Frustum` | The six planes of a view-projection matrix; whether a point or box is in view |
| `Clipping.ClipPolygon` | Cuts a polygon of any vertex type by a plane (Sutherland–Hodgman), interpolating the new vertices |
| `Edge` | The edge function rasterizers walk (value, step per pixel) and a top-left fill rule so shared edges are drawn once |
| `Mesh`, `MeshBuilder` | Triangle meshes with normals (worked out if not given) and texture coordinates; boxes, planes, spheres, cylinders, cones and extruded 2D outlines (their caps mapped across the outline or a picture you give); `Transform` moves a copy |

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
| `Renderer.Fog` | Distance fog: from `Start` to `End` in front of the camera things fade into its colour; clear to the same colour and the horizon melts into the sky (and far triangles need no clipping by eye) |
| `Models.FromPicture` | A sprite made solid: its opaque pixels' outline extruded, the picture on both sides and its colours along the edges, standing on its lowest row. A side view of a car becomes a blocky 3D car |
| `RenderStats` | Triangles and pixels drawn since the last `Clear` |

```csharp
var camera = new Camera { Position = new Vector3(4, 3, 6), Target = Vector3.Zero };
var renderer = new Renderer(new FrameBuffer(320, 180)) { ViewProjection = camera.ViewProjection(320 / 180f) };
renderer.Clear(Colors.Pack(new Vector4(0.45f, 0.6f, 0.85f, 1)));
renderer.DrawMesh(MeshBuilder.Extrude(carProfile, 1.6f), Matrix4x4.CreateRotationY(angle), new Material(red, Shading.Flat));
```

A textured scene of about 300 triangles at 320×180 takes about 1 ms a frame on a desktop and about 3 ms in the
browser (15 ms when .NET is interpreted there rather than compiled ahead of time). Each row of a triangle steps its
values by a fixed amount per pixel, and nearest texels on power-of-two textures are read with a mask.

`Jentl.Core.Raster` puts 3D on screen. An `IMeshView` draws meshes into a picture that a scene shows as a texture:
`SoftwareView` with `Jentl.Raster` on the CPU (pick a low resolution, e.g. 320×180, and draw it scaled up with point
sampling), or `GpuView` with MonoGame's `BasicEffect` on the graphics card, into its own render target with depth.
Both take the same meshes, `Material`s, `Lighting`, `Fog` and camera (`view.Look(camera)` sets the view and the
view-projection; the graphics card needs the view on its own to measure fog), so a scene can switch between them.
`RasterTextures.From` turns an image from `AssetStore.Image` (or a sprite's region of an atlas image) into a
`Jentl.Raster.Texture`. Call
`Begin`, `DrawMesh`, `Finish` from `Scene.DrawLayers` (the sprite batch is not begun there), and draw `Texture` in
`Draw`.
