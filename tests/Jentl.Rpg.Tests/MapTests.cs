using System.IO.Compression;
using Jentl.Puzzle;
using Jentl.Rpg.Maps;

namespace Jentl.Rpg.Tests;

public class MapTests
{
    private static readonly int[] Gids = [1, 2, 2, 1];

    private const string Tsx = """
        <?xml version="1.0" encoding="UTF-8"?>
        <tileset version="1.10" name="terrain" tilewidth="16" tileheight="16" tilecount="4" columns="2">
         <image source="../art/terrain.png" width="32" height="32"/>
         <tile id="1">
          <properties>
           <property name="name" value="wall"/>
           <property name="solid" type="bool" value="true"/>
           <property name="opaque" type="bool" value="true"/>
          </properties>
         </tile>
        </tileset>
        """;

    [Fact]
    public void ReadMap_ReadsCsvLayers_ExternalTilesets_AndObjects()
    {
        const string tmx = """
            <?xml version="1.0" encoding="UTF-8"?>
            <map version="1.10" orientation="orthogonal" width="3" height="2" tilewidth="16" tileheight="16" infinite="0">
             <properties><property name="lit" type="bool" value="false"/></properties>
             <tileset firstgid="1" source="sets/terrain.tsx"/>
             <layer id="1" name="ground" width="3" height="2">
              <data encoding="csv">
            1,2,1,
            1,1,2147483650
            </data>
             </layer>
             <group name="extra">
              <layer id="2" name="decor" width="3" height="2" visible="0"><data encoding="csv">0,0,0,0,0,0</data></layer>
             </group>
             <objectgroup id="3" name="things">
              <object id="1" name="start" class="spawn" x="16" y="16"><point/></object>
              <object id="2" name="elder" type="actor" x="32" y="0" width="16" height="16">
               <properties><property name="dialogue" value="elder"/><property name="note">two
            lines</property></properties>
              </object>
              <object id="3" name="chest" class="item" gid="1" x="0" y="32" width="16" height="16"/>
             </objectgroup>
            </map>
            """;
        var map = TmxReader.ReadMap(tmx, path => path == "sets/terrain.tsx" ? Tsx : throw new FileNotFoundException(path));

        map.Width.Should().Be(3);
        map.Properties.GetBool("lit", fallback: true).Should().BeFalse();
        map.Tilesets.Single().Tileset.Image.Should().Be("art/terrain.png", "image paths end up relative to the map");
        map.Layers.Select(l => l.Name).Should().Equal("ground", "decor");
        map.Layer("decor").Visible.Should().BeFalse();
        map.IsSolid(new Cell(1, 0)).Should().BeTrue();
        map.IsSolid(new Cell(2, 1)).Should().BeTrue("the flip flag is dropped from the gid");
        map.IsSolid(new Cell(0, 0)).Should().BeFalse();
        map.IsSolid(new Cell(5, 5)).Should().BeTrue("outside the map counts as solid");
        map.Gid("wall").Should().Be(2);
        map.TileNames(new Cell(1, 0)).Should().Equal("wall");

        var objects = map.Objects.ToArray();
        objects[0].Should().Match<MapObject>(o => o.Type == "spawn" && o.Cell == new Cell(1, 1));
        objects[1].Type.Should().Be("actor");
        objects[1].Properties.GetString("dialogue").Should().Be("elder");
        objects[1].Properties.GetString("note").Should().Be("two\nlines");
        objects[2].Cell.Should().Be(new Cell(0, 1), "tile objects are anchored at their bottom-left corner");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("zlib")]
    [InlineData("gzip")]
    public void ReadMap_ReadsBase64Layers(string? compression)
    {
        var bytes = Gids.SelectMany(BitConverter.GetBytes).ToArray();
        if (compression is not null)
        {
            using var memory = new MemoryStream();
            using (Stream deflate = compression == "zlib" ? new ZLibStream(memory, CompressionLevel.Optimal) : new GZipStream(memory, CompressionLevel.Optimal))
            {
                deflate.Write(bytes);
            }

            bytes = memory.ToArray();
        }

        var compressionAttribute = compression is null ? "" : $" compression=\"{compression}\"";
        var tmx = $"""
            <map orientation="orthogonal" width="2" height="2" tilewidth="16" tileheight="16">
             <tileset firstgid="1" source="t.tsx"/>
             <layer name="ground" width="2" height="2"><data encoding="base64"{compressionAttribute}>{Convert.ToBase64String(bytes)}</data></layer>
            </map>
            """;

        var map = TmxReader.ReadMap(tmx, _ => Tsx);

        map.Layer("ground")[new Cell(1, 0)].Should().Be(2);
        map.Layer("ground")[new Cell(1, 1)].Should().Be(1);
    }

    [Fact]
    public void ReadTileset_ReadsTileAnimations()
    {
        const string tsx = """
            <tileset name="t" tilewidth="16" tileheight="16" tilecount="4" columns="2">
             <image source="t.png" width="32" height="32"/>
             <tile id="0">
              <animation>
               <frame tileid="0" duration="180"/>
               <frame tileid="3" duration="140"/>
              </animation>
             </tile>
            </tileset>
            """;
        var tileset = TmxReader.ReadTileset(tsx);

        tileset.Animation(0).Should().Equal(new TileFrame(0, 180), new TileFrame(3, 140));
        tileset.Frame(0, TimeSpan.FromMilliseconds(100)).Should().Be(0);
        tileset.Frame(0, TimeSpan.FromMilliseconds(200)).Should().Be(3);
        tileset.Frame(0, TimeSpan.FromMilliseconds(330)).Should().Be(0, "it loops");
        tileset.Frame(1, TimeSpan.FromSeconds(9)).Should().Be(1, "tiles without an animation stay put");
    }

    [Fact]
    public void ReadMap_RejectsInfiniteMaps()
    {
        var act = () => TmxReader.ReadMap("""<map orientation="orthogonal" width="2" height="2" tilewidth="16" tileheight="16" infinite="1"/>""", _ => "");

        act.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void FieldOfView_IsBlockedByWalls()
    {
        var map = Fixtures.Map(
            ".........",
            ".........",
            "....#....",
            ".........",
            ".........");
        var origin = new Cell(4, 4);

        var visible = FieldOfView.Compute(origin, 6, map.IsOpaque);

        visible.Should().Contain(origin).And.Contain(new Cell(0, 4)).And.Contain(new Cell(4, 2)).And.Contain(new Cell(0, 0));
        visible.Should().NotContain(new Cell(4, 0), "the wall hides what is straight behind it");
        FieldOfView.HasLineOfSight(origin, new Cell(4, 0), map.IsOpaque).Should().BeFalse();
        FieldOfView.HasLineOfSight(origin, new Cell(0, 0), map.IsOpaque).Should().BeTrue();
    }

    [Fact]
    public void FieldOfView_StopsAtTheRadius()
    {
        var map = Fixtures.Map(new string('.', 20));

        FieldOfView.Compute(new Cell(0, 0), 5, map.IsOpaque).Should().Contain(new Cell(5, 0)).And.NotContain(new Cell(6, 0));
    }

    [Fact]
    public void Pathfinder_WalksAroundWalls()
    {
        var map = Fixtures.Map(
            ".....",
            ".###.",
            ".....");

        var path = Pathfinder.Find(new Cell(2, 0), new Cell(2, 2), c => !map.IsSolid(c));

        path.Should().HaveCount(6).And.EndWith(new Cell(2, 2));
        path!.Zip(path!.Skip(1)).Should().OnlyContain(p => Pathfinder.Distance(p.First, p.Second) == 1);
        Pathfinder.Find(new Cell(0, 0), new Cell(0, 0), _ => true).Should().BeEmpty();
    }

    [Fact]
    public void Pathfinder_ReturnsNull_WhenWalledIn()
    {
        var map = Fixtures.Map(
            "..#..",
            "..#..");

        Pathfinder.Find(new Cell(0, 0), new Cell(4, 0), c => !map.IsSolid(c)).Should().BeNull();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Dungeons_AreConnected_AndRepeatBySeed(int seed)
    {
        var generator = new DungeonGenerator { Width = 40, Height = 25 };
        var dungeon = generator.Generate(seed);

        dungeon.Rooms.Should().HaveCountGreaterThan(3);
        dungeon.Rooms.Should().OnlyContain(r => r.X >= 1 && r.Y >= 1 && r.X + r.Width < 40 && r.Y + r.Height < 25);

        var reached = new HashSet<Cell> { dungeon.Entrance };
        var frontier = new Queue<Cell>(reached);
        while (frontier.TryDequeue(out var cell))
        {
            foreach (var next in GridDirections.All.Select(cell.Step).Where(n => dungeon.IsFloor(n) && reached.Add(n)))
            {
                frontier.Enqueue(next);
            }
        }

        reached.Should().BeEquivalentTo(dungeon.FloorCells, "every floor cell is reachable from the entrance");
        generator.Generate(seed).FloorCells.Should().Equal(dungeon.FloorCells);
        dungeon.EdgeWalls.Should().OnlyContain(c => !dungeon.IsFloor(c));
    }

    [Theory]
    [InlineData("maps", "../tiles/a.tsx", "tiles/a.tsx")]
    [InlineData("", "./a.png", "a.png")]
    [InlineData("a/b", "../../../c.png", "../c.png")]
    public void Combine_ResolvesRelativePaths(string folder, string path, string expected) =>
        TmxReader.Combine(folder, path).Should().Be(expected);
}
