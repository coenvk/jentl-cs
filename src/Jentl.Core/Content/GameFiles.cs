using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Jentl.Core.Content;

/// <summary>
/// Reads files shipped with the game, by path relative to the game's root (e.g. <c>Assets/fonts/x.ttf</c>).
/// </summary>
/// <remarks>
/// With <see cref="Root"/> set (desktop) files are read from that folder. Without it they go through
/// <see cref="TitleContainer"/>, which in a browser fetches them from the server. Desktop sets a root
/// because under NativeAOT MonoGame's title container resolves paths against the working directory.
/// </remarks>
public static class GameFiles
{
    /// <summary>The folder game files are read from; null to use <see cref="TitleContainer"/>.</summary>
    public static string? Root { get; set; }

    public static Stream Open(string path) =>
        Root is { } root ? File.OpenRead(Path.Combine(root, path)) : TitleContainer.OpenStream(path);

    public static byte[] ReadAllBytes(string path)
    {
        using var stream = Open(path);
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    public static string ReadAllText(string path)
    {
        using var reader = new StreamReader(Open(path));
        return reader.ReadToEnd();
    }

    /// <summary>Loads a PNG or JPEG. The result is not premultiplied; see <see cref="JentlGame"/>'s blend state.</summary>
    public static Texture2D LoadTexture(GraphicsDevice device, string path)
    {
        using var stream = Open(path);
        return Texture2D.FromStream(device, stream);
    }
}
