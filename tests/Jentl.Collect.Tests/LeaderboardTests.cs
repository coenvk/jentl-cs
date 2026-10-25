namespace Jentl.Collect.Tests;

public class LeaderboardTests
{
    private static readonly DateTimeOffset Day = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    private static LeaderboardEntry Entry(string name, long score, int minute = 0) => new(name, score, Day.AddMinutes(minute));

    [Fact]
    public void Submit_KeepsTheBestFirst_AndSaysWhereItWent()
    {
        var board = new Leaderboard(ScoreOrder.HigherIsBetter, capacity: 3);

        board.Submit(Entry("A", 100)).Should().Be(0);
        board.Submit(Entry("B", 300)).Should().Be(0);
        board.Submit(Entry("C", 200)).Should().Be(1);

        board.Entries.Select(e => e.Name).Should().Equal("B", "C", "A");
    }

    [Fact]
    public void Submit_WhenFull_DropsTheWorst_AndTurnsAwayWhatIsNoBetter()
    {
        var board = new Leaderboard(ScoreOrder.HigherIsBetter, capacity: 2);
        board.Submit(Entry("A", 100));
        board.Submit(Entry("B", 200));

        board.Qualifies(100).Should().BeFalse("a tie with the last place does not push it off");
        board.Submit(Entry("C", 100)).Should().BeNull();
        board.Submit(Entry("D", 150)).Should().Be(1);

        board.Entries.Select(e => e.Name).Should().Equal("B", "D");
    }

    [Fact]
    public void Ties_GoToWhoeverGotThereFirst()
    {
        var board = new Leaderboard(ScoreOrder.HigherIsBetter);
        board.Submit(Entry("First", 100));

        board.Submit(Entry("Second", 100, minute: 5)).Should().Be(1);
    }

    [Fact]
    public void LowerIsBetter_ForTimesAndMoves()
    {
        var board = new Leaderboard(ScoreOrder.LowerIsBetter);
        board.Submit(Entry("Slow", 90));
        board.Submit(Entry("Quick", 40)).Should().Be(0);

        board.Entries[0].Name.Should().Be("Quick");
        board.Qualifies(1000).Should().BeTrue("there is room");
    }

    [Fact]
    public void BestPerName_KeepsOnlyEachNamesBest()
    {
        var board = new Leaderboard(ScoreOrder.HigherIsBetter, bestPerName: true);
        board.Submit(Entry("A", 100));

        board.Submit(Entry("A", 50)).Should().BeNull("worse than their own best");
        board.Submit(Entry("A", 200)).Should().Be(0);

        board.Entries.Should().ContainSingle().Which.Score.Should().Be(200);
    }

    [Fact]
    public void Json_RoundTripsTheEntries()
    {
        var board = new Leaderboard(ScoreOrder.LowerIsBetter, capacity: 5);
        board.Submit(Entry("A", 12));
        board.Submit(Entry("B", 9, minute: 1));

        var again = Leaderboard.FromJson(board.ToJson(), ScoreOrder.LowerIsBetter, capacity: 5);

        again.Entries.Should().Equal(board.Entries);
    }

    [Fact]
    public void FromJson_PutsUnsortedOrTooManyEntriesInOrder()
    {
        var json = """[{"name":"A","score":1,"at":"2026-10-25T12:00:00+00:00"},{"name":"B","score":3,"at":"2026-10-25T12:00:00+00:00"},{"name":"C","score":2,"at":"2026-10-25T12:00:00+00:00"}]""";

        var board = Leaderboard.FromJson(json, ScoreOrder.HigherIsBetter, capacity: 2);

        board.Entries.Select(e => e.Name).Should().Equal("B", "C");
    }

    [Fact]
    public void FromJson_OfSomethingElse_Throws()
    {
        var read = () => Leaderboard.FromJson("{not json", ScoreOrder.HigherIsBetter);
        read.Should().Throw<InvalidDataException>();
    }
}

public class PlayerNameTests
{
    [Fact]
    public void Make_GivesAnAdjectiveAnAnimalAndANumber_ThatPassesTheCheck()
    {
        var random = new Random(7);
        for (var i = 0; i < 200; i++)
        {
            var name = PlayerNames.Make(random);
            name.Split(' ').Should().HaveCount(3);
            PlayerNames.IsValid(name).Should().BeTrue(name);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("Swift Otter")]
    [InlineData("Swift Otter 100")]
    [InlineData("Rude Word 12")]
    [InlineData("swift otter 12")]
    public void IsValid_TurnsAwayAnythingTheMakerWouldNotMake(string name) => PlayerNames.IsValid(name).Should().BeFalse();
}
