# Jentl game kits

Jentl has two kits for building new games quickly. Each has a framework-free core (rules, state, undo, files; plain
C#, easy to unit test) and a thin screen layer in `Jentl.Core` (drawing, layout, input, animation) that runs on
desktop and in the browser.

| Kit | Core | Screen layer | Games built on it |
|---|---|---|---|
| Puzzle | `Jentl.Puzzle` | `Jentl.Core.Puzzle.GridView` | Sokoban (`SokobanRules`), and Lights Out and a push puzzle in the tests |
| Cards | `Jentl.Cards` | `Jentl.Core.Cards` (`CardArt`, `CardRenderer`, `TableView`) | Klondike (`src/Klondike`), FreeCell (`src/FreeCell`), Pyramid (`src/Pyramid`) and Spider (`src/Spider`) |

Both cores work the same way: **rules change the state through a turn builder, and the engine records every change**.
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
