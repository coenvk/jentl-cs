namespace Jentl.Puzzle.Tests;

public class PuzzleGameTests
{
    private static PuzzleGame<GridDirection> PushGame(string map) =>
        new(LevelFile.ReadPack(map, PushRules.Legend).Single().Board, new PushRules());

    [Fact]
    public void Play_RecordsMovesAndTags()
    {
        var game = PushGame("######\n#@$.-#\n######");

        var turn = game.Play(GridDirection.Right);

        turn.Should().NotBeNull();
        turn!.Has("push").Should().BeTrue();
        turn.Moves.Select(m => (m.Piece.Kind, m.From, m.To)).Should().Equal(
            (PushRules.Box, new Cell(2, 1), new Cell(3, 1)),
            (PushRules.Player, new Cell(1, 1), new Cell(2, 1)));
        game.IsSolved.Should().BeTrue();
    }

    [Fact]
    public void Play_RejectedInput_LeavesBoardAndHistoryUntouched()
    {
        var game = PushGame("######\n#@$$.#\n######");
        var before = LevelFile.Write(game.Board, PushRules.Legend);

        game.Play(GridDirection.Right).Should().BeNull("two boxes in a row cannot be pushed");

        LevelFile.Write(game.Board, PushRules.Legend).Should().Be(before);
        game.History.Should().BeEmpty();
    }

    [Fact]
    public void Play_RulesThatRejectAfterChanging_AreRolledBack()
    {
        var board = new Board(3, 1, PushRules.Floor);
        var piece = board.Place(PushRules.Player, new Cell(0, 0));
        var game = new PuzzleGame<int>(board, new ChangeThenRejectRules(piece));

        game.Play(0).Should().BeNull();

        piece.Position.Should().Be(new Cell(0, 0));
    }

    [Fact]
    public void Play_RulesThatThrow_AreRolledBack()
    {
        var board = new Board(3, 1, PushRules.Floor);
        var piece = board.Place(PushRules.Player, new Cell(0, 0));
        var game = new PuzzleGame<int>(board, new ChangeThenThrowRules(piece));

        var act = () => game.Play(0);

        act.Should().Throw<InvalidOperationException>();
        piece.Position.Should().Be(new Cell(0, 0));
    }

    [Fact]
    public void UndoAndRedo_RestoreExactState_AndKeepPieceIdentity()
    {
        var game = PushGame("#######\n#@$ ..#\n#  $  #\n#######");
        var box = game.Board.PieceAt(new Cell(2, 1))!;
        var start = LevelFile.Write(game.Board, PushRules.Legend);
        game.Play(GridDirection.Right);
        game.Play(GridDirection.Right);
        var after = LevelFile.Write(game.Board, PushRules.Legend);

        game.Undo();
        game.Undo();
        LevelFile.Write(game.Board, PushRules.Legend).Should().Be(start);
        game.Undo().Should().BeNull();

        game.Redo();
        game.Redo();
        LevelFile.Write(game.Board, PushRules.Legend).Should().Be(after);
        game.Board.PieceAt(new Cell(4, 1)).Should().BeSameAs(box);
        game.CountTagged("push").Should().Be(2);
    }

    [Fact]
    public void Play_ClearsRedo()
    {
        var game = PushGame("#######\n#@$ ..#\n#  $  #\n#######");
        game.Play(GridDirection.Down);
        game.Undo();

        game.Play(GridDirection.Right);

        game.CanRedo.Should().BeFalse();
    }

    [Fact]
    public void Restart_UndoesEverything()
    {
        var game = PushGame("#######\n#@$ ..#\n#  $  #\n#######");
        var start = LevelFile.Write(game.Board, PushRules.Legend);
        game.Play(GridDirection.Right);
        game.Play(GridDirection.Down);

        game.Restart();

        LevelFile.Write(game.Board, PushRules.Legend).Should().Be(start);
        game.CanUndo.Should().BeFalse();
        game.CanRedo.Should().BeFalse();
    }

    [Fact]
    public void TileChanges_UndoToo_LightsOut()
    {
        // A plus of lights: one tap in the middle turns all five off.
        var board = LevelFile.ReadPack(".O.\nOOO\n.O.", LightsOutRules.Legend).Single().Board;
        var game = new PuzzleGame<Cell>(board, new LightsOutRules());

        game.Play(new Cell(1, 1))!.Changes.Should().HaveCount(5).And.AllBeOfType<TileChanged>();
        game.IsSolved.Should().BeTrue();

        game.Undo();
        LevelFile.Write(board, LightsOutRules.Legend).Should().Be(".O.\nOOO\n.O.\n");
        game.IsSolved.Should().BeFalse();

        game.Play(new Cell(0, 0));
        LevelFile.Write(board, LightsOutRules.Legend).Should().Be("O..\n.OO\n.O.\n", "the corner has only two neighbours");
    }

    [Fact]
    public void AddedAndRemovedPieces_UndoAndRedo()
    {
        var board = new Board(2, 1, PushRules.Floor);
        var game = new PuzzleGame<int>(board, new SpawnRules());

        game.Play(0);
        game.Play(1);
        board.Pieces.Should().ContainSingle(p => p.Position == new Cell(1, 0));

        game.Undo();
        board.Pieces.Should().ContainSingle(p => p.Position == new Cell(0, 0));
        game.Undo();
        board.Pieces.Should().BeEmpty();
        game.Redo();
        game.Redo();
        board.Pieces.Should().ContainSingle(p => p.Position == new Cell(1, 0));
    }

    private sealed class ChangeThenRejectRules(Piece piece) : IPuzzleRules<int>
    {
        public bool Play(TurnBuilder turn, int input)
        {
            turn.Move(piece, new Cell(1, 0));
            return false;
        }

        public bool IsSolved(Board board) => false;
    }

    private sealed class ChangeThenThrowRules(Piece piece) : IPuzzleRules<int>
    {
        public bool Play(TurnBuilder turn, int input)
        {
            turn.Move(piece, new Cell(1, 0));
            throw new InvalidOperationException("Rule bug.");
        }

        public bool IsSolved(Board board) => false;
    }

    /// <summary>Input 0 adds a box at (0,0); input 1 replaces it with one at (1,0).</summary>
    private sealed class SpawnRules : IPuzzleRules<int>
    {
        public bool Play(TurnBuilder turn, int input)
        {
            if (input == 0)
            {
                turn.Add(PushRules.Box, new Cell(0, 0));
            }
            else
            {
                turn.Remove(turn.Board.Pieces.Single());
                turn.Add(PushRules.Box, new Cell(1, 0));
            }

            return true;
        }

        public bool IsSolved(Board board) => false;
    }
}
