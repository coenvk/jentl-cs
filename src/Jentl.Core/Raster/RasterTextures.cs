using Jentl.Assets;
using Microsoft.Xna.Framework;

namespace Jentl.Core.Raster;

/// <summary>Pictures for the software renderer, from images as files decode (see <see cref="Content.AssetStore.Image"/>).</summary>
public static class RasterTextures
{
    /// <summary>The <paramref name="source"/> part of <paramref name="image"/> (a sprite's region in an atlas, say) as a texture.</summary>
    public static Jentl.Raster.Texture From(Image image, Rectangle source)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (source.X < 0 || source.Y < 0 || source.Right > image.Width || source.Bottom > image.Height || source.Width <= 0 || source.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(source), $"{source} is not inside the {image.Width}x{image.Height} image.");
        }

        var rgba = new byte[source.Width * source.Height * 4];
        for (var y = 0; y < source.Height; y++)
        {
            Array.Copy(image.Pixels, (((source.Y + y) * image.Width) + source.X) * 4, rgba, y * source.Width * 4, source.Width * 4);
        }

        return Jentl.Raster.Texture.FromRgba(source.Width, source.Height, rgba);
    }

    /// <summary>The whole of <paramref name="image"/> as a texture.</summary>
    public static Jentl.Raster.Texture From(Image image)
    {
        ArgumentNullException.ThrowIfNull(image);
        return From(image, new Rectangle(0, 0, image.Width, image.Height));
    }
}
