// Asset tooling for Jentl games.
//
// Usage:
//   Assets atlas <sprite-folder> <output.atlas.json> [--padding <n>]
//       Packs every PNG under the folder into one image (output.png next to the description). Sprites are named
//       by their path in the folder without ".png" ("floors/floor1"). The description records the folder and a
//       fingerprint of its files, so `check` can tell when the atlas needs rebuilding.
//   Assets check <assets-folder>
//       Checks that images decode, JSON parses, atlases fit their images and are up to date, and Tiled maps and
//       tilesets load with every tile known and every image present. Exits with 1 if anything is wrong.
//
// Example (from the repository root):
//   dotnet run --project tools/Assets -- atlas src/Sokoban.Desktop/Art/sprites src/Sokoban.Desktop/Assets/sprites.atlas.json
//   dotnet run --project tools/Assets -- check src/Sokoban.Desktop/Assets

using System.Globalization;
using Jentl.Assets;
using Jentl.Rpg.Maps;

if (args is ["atlas", var source, var output, ..])
{
    var padding = args is [_, _, _, "--padding", var p] ? int.Parse(p, CultureInfo.InvariantCulture) : 2;
    if (!output.EndsWith(AssetCheck.AtlasSuffix, StringComparison.OrdinalIgnoreCase))
    {
        Console.Error.WriteLine($"The output should end in {AssetCheck.AtlasSuffix}.");
        return 1;
    }

    var folder = Path.GetDirectoryName(Path.GetFullPath(output))!;
    var imageFile = Path.GetFileName(output)[..^AssetCheck.AtlasSuffix.Length] + ".png";
    var relativeSource = Path.GetRelativePath(folder, Path.GetFullPath(source)).Replace('\\', '/');
    var (image, atlas) = AtlasBuilder.FromFolder(source, imageFile, relativeSource, padding);
    File.WriteAllBytes(Path.Combine(folder, imageFile), Png.Encode(image));
    File.WriteAllText(output, atlas.ToJson() + "\n");
    Console.WriteLine($"Packed {atlas.Sprites.Count} sprites into {imageFile} ({atlas.Width}x{atlas.Height}).");
    return 0;
}

if (args is ["check", var assets])
{
    var check = new AssetCheck(assets);
    check.Checks[".tmx"] = MapCheck.Map;
    check.Checks[".tsx"] = MapCheck.Tileset;
    var problems = check.Run();
    foreach (var problem in problems)
    {
        Console.Error.WriteLine(problem);
    }

    Console.WriteLine(problems.Count == 0 ? "All assets are fine." : $"{problems.Count} problem(s).");
    return problems.Count == 0 ? 0 : 1;
}

Console.Error.WriteLine("Usage: Assets atlas <sprite-folder> <output.atlas.json> [--padding <n>] | Assets check <assets-folder>");
return 2;
