using Jentl.Rpg.Maps;

namespace Jentl.Assets.Tests;

public sealed class AssetTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("jentl-assets-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void Png_RoundTrips()
    {
        var image = Checker(5, 3);

        var back = Png.Decode(Png.Encode(image));

        back.Width.Should().Be(5);
        back.Height.Should().Be(3);
        back.Pixels.Should().Equal(image.Pixels);
    }

    [Fact]
    public void NotAnImage_IsReported()
    {
        var act = () => Png.Decode([1, 2, 3]);

        act.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void Packing_KeepsSpritesApart_AndInside()
    {
        var random = new Random(4);
        var sizes = Enumerable.Range(0, 60).Select(_ => (random.Next(4, 70), random.Next(4, 70))).ToList();

        var (width, height, regions) = AtlasPacker.Pack(sizes, padding: 2);

        for (var i = 0; i < regions.Length; i++)
        {
            var r = regions[i];
            (r.Width, r.Height).Should().Be(sizes[i]);
            r.X.Should().BeGreaterThanOrEqualTo(2);
            r.Y.Should().BeGreaterThanOrEqualTo(2);
            (r.X + r.Width + 2).Should().BeLessThanOrEqualTo(width);
            (r.Y + r.Height + 2).Should().BeLessThanOrEqualTo(height);
            for (var j = i + 1; j < regions.Length; j++)
            {
                Overlap(Grow(r, 2), regions[j]).Should().BeFalse($"sprites {i} and {j} (with padding) must not overlap");
            }
        }

        width.Should().BeOneOf(64, 128, 256, 512, 1024);
    }

    [Fact]
    public void Packing_PicksASmallSheet()
    {
        var sizes = Enumerable.Repeat((56, 56), 46).ToList();

        var (width, height, _) = AtlasPacker.Pack(sizes);

        (width, height).Should().Be((512, 512));
    }

    [Fact]
    public void TooMuch_DoesNotFit()
    {
        var act = () => AtlasPacker.Pack([(5000, 10)]);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Building_CopiesEachSprite_AndExtrudesItsEdges()
    {
        var red = Solid(4, 4, (255, 0, 0, 255));
        var blue = Solid(3, 6, (0, 0, 255, 255));

        var (sheet, atlas) = AtlasBuilder.Build([("red", red), ("blue", blue)], "sheet.png", padding: 2);

        var r = atlas.Sprites["red"];
        sheet[r.X, r.Y].Should().Be(((byte)255, (byte)0, (byte)0, (byte)255));
        sheet[r.X - 1, r.Y].Should().Be(((byte)255, (byte)0, (byte)0, (byte)255), "the left edge repeats into the padding");
        sheet[r.X + r.Width + 1, r.Y + r.Height + 1].Should().Be(((byte)255, (byte)0, (byte)0, (byte)255), "and the corner");
        var b = atlas.Sprites["blue"];
        (b.Width, b.Height).Should().Be((3, 6));
        sheet[b.X + 2, b.Y + 5].Should().Be(((byte)0, (byte)0, (byte)255, (byte)255));
    }

    [Fact]
    public void TwoSpritesWithOneName_AreRefused()
    {
        var act = () => AtlasBuilder.Build([("a", Solid(2, 2, default)), ("a", Solid(2, 2, default))], "x.png");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Descriptions_RoundTripAsJson()
    {
        var atlas = new AtlasFile("s.png", 64, 32, new Dictionary<string, AtlasRegion> { ["a/b"] = new(1, 2, 3, 4) }, "../Art", "abc");

        var back = AtlasFile.FromJson(atlas.ToJson());

        back.Sprites["a/b"].Should().Be(new AtlasRegion(1, 2, 3, 4));
        (back with { Sprites = atlas.Sprites }).Should().Be(atlas);
    }

    [Fact]
    public void AFolderBuilds_WithNamesFromItsPaths_AndAFingerprint()
    {
        var art = Directory.CreateDirectory(Path.Combine(_folder, "art", "crates")).Parent!.FullName;
        File.WriteAllBytes(Path.Combine(art, "crates", "crate1.png"), Png.Encode(Solid(8, 8, (9, 9, 9, 255))));
        File.WriteAllBytes(Path.Combine(art, "floor.png"), Png.Encode(Solid(8, 8, (1, 1, 1, 255))));

        var (_, atlas) = AtlasBuilder.FromFolder(art, "sheet.png");
        var before = AtlasBuilder.Fingerprint(art);
        File.WriteAllBytes(Path.Combine(art, "floor.png"), Png.Encode(Solid(8, 8, (2, 2, 2, 255))));

        atlas.Sprites.Keys.Should().Equal("crates/crate1", "floor");
        atlas.Fingerprint.Should().Be(before);
        AtlasBuilder.Fingerprint(art).Should().NotBe(before, "a changed sprite changes the fingerprint");
    }

    [Fact]
    public void TheCheck_FindsBrokenFilesAndStaleAtlases()
    {
        var assets = Directory.CreateDirectory(Path.Combine(_folder, "Assets")).FullName;
        var art = Directory.CreateDirectory(Path.Combine(_folder, "Art")).FullName;
        File.WriteAllBytes(Path.Combine(art, "tile.png"), Png.Encode(Solid(4, 4, (5, 5, 5, 255))));
        var (sheet, atlas) = AtlasBuilder.FromFolder(art, "sheet.png", "../Art");
        File.WriteAllBytes(Path.Combine(assets, "sheet.png"), Png.Encode(sheet));
        File.WriteAllText(Path.Combine(assets, "sheet.atlas.json"), atlas.ToJson());
        File.WriteAllText(Path.Combine(assets, "good.json"), "{ \"a\": 1 }");
        new AssetCheck(assets).Run().Should().BeEmpty();

        File.WriteAllText(Path.Combine(assets, "bad.json"), "{ \"a\": ");
        File.WriteAllBytes(Path.Combine(assets, "bad.png"), [1, 2, 3]);
        File.WriteAllBytes(Path.Combine(art, "tile.png"), Png.Encode(Solid(4, 4, (6, 6, 6, 255))));
        File.WriteAllText(Path.Combine(assets, "wide.atlas.json"), (atlas with { Image = "sheet.png", Sprites = new Dictionary<string, AtlasRegion> { ["x"] = new(60, 0, 10, 10) }, Source = null }).ToJson());

        new AssetCheck(assets).Run().Select(p => p.Path).Should().BeEquivalentTo(["bad.json", "bad.png", "sheet.atlas.json", "wide.atlas.json"]);
    }

    [Fact]
    public void ExtraChecks_RunByExtension()
    {
        File.WriteAllText(Path.Combine(_folder, "level.lvl"), "?");
        var check = new AssetCheck(_folder);
        check.Checks[".lvl"] = path => File.ReadAllText(path) == "?" ? "not a level" : null;

        check.Run().Should().ContainSingle().Which.Should().Be(new AssetProblem("level.lvl", "not a level"));
    }

    /// <summary>The game's own assets, as shipped: everything loads, maps and tilesets included, and the sprite atlas is current.</summary>
    [Fact]
    public void TheGamesAssets_PassTheCheck()
    {
        var check = new AssetCheck(Path.Combine(RepositoryRoot(), "src", "Sokoban.Desktop", "Assets"));
        check.Checks[".tmx"] = MapCheck.Map;
        check.Checks[".tsx"] = MapCheck.Tileset;

        check.Run().Should().BeEmpty("rebuild a stale atlas with: dotnet run --project tools/Assets -- atlas src/Sokoban.Desktop/Art/sprites src/Sokoban.Desktop/Assets/sprites.atlas.json");
    }

    private static Image Solid(int width, int height, (byte R, byte G, byte B, byte A) color)
    {
        var image = new Image(width, height);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                image[x, y] = color;
            }
        }

        return image;
    }

    private static Image Checker(int width, int height)
    {
        var image = new Image(width, height);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                image[x, y] = ((byte)(x * 40), (byte)(y * 80), (byte)((x + y) % 2 * 255), (byte)(200 + x));
            }
        }

        return image;
    }

    private static AtlasRegion Grow(AtlasRegion r, int by) => new(r.X - by, r.Y - by, r.Width + 2 * by, r.Height + 2 * by);

    private static bool Overlap(AtlasRegion a, AtlasRegion b) =>
        a.X < b.X + b.Width && b.X < a.X + a.Width && a.Y < b.Y + b.Height && b.Y < a.Y + a.Height;

    private static string RepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Sokoban.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new DirectoryNotFoundException("The repository root was not found.");
    }
}
