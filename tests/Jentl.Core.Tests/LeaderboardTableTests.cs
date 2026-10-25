using Jentl.Collect;
using Jentl.Core.UI;

namespace Jentl.Core.Tests;

public class LeaderboardTableTests
{
    private static readonly DateTimeOffset Day = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    private static List<LeaderboardEntry> Board(int count) =>
        [.. Enumerable.Range(0, count).Select(i => new LeaderboardEntry($"Player {i}", 1000 - i, Day))];

    [Fact]
    public void Rows_NumberTheTop_AndMarkThePlayersLine()
    {
        var rows = LeaderboardTable.Rows(Board(3), rank: 1, mine: null, score => $"{score} pts");

        rows.Select(r => r.Place).Should().Equal("1", "2", "3");
        rows.Select(r => r.Score).Should().Equal("1000 pts", "999 pts", "998 pts");
        rows.Select(r => r.Highlight).Should().Equal(false, true, false);
    }

    [Fact]
    public void Rows_AddThePlayerBelowTheTop_WhenTheyAreFurtherDown()
    {
        var mine = new LeaderboardEntry("Swift Otter 27", 12, Day);

        var rows = LeaderboardTable.Rows(Board(10), rank: 41, mine, score => score.ToString(System.Globalization.CultureInfo.InvariantCulture));

        rows.Should().HaveCount(12);
        rows[10].Should().Be(LeaderboardRow.Gap);
        rows[11].Should().Be(new LeaderboardRow("42", "Swift Otter 27", "12", Highlight: true));
    }
}

public class ButtonRowTests
{
    [Fact]
    public void AButtonsValue_CanFollowWhatItShows()
    {
        var name = "Swift Otter 27";
        var row = new ButtonRow("Name", () => name = "Brave Fox 11", () => name);

        row.Value.Should().Be("Swift Otter 27");
        row.Activate();
        row.Value.Should().Be("Brave Fox 11");
        new ButtonRow("Back", () => { }).Value.Should().BeEmpty();
    }
}
