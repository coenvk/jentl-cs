using System.Globalization;
using Jentl.Collect;
using Jentl.Core.Graphics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Jentl.Core.UI;

/// <summary>A line of a leaderboard as shown: its place (from 1), the name, the score as text, and whether it is the player's.</summary>
public readonly record struct LeaderboardRow(string Place, string Name, string Score, bool Highlight)
{
    /// <summary>The "…" between the top and a player further down.</summary>
    public static LeaderboardRow Gap { get; } = new("", "…", "", false);
}

/// <summary>
/// Draws a leaderboard as a table: place, name and score per line, the player's own line picked out in the accent
/// colour, and, when the player is further down than the top shown, a gap and their line below it.
/// </summary>
public static class LeaderboardTable
{
    private const int RowGap = 6;

    /// <summary>
    /// The rows for <paramref name="top"/>: <paramref name="rank"/> is the player's place (0 is the top) and
    /// <paramref name="mine"/> their line, if they are on the board; <paramref name="format"/> writes a score.
    /// </summary>
    public static IReadOnlyList<LeaderboardRow> Rows(IReadOnlyList<LeaderboardEntry> top, int? rank, LeaderboardEntry? mine, Func<long, string> format)
    {
        ArgumentNullException.ThrowIfNull(top);
        ArgumentNullException.ThrowIfNull(format);
        var rows = top.Select((e, i) => new LeaderboardRow(Place(i), e.Name, format(e.Score), i == rank)).ToList();
        if (rank is { } r && r >= top.Count && mine is not null)
        {
            rows.Add(LeaderboardRow.Gap);
            rows.Add(new LeaderboardRow(Place(r), mine.Name, format(mine.Score), Highlight: true));
        }

        return rows;
    }

    /// <summary>How tall <paramref name="rows"/> rows are.</summary>
    public static int Height(Fonts fonts, int rows)
    {
        ArgumentNullException.ThrowIfNull(fonts);
        return Math.Max(1, rows) * RowHeight(fonts);
    }

    /// <summary>Draws <paramref name="rows"/> across <paramref name="area"/> from its top; <paramref name="empty"/> if there are none.</summary>
    public static void Draw(SpriteBatch batch, Fonts fonts, Rectangle area, IReadOnlyList<LeaderboardRow> rows, string empty)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(fonts);
        ArgumentNullException.ThrowIfNull(rows);
        var font = fonts.Body;
        var height = RowHeight(fonts);
        if (rows.Count == 0)
        {
            batch.DrawText(font, empty, new Vector2(area.Center.X, area.Y), Palette.Muted, Align.Center, shadow: false);
            return;
        }

        var pad = fonts.Px(16);
        var placeWidth = font.MeasureString("000").X;
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var line = new Rectangle(area.X, area.Y + (i * height), area.Width, height - fonts.Px(RowGap));
            if (row == LeaderboardRow.Gap)
            {
                batch.DrawText(font, row.Name, new Vector2(line.Center.X, line.Y), Palette.Muted, Align.Center, shadow: false);
                continue;
            }

            batch.FillRectangle(line, row.Highlight ? Palette.Accent : i % 2 == 0 ? Palette.Panel : Palette.PanelLight);
            var (ink, muted) = row.Highlight ? (Palette.Ink, Palette.Ink) : (Palette.Text, Palette.Muted);
            var y = line.Y + ((line.Height - font.LineHeight) / 2);
            batch.DrawText(font, row.Place, new Vector2(line.X + pad + placeWidth, y), muted, Align.Right, shadow: false);
            batch.DrawText(font, row.Name, new Vector2(line.X + (2 * pad) + placeWidth, y), ink, shadow: false);
            batch.DrawText(font, row.Score, new Vector2(line.Right - pad, y), ink, Align.Right, shadow: false);
        }
    }

    private static int RowHeight(Fonts fonts) => (int)fonts.Body.LineHeight + fonts.Px(14) + fonts.Px(RowGap);

    private static string Place(int index) => (index + 1).ToString(CultureInfo.InvariantCulture);
}
