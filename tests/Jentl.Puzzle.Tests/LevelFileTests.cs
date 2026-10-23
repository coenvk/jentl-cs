namespace Jentl.Puzzle.Tests;

public class LevelFileTests
{
    [Fact]
    public void ReadPack_ReadsMapsTitlesAndMetadata()
    {
        var levels = LevelFile.ReadPack("""
            ; First

            #####
            #@$.#
            #####

            #####
            #.$@#
            #####
            Title: Second
            Author: Someone

            #####
            #@*.$#
            ######
            """, PushRules.Legend);

        levels.Select(l => l.Title).Should().Equal("First", "Second", null);
        levels[1].Metadata["Author"].Should().Be("Someone");
        levels[2].Board.Width.Should().Be(6, "short rows are padded to the widest");
    }

    [Fact]
    public void Write_RoundTripsWithPreferredSymbols()
    {
        var level = LevelFile.ReadPack("#####\n#@-$.#\n######", PushRules.Legend).Single();

        LevelFile.Write(level.Board, PushRules.Legend).Should().Be("#####\n#@ $.#\n######\n", "'-' is an alias for floor; ' ' is preferred");
    }

    [Fact]
    public void ReadPack_PlacesPiecesOnTiles()
    {
        var board = LevelFile.ReadPack("####\n#+*#\n####", PushRules.Legend).Single().Board;

        board.TileAt(new Cell(1, 1)).Should().Be(PushRules.Target);
        board.PieceAt(new Cell(1, 1))!.Kind.Should().Be(PushRules.Player);
        board.PieceAt(new Cell(2, 1))!.Kind.Should().Be(PushRules.Box);
    }

    [Fact]
    public void Legend_RejectsDuplicateSymbols()
    {
        var act = () => new Legend(PushRules.Floor).Add('#', PushRules.Wall).Add('#', PushRules.Target);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Write_WithoutSymbolForACombination_Throws()
    {
        var board = new Board(1, 1, PushRules.Wall);
        board.Place(PushRules.Box, new Cell(0, 0));

        var act = () => LevelFile.Write(board, PushRules.Legend);

        act.Should().Throw<InvalidOperationException>().WithMessage("*box*wall*");
    }

    [Fact]
    public void LinesWithUnknownSymbols_AreNotMaps() =>
        LevelFile.ReadPack("Hello world\n#####\n#@$.#\n#####", PushRules.Legend).Single().Title.Should().Be("Hello world");
}
