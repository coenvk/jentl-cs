using System.Text;

namespace Jentl.Puzzle;

/// <summary>
/// Which character stands for which tile, optionally with a piece on it. The first symbol added for
/// a combination is the one written back; later ones are accepted when reading only (aliases).
/// </summary>
public sealed class Legend
{
    private readonly Dictionary<char, (TileKind Tile, PieceKind? Piece)> _read = [];
    private readonly Dictionary<(TileKind, PieceKind?), char> _write = [];

    /// <param name="padding">Tile for cells past the end of a short row; also what blank space means.</param>
    public Legend(TileKind padding, char paddingSymbol = ' ')
    {
        ArgumentNullException.ThrowIfNull(padding);
        Padding = padding;
        Add(paddingSymbol, padding);
    }

    public TileKind Padding { get; }

    public Legend Add(char symbol, TileKind tile, PieceKind? piece = null)
    {
        ArgumentNullException.ThrowIfNull(tile);
        if (!_read.TryAdd(symbol, (tile, piece)))
        {
            throw new ArgumentException($"Symbol '{symbol}' is already defined.", nameof(symbol));
        }

        _write.TryAdd((tile, piece), symbol);
        return this;
    }

    public bool Defines(char symbol) => _read.ContainsKey(symbol);

    public (TileKind Tile, PieceKind? Piece) Read(char symbol) =>
        _read.TryGetValue(symbol, out var entry) ? entry : throw new FormatException($"Unknown symbol '{symbol}'.");

    public char Symbol(TileKind tile, PieceKind? piece) =>
        _write.TryGetValue((tile, piece), out var symbol)
            ? symbol
            : throw new InvalidOperationException($"No symbol for {piece?.Name ?? "nothing"} on {tile.Name}.");
}

/// <summary>One level read from a file: its board, title and any <c>Key: value</c> metadata.</summary>
public sealed record LevelData(string? Title, IReadOnlyDictionary<string, string> Metadata, Board Board);

/// <summary>
/// Reads and writes text level files. A file holds any number of maps: runs of lines made only of
/// legend symbols. Between maps, <c>; comment</c> lines and plain text give the next map its title,
/// and <c>Key: value</c> lines after a map are its metadata (<c>Title:</c> sets the title).
/// This is the layout of Sokoban's XSB packs, generalised to any legend.
/// </summary>
public static class LevelFile
{
    public static IReadOnlyList<LevelData> ReadPack(string text, Legend legend)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(legend);
        var lines = text.ReplaceLineEndings("\n").Split('\n');
        var levels = new List<LevelData>();
        string? pendingTitle = null;

        for (var i = 0; i < lines.Length;)
        {
            if (!IsMapLine(lines[i], legend))
            {
                var line = lines[i].Trim();
                if (line.StartsWith(';'))
                {
                    pendingTitle = line.TrimStart(';').Trim();
                }
                else if (line.Length > 0 && !IsMetadataLine(line, out _, out _))
                {
                    pendingTitle = line;
                }

                i++;
                continue;
            }

            var start = i;
            while (i < lines.Length && IsMapLine(lines[i], legend))
            {
                i++;
            }

            var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var j = i; j < lines.Length && !IsMapLine(lines[j], legend); j++)
            {
                if (IsMetadataLine(lines[j].Trim(), out var key, out var value))
                {
                    metadata[key] = value;
                }
            }

            var title = metadata.TryGetValue("Title", out var named) ? named : pendingTitle;
            pendingTitle = null;
            try
            {
                levels.Add(new LevelData(title, metadata, ReadMap(lines.AsSpan(start, i - start), legend)));
            }
            catch (FormatException e)
            {
                throw new FormatException($"Level at line {start + 1}: {e.Message}", e);
            }
        }

        return levels;
    }

    /// <summary>Writes a board as map lines, trailing padding trimmed from each row.</summary>
    public static string Write(Board board, Legend legend)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(legend);
        var sb = new StringBuilder();
        var row = new StringBuilder(board.Width);
        for (var y = 0; y < board.Height; y++)
        {
            row.Clear();
            for (var x = 0; x < board.Width; x++)
            {
                var cell = new Cell(x, y);
                row.Append(legend.Symbol(board.TileAt(cell), board.PieceAt(cell)?.Kind));
            }

            sb.Append(row.ToString().TrimEnd()).Append('\n');
        }

        return sb.ToString();
    }

    /// <summary>A map line: only legend symbols, and at least one that is not blank.</summary>
    private static bool IsMapLine(string line, Legend legend)
    {
        var trimmed = line.TrimEnd();
        return trimmed.Length > 0 && trimmed.Any(c => c != ' ') && trimmed.All(legend.Defines);
    }

    private static bool IsMetadataLine(string line, out string key, out string value)
    {
        var colon = line.IndexOf(':', StringComparison.Ordinal);
        if (colon > 0 && line[..colon].All(char.IsLetter))
        {
            key = line[..colon];
            value = line[(colon + 1)..].Trim();
            return true;
        }

        key = value = "";
        return false;
    }

    private static Board ReadMap(ReadOnlySpan<string> rows, Legend legend)
    {
        var width = 0;
        foreach (var row in rows)
        {
            width = Math.Max(width, row.TrimEnd().Length);
        }

        var board = new Board(width, rows.Length, legend.Padding);
        for (var y = 0; y < rows.Length; y++)
        {
            var row = rows[y].TrimEnd();
            for (var x = 0; x < row.Length; x++)
            {
                var (tile, piece) = legend.Read(row[x]);
                var cell = new Cell(x, y);
                board.SetTile(cell, tile);
                if (piece is not null)
                {
                    board.Place(piece, cell);
                }
            }
        }

        return board;
    }
}
