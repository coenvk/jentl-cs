using System.Collections.Concurrent;
using Jentl.Assets;
using Jentl.Core.Graphics;
using Microsoft.Xna.Framework.Graphics;

namespace Jentl.Core.Content;

/// <summary>
/// Many sprites packed into one texture (made by the Assets tool): an image plus a description naming each
/// sprite's region. Drawing from one texture is cheaper, and a browser downloads one file instead of dozens.
/// </summary>
public sealed class TextureAtlas : IDisposable
{
    internal TextureAtlas(Texture2D texture, AtlasFile description)
    {
        Texture = texture;
        Description = description;
    }

    public Texture2D Texture { get; private set; }

    public AtlasFile Description { get; private set; }

    public IEnumerable<string> Names => Description.Sprites.Keys;

    /// <exception cref="KeyNotFoundException">The atlas has no sprite of that name.</exception>
    public Sprite this[string name] => TryGet(name, out var sprite) ? sprite : throw new KeyNotFoundException($"The atlas {Description.Image} has no sprite {name}.");

    public bool TryGet(string name, out Sprite sprite)
    {
        var found = Description.Sprites.TryGetValue(name, out var r);
        sprite = found ? new Sprite(Texture, new Microsoft.Xna.Framework.Rectangle(r.X, r.Y, r.Width, r.Height)) : default;
        return found;
    }

    public void Dispose() => Texture.Dispose();

    /// <summary>Takes new pixels and regions; pixels go into the same texture when the size allows, so sprites already handed out stay valid.</summary>
    internal bool Refresh(GraphicsDevice device, Image image, AtlasFile description)
    {
        var sameLayout = image.Width == Texture.Width && image.Height == Texture.Height
            && description.Sprites.Count == Description.Sprites.Count
            && description.Sprites.All(s => Description.Sprites.TryGetValue(s.Key, out var r) && r == s.Value);
        if (image.Width == Texture.Width && image.Height == Texture.Height)
        {
            Texture.SetData(image.Pixels);
        }
        else
        {
            Texture = AssetStore.Upload(device, image);
        }

        Description = description;
        return sameLayout;
    }
}

/// <summary>
/// Loads game assets by path (<c>Assets/...</c>), each once, and keeps them until disposed. On the desktop it can
/// also watch the game's source folder and reload what changes while the game runs: textures and atlases are
/// refreshed in place, and an atlas whose source sprites change is repacked on the spot; anything else is
/// re-read the next time it's asked for. <see cref="Changed"/> tells the game, for things it must redo itself
/// (a language file, a level). Call <see cref="Update"/> once a frame; the game does.
/// </summary>
public sealed class AssetStore(GraphicsDevice device) : IDisposable
{
    private const int Retries = 20;

    private readonly Dictionary<string, Texture2D> _textures = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TextureAtlas> _atlases = new(StringComparer.Ordinal);
    private readonly Dictionary<string, object> _loaded = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<(string Path, int Tries)> _changes = new();
    private readonly List<Texture2D> _retired = [];
    private FileSystemWatcher? _watcher;

    /// <summary>A file changed and was reloaded (its path, as asked for: <c>Assets/lang/nl.json</c>).</summary>
    public event Action<string>? Changed;

    /// <summary>The folder being watched, if hot reload is on; files found there take precedence over the shipped ones.</summary>
    public string? WatchedFolder { get; private set; }

    public Texture2D Texture(string path)
    {
        if (!_textures.TryGetValue(path, out var texture))
        {
            texture = Upload(device, Png.Decode(ReadBytes(path)));
            _textures[path] = texture;
        }

        return texture;
    }

    /// <summary>
    /// An image decoded but kept off the graphics card (for the software renderer, say, through
    /// <see cref="Raster.RasterTextures"/>); read afresh each time, so keep what you make of it.
    /// </summary>
    public Image Image(string path) => Png.Decode(ReadBytes(path));

    /// <summary>An atlas by its description's path (<c>Assets/sprites.atlas.json</c>); the image sits next to it.</summary>
    public TextureAtlas Atlas(string path)
    {
        if (!_atlases.TryGetValue(path, out var atlas))
        {
            var description = AtlasFile.FromJson(ReadText(path));
            atlas = new TextureAtlas(Upload(device, Png.Decode(ReadBytes(Sibling(path, description.Image)))), description);
            _atlases[path] = atlas;
        }

        return atlas;
    }

    /// <summary>Something read from a text file (JSON, a level pack), made once by <paramref name="read"/> and kept; a change on disk drops it, so the next call reads it afresh.</summary>
    public T Get<T>(string path, Func<string, T> read)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(read);
        if (!_loaded.TryGetValue(path, out var value))
        {
            value = read(ReadText(path));
            _loaded[path] = value;
        }

        return (T)value;
    }

    /// <summary>A text file, as it is now (from the watched folder if it's there).</summary>
    public string ReadText(string path)
    {
        using var reader = new StreamReader(new MemoryStream(ReadBytes(path)));
        return reader.ReadToEnd();
    }

    public byte[] ReadBytes(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return WatchedFolder is { } folder && File.Exists(Path.Combine(folder, path))
            ? File.ReadAllBytes(Path.Combine(folder, path))
            : GameFiles.ReadAllBytes(path);
    }

    /// <summary>
    /// Turns hot reload on: <paramref name="folder"/> is the game's source folder (holding <c>Assets/</c>), whose
    /// files are read in preference to the shipped copies, and whose changes reload what was loaded from it.
    /// Does nothing in a browser.
    /// </summary>
    public void Watch(string folder)
    {
        if (OperatingSystem.IsBrowser())
        {
            return;
        }

        WatchedFolder = Path.GetFullPath(folder);
        _watcher?.Dispose();
        _watcher = new FileSystemWatcher(WatchedFolder) { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size };
        _watcher.Changed += (_, e) => Queue(e.FullPath);
        _watcher.Created += (_, e) => Queue(e.FullPath);
        _watcher.Renamed += (_, e) => Queue(e.FullPath);
        _watcher.EnableRaisingEvents = true;
    }

    /// <summary>Applies the changes seen since the last call. Call on the game's thread, once a frame.</summary>
    public void Update()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var count = _changes.Count;
        for (var i = 0; i < count && _changes.TryDequeue(out var change); i++)
        {
            if (!seen.Add(change.Path))
            {
                continue;
            }

            try
            {
                Reload(change.Path);
            }
            catch (IOException) when (change.Tries < Retries)
            {
                // Still being written; try again next frame.
                _changes.Enqueue((change.Path, change.Tries + 1));
            }
#pragma warning disable CA1031 // A broken file while editing must not stop the game; the old asset stays.
            catch (Exception e) when (e is not OutOfMemoryException)
#pragma warning restore CA1031
            {
                System.Diagnostics.Debug.WriteLine($"Could not reload {change.Path}: {e.Message}");
            }
        }
    }

    /// <summary>Reloads whatever came from <paramref name="path"/> (relative to the watched folder), and repacks atlases built from it.</summary>
    public void Reload(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        path = path.Replace('\\', '/');
        if (_textures.TryGetValue(path, out var texture))
        {
            var image = Png.Decode(ReadBytes(path));
            if (image.Width == texture.Width && image.Height == texture.Height)
            {
                texture.SetData(image.Pixels);
            }
            else
            {
                _retired.Add(texture);
                _textures[path] = Upload(device, image);
            }

            Changed?.Invoke(path);
        }

        foreach (var (atlasPath, atlas) in _atlases)
        {
            if (atlasPath == path || Sibling(atlasPath, atlas.Description.Image) == path)
            {
                var description = AtlasFile.FromJson(ReadText(atlasPath));
                Refresh(atlas, Png.Decode(ReadBytes(Sibling(atlasPath, description.Image))), description);
                Changed?.Invoke(atlasPath);
            }
            else if (WatchedFolder is { } folder && atlas.Description.Source is { } source
                && Path.GetFullPath(Path.Combine(folder, Sibling(atlasPath, source))) is var sourceFolder
                && Path.GetFullPath(Path.Combine(folder, path)).StartsWith(sourceFolder + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                && path.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            {
                // A source sprite changed: repack, as the Assets tool would (without writing anything).
                var (image, description) = AtlasBuilder.FromFolder(sourceFolder, atlas.Description.Image, source);
                Refresh(atlas, image, description);
                Changed?.Invoke(atlasPath);
            }
        }

        if (_loaded.Remove(path))
        {
            Changed?.Invoke(path);
        }
        else if (!_textures.ContainsKey(path) && path.StartsWith("Assets/", StringComparison.Ordinal))
        {
            // Not cached here, but the game may read it itself (a language file): let it know.
            Changed?.Invoke(path);
        }
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        foreach (var texture in _textures.Values.Concat(_retired))
        {
            texture.Dispose();
        }

        foreach (var atlas in _atlases.Values)
        {
            atlas.Dispose();
        }
    }

    internal static Texture2D Upload(GraphicsDevice device, Image image)
    {
        var texture = new Texture2D(device, image.Width, image.Height);
        texture.SetData(image.Pixels);
        return texture;
    }

    /// <summary>A path next to <paramref name="path"/>: <c>Assets/sprites.atlas.json</c> and <c>sprites.png</c> make <c>Assets/sprites.png</c>.</summary>
    private static string Sibling(string path, string name)
    {
        var folder = path.LastIndexOf('/') is var slash and >= 0 ? path[..(slash + 1)] : "";
        return NormalizeDots(folder + name);
    }

    private static string NormalizeDots(string path)
    {
        var parts = new List<string>();
        foreach (var part in path.Split('/'))
        {
            if (part == ".." && parts.Count > 0 && parts[^1] != "..")
            {
                parts.RemoveAt(parts.Count - 1);
            }
            else if (part != ".")
            {
                parts.Add(part);
            }
        }

        return string.Join('/', parts);
    }

    private void Queue(string fullPath)
    {
        if (WatchedFolder is { } folder && !Directory.Exists(fullPath))
        {
            _changes.Enqueue((Path.GetRelativePath(folder, fullPath).Replace('\\', '/'), 0));
        }
    }

    private void Refresh(TextureAtlas atlas, Image image, AtlasFile description)
    {
        var old = atlas.Texture;
        atlas.Refresh(device, image, description);
        if (!ReferenceEquals(old, atlas.Texture))
        {
            _retired.Add(old);
        }
    }
}
