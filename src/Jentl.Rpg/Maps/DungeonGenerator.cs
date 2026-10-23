using Jentl.Puzzle;

namespace Jentl.Rpg.Maps;

/// <summary>A rectangular room in cells.</summary>
public readonly record struct Room(int X, int Y, int Width, int Height)
{
    public Cell Center => new(X + Width / 2, Y + Height / 2);

    public bool Contains(Cell cell) => cell.X >= X && cell.Y >= Y && cell.X < X + Width && cell.Y < Y + Height;

    public IEnumerable<Cell> Cells
    {
        get
        {
            var (x, y, width) = (X, Y, Width);
            return Enumerable.Range(0, Width * Height).Select(i => new Cell(x + i % width, y + i / width));
        }
    }

    /// <summary>Whether the two rooms overlap or come closer than <paramref name="margin"/> cells.</summary>
    public bool Touches(Room other, int margin) =>
        X - margin < other.X + other.Width && other.X - margin < X + Width
        && Y - margin < other.Y + other.Height && other.Y - margin < Y + Height;
}

/// <summary>A generated floor plan: which cells are floor, the rooms, and where to enter and leave.</summary>
public sealed class DungeonLayout
{
    private readonly bool[] _floor;

    internal DungeonLayout(int width, int height, bool[] floor, IReadOnlyList<Room> rooms)
    {
        Width = width;
        Height = height;
        _floor = floor;
        Rooms = rooms;
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>The rooms in the order they are connected; the first holds <see cref="Entrance"/>, the last <see cref="Exit"/>.</summary>
    public IReadOnlyList<Room> Rooms { get; }

    public Cell Entrance => Rooms[0].Center;

    public Cell Exit => Rooms[^1].Center;

    public bool IsFloor(Cell cell) => cell.X >= 0 && cell.Y >= 0 && cell.X < Width && cell.Y < Height && _floor[cell.Y * Width + cell.X];

    public IEnumerable<Cell> FloorCells => Enumerable.Range(0, Width * Height).Where(i => _floor[i]).Select(i => new Cell(i % Width, i / Width));

    /// <summary>Wall cells that touch a floor cell (including diagonally): the walls worth drawing.</summary>
    public IEnumerable<Cell> EdgeWalls => Enumerable.Range(0, Width * Height)
        .Select(i => new Cell(i % Width, i / Width))
        .Where(c => !IsFloor(c) && Neighbours8(c).Any(IsFloor));

    private static IEnumerable<Cell> Neighbours8(Cell cell)
    {
        for (var dy = -1; dy <= 1; dy++)
        {
            for (var dx = -1; dx <= 1; dx++)
            {
                if (dx != 0 || dy != 0)
                {
                    yield return new Cell(cell.X + dx, cell.Y + dy);
                }
            }
        }
    }
}

/// <summary>
/// Rooms-and-corridors dungeons: rooms placed at random without overlapping, each joined to the previous
/// one by an L-shaped corridor, so every room is reachable. The same seed gives the same dungeon.
/// </summary>
public sealed class DungeonGenerator
{
    public int Width { get; init; } = 48;

    public int Height { get; init; } = 32;

    public int MaxRooms { get; init; } = 10;

    public int MinRoomSize { get; init; } = 4;

    public int MaxRoomSize { get; init; } = 9;

    public DungeonLayout Generate(int seed)
    {
        if (MinRoomSize < 1 || MaxRoomSize < MinRoomSize || MaxRoomSize + 2 > Math.Min(Width, Height))
        {
            throw new InvalidOperationException("Room sizes must be positive and fit inside the dungeon.");
        }

        var random = new Random(seed);
        var floor = new bool[Width * Height];
        var rooms = new List<Room>();
#pragma warning disable CA5394 // Level generation, not security relevant.
        for (var attempt = 0; attempt < MaxRooms * 20 && rooms.Count < MaxRooms; attempt++)
        {
            var width = random.Next(MinRoomSize, MaxRoomSize + 1);
            var height = random.Next(MinRoomSize, Math.Min(MaxRoomSize, Height - 2) + 1);
            var room = new Room(random.Next(1, Width - width - 1), random.Next(1, Height - height - 1), width, height);
            if (!rooms.Any(r => r.Touches(room, margin: 1)))
            {
                rooms.Add(room);
            }
        }

        // Joining rooms in left-to-right order keeps corridors short and rarely crossing.
        rooms.Sort((a, b) => a.Center.X.CompareTo(b.Center.X));
        foreach (var room in rooms)
        {
            foreach (var cell in room.Cells)
            {
                floor[cell.Y * Width + cell.X] = true;
            }
        }

        for (var i = 1; i < rooms.Count; i++)
        {
            Corridor(floor, rooms[i - 1].Center, rooms[i].Center, horizontalFirst: random.Next(2) == 0);
        }
#pragma warning restore CA5394

        return new DungeonLayout(Width, Height, floor, rooms);
    }

    private void Corridor(bool[] floor, Cell from, Cell to, bool horizontalFirst)
    {
        var corner = horizontalFirst ? new Cell(to.X, from.Y) : new Cell(from.X, to.Y);
        Line(floor, from, corner);
        Line(floor, corner, to);
    }

    private void Line(bool[] floor, Cell from, Cell to)
    {
        var dx = Math.Sign(to.X - from.X);
        var dy = Math.Sign(to.Y - from.Y);
        for (var cell = from; ; cell = new Cell(cell.X + dx, cell.Y + dy))
        {
            floor[cell.Y * Width + cell.X] = true;
            if (cell == to)
            {
                break;
            }
        }
    }
}
