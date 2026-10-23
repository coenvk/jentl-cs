namespace Jentl.Puzzle.Tests;

/// <summary>A minimal push puzzle: the player walks, pushes one box at a time, and wins with every box on a target.</summary>
internal sealed class PushRules : IPuzzleRules<GridDirection>
{
    public static readonly TileKind Floor = new("floor");
    public static readonly TileKind Wall = new("wall", IsSolid: true);
    public static readonly TileKind Target = new("target");
    public static readonly PieceKind Player = new("player");
    public static readonly PieceKind Box = new("box");

    public static Legend Legend { get; } = new Legend(Floor)
        .Add('#', Wall)
        .Add('.', Target)
        .Add('@', Floor, Player)
        .Add('$', Floor, Box)
        .Add('*', Target, Box)
        .Add('+', Target, Player)
        .Add('-', Floor);

    public bool Play(TurnBuilder turn, GridDirection input)
    {
        var board = turn.Board;
        var player = board.PiecesOf(Player).Single();
        var next = player.Position.Step(input);
        if (board.PieceAt(next) is { } box)
        {
            if (!board.IsFree(next.Step(input)))
            {
                return false;
            }

            turn.Move(box, next.Step(input));
            turn.Tag("push");
        }

        if (!board.IsFree(next))
        {
            return false;
        }

        turn.Move(player, next);
        return true;
    }

    public bool IsSolved(Board board) => board.CellsOf(Target).All(c => board.PieceAt(c)?.Kind == Box);
}

/// <summary>Lights Out: tapping a cell toggles it and its four neighbours; solved when every light is off.</summary>
internal sealed class LightsOutRules : IPuzzleRules<Cell>
{
    public static readonly TileKind On = new("on");
    public static readonly TileKind Off = new("off");

    public static Legend Legend { get; } = new Legend(Off, '.').Add('O', On);

    public bool Play(TurnBuilder turn, Cell input)
    {
        if (!turn.Board.Contains(input))
        {
            return false;
        }

        foreach (var cell in (Cell[])[input, .. GridDirections.All.Select(input.Step)])
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
