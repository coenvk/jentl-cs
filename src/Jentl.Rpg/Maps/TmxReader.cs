using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using Jentl.Puzzle;

namespace Jentl.Rpg.Maps;

/// <summary>
/// Reads maps (.tmx) and tilesets (.tsx) saved by the Tiled map editor (mapeditor.org): orthogonal,
/// fixed-size maps with tile layers in CSV, XML or base64 (optionally zlib or gzip compressed), object
/// layers, groups, and embedded or external tilesets.
/// </summary>
public static class TmxReader
{
    // The top bits of a gid mark flipped or rotated tiles; drawing ignores them, so they are dropped.
    private const uint GidMask = 0x0FFFFFFF;

    /// <summary>Reads a map.</summary>
    /// <param name="tmx">The map file's text.</param>
    /// <param name="readFile">
    /// Reads another file (an external tileset) by path relative to the map. Image paths in the result are
    /// relative to the map as well.
    /// </param>
    public static TileMap ReadMap(string tmx, Func<string, string> readFile)
    {
        ArgumentNullException.ThrowIfNull(readFile);
        var map = XDocument.Parse(tmx).Root ?? throw new FormatException("The map file is empty.");
        if (map.Name != "map")
        {
            throw new FormatException($"Expected a <map>, found <{map.Name}>.");
        }

        if ((string?)map.Attribute("orientation") is { } orientation && orientation != "orthogonal")
        {
            throw new NotSupportedException($"Only orthogonal maps are supported, not {orientation}.");
        }

        if (Int(map, "infinite", 0) != 0)
        {
            throw new NotSupportedException("Infinite maps are not supported; give the map a fixed size in Tiled.");
        }

        var width = Int(map, "width");
        var height = Int(map, "height");
        var tileWidth = Int(map, "tilewidth");
        var tileHeight = Int(map, "tileheight");

        var tilesets = new List<MapTileset>();
        foreach (var element in map.Elements("tileset"))
        {
            var firstGid = Int(element, "firstgid");
            if ((string?)element.Attribute("source") is { } source)
            {
                var folder = Folder(source);
                tilesets.Add(new MapTileset(firstGid, ReadTileset(XDocument.Parse(readFile(source)).Root!, folder)));
            }
            else
            {
                tilesets.Add(new MapTileset(firstGid, ReadTileset(element, "")));
            }
        }

        var layers = new List<TileLayer>();
        var objectLayers = new List<ObjectLayer>();
        ReadLayers(map, width, height, tileWidth, tileHeight, layers, objectLayers);
        return new TileMap(width, height, tileWidth, tileHeight, tilesets, layers, objectLayers, ReadProperties(map));
    }

    /// <summary>Reads a tileset file; the image path stays as written (relative to the tileset).</summary>
    public static Tileset ReadTileset(string tsx) =>
        ReadTileset(XDocument.Parse(tsx).Root ?? throw new FormatException("The tileset file is empty."), "");

    private static Tileset ReadTileset(XElement element, string folder)
    {
        var image = element.Element("image") ?? throw new FormatException("Only tilesets cut from a single image are supported.");
        var tiles = element.Elements("tile").ToDictionary(t => Int(t, "id"), ReadProperties);
        var tileWidth = Int(element, "tilewidth");
        var columns = Int(element, "columns", 0);
        if (columns == 0)
        {
            columns = Math.Max(1, Int(image, "width") / tileWidth);
        }

        return new Tileset(
            (string?)element.Attribute("name") ?? "tileset",
            tileWidth,
            Int(element, "tileheight"),
            columns,
            Int(element, "tilecount", 0),
            Combine(folder, (string?)image.Attribute("source") ?? throw new FormatException("A tileset image has no source.")),
            tiles);
    }

    private static void ReadLayers(XElement parent, int width, int height, int tileWidth, int tileHeight, List<TileLayer> layers, List<ObjectLayer> objectLayers)
    {
        foreach (var element in parent.Elements())
        {
            switch (element.Name.LocalName)
            {
                case "layer":
                    layers.Add(new TileLayer(
                        (string?)element.Attribute("name") ?? "",
                        width,
                        height,
                        ReadData(element.Element("data") ?? throw new FormatException("A tile layer has no data."), width * height),
                        ReadProperties(element),
                        Int(element, "visible", 1) != 0));
                    break;
                case "objectgroup":
                    objectLayers.Add(new ObjectLayer(
                        (string?)element.Attribute("name") ?? "",
                        [.. element.Elements("object").Select(o => ReadObject(o, tileWidth, tileHeight))]));
                    break;
                case "group":
                    ReadLayers(element, width, height, tileWidth, tileHeight, layers, objectLayers);
                    break;
            }
        }
    }

    private static int[] ReadData(XElement data, int count)
    {
        if (data.Elements("chunk").Any())
        {
            throw new NotSupportedException("Infinite maps (chunked layers) are not supported.");
        }

        var encoding = (string?)data.Attribute("encoding");
        uint[] raw;
        switch (encoding)
        {
            case "csv":
                raw = [.. data.Value.Split([',', '\n', '\r', ' ', '\t'], StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => uint.Parse(s, CultureInfo.InvariantCulture))];
                break;
            case "base64":
                raw = Decode(Convert.FromBase64String(data.Value.Trim()), (string?)data.Attribute("compression"));
                break;
            case null:
                raw = [.. data.Elements("tile").Select(t => (uint?)t.Attribute("gid") ?? 0)];
                break;
            default:
                throw new NotSupportedException($"Unknown layer encoding '{encoding}'.");
        }

        if (raw.Length != count)
        {
            throw new FormatException($"A layer has {raw.Length} tiles; the map needs {count}.");
        }

        return [.. raw.Select(g => (int)(g & GidMask))];
    }

    private static uint[] Decode(byte[] bytes, string? compression)
    {
        if (compression is not null)
        {
            using Stream input = new MemoryStream(bytes);
            using Stream inflate = compression switch
            {
                "zlib" => new ZLibStream(input, CompressionMode.Decompress),
                "gzip" => new GZipStream(input, CompressionMode.Decompress),
                _ => throw new NotSupportedException($"Unknown layer compression '{compression}'."),
            };
            using var output = new MemoryStream();
            inflate.CopyTo(output);
            bytes = output.ToArray();
        }

        var gids = new uint[bytes.Length / 4];
        for (var i = 0; i < gids.Length; i++)
        {
            gids[i] = BitConverter.ToUInt32(bytes, i * 4); // little-endian, as Tiled writes it
        }

        return gids;
    }

    private static MapObject ReadObject(XElement element, int tileWidth, int tileHeight)
    {
        var x = Float(element, "x");
        var y = Float(element, "y");
        var width = (int)Float(element, "width");
        var height = (int)Float(element, "height");

        // Tile objects are anchored at their bottom-left corner, everything else at the top-left.
        if (element.Attribute("gid") is not null)
        {
            y -= height;
        }

        var cell = new Cell((int)MathF.Floor((x + 0.01f) / tileWidth), (int)MathF.Floor((y + 0.01f) / tileHeight));
        var type = (string?)element.Attribute("class") ?? (string?)element.Attribute("type") ?? "";
        return new MapObject(Int(element, "id", 0), (string?)element.Attribute("name") ?? "", type, cell, width, height, ReadProperties(element));
    }

    private static Properties ReadProperties(XElement element)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in element.Element("properties")?.Elements("property") ?? [])
        {
            // Multi-line strings are stored as the element's text instead of a value attribute.
            values[(string?)property.Attribute("name") ?? ""] = (string?)property.Attribute("value") ?? property.Value;
        }

        return values.Count == 0 ? Properties.Empty : new Properties(values);
    }

    private static int Int(XElement element, string name, int? fallback = null) =>
        element.Attribute(name) is { } attribute ? int.Parse(attribute.Value, CultureInfo.InvariantCulture)
        : fallback ?? throw new FormatException($"<{element.Name}> has no {name} attribute.");

    private static float Float(XElement element, string name) =>
        element.Attribute(name) is { } attribute ? float.Parse(attribute.Value, CultureInfo.InvariantCulture) : 0;

    private static string Folder(string path)
    {
        var slash = path.Replace('\\', '/').LastIndexOf('/');
        return slash < 0 ? "" : path[..slash];
    }

    /// <summary>Joins two relative paths with forward slashes, resolving "." and "..".</summary>
    internal static string Combine(string folder, string path)
    {
        var parts = new List<string>();
        foreach (var part in $"{folder}/{path}".Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part == "..")
            {
                if (parts.Count > 0 && parts[^1] != "..")
                {
                    parts.RemoveAt(parts.Count - 1);
                }
                else
                {
                    parts.Add(part);
                }
            }
            else if (part != ".")
            {
                parts.Add(part);
            }
        }

        return string.Join('/', parts);
    }
}
