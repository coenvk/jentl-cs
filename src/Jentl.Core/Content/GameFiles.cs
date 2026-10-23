using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StbImageSharp;

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

    /// <summary>
    /// Supplies game files from elsewhere, taking precedence over <see cref="Root"/>. The browser build
    /// downloads every file up front and serves them from memory: the title container's synchronous
    /// requests cannot carry binary files intact.
    /// </summary>
    public static Func<string, Stream>? Provider { get; set; }

    public static Stream Open(string path) =>
        Provider is { } provider ? provider(path)
        : Root is { } root ? File.OpenRead(Path.Combine(root, path))
        : TitleContainer.OpenStream(path);

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

    /// <summary>
    /// Loads a PNG or JPEG. Decoded in managed code rather than with <c>Texture2D.FromStream</c>, which the
    /// browser platform does not support for image files. The result is not premultiplied; see
    /// <see cref="JentlGame"/>'s blend state.
    /// </summary>
    public static Texture2D LoadTexture(GraphicsDevice device, string path)
    {
        using var stream = Open(path);
        var image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
        var texture = new Texture2D(device, image.Width, image.Height);
        texture.SetData(image.Data);
        return texture;
    }
}
