using System.Numerics;
using Jentl.Geometry;

namespace Jentl.Raster;

/// <summary>Meshes made from pictures.</summary>
public static class Models
{
    /// <summary>
    /// A side view made solid: the outline of <paramref name="picture"/>'s opaque texels (see
    /// <see cref="Polygon2.Outline"/>) extruded <paramref name="depth"/> deep along Z, each texel
    /// <paramref name="pixelSize"/> across. Drawn with the picture as its texture, both sides show the picture and the
    /// edges its colours, as pixel art made into blocks. The model is centred on X and Z and stands on its lowest
    /// opaque row at y = 0, so a car sprite's wheels touch the ground. Empty if no texel is opaque.
    /// </summary>
    public static Mesh FromPicture(Texture picture, float pixelSize, float depth)
    {
        ArgumentNullException.ThrowIfNull(picture);
        var (width, height) = (picture.Width, picture.Height);
        var outline = Polygon2.Outline(width, height, (x, y) => picture.Pixels[(y * width) + x] >> 24 >= 128);
        if (outline.Count == 0)
        {
            return new Mesh([], []);
        }

        // Pixels run down; the model's y runs up from the outline's bottom, and x is centred on the outline.
        var bounds = Box2.FromPoints(outline);
        var (middle, bottom) = ((bounds.Min.X + bounds.Max.X) / 2, bounds.Max.Y);
        Vector2 Place(Vector2 p) => new((p.X - middle) * pixelSize, (bottom - p.Y) * pixelSize);
        var whole = new Box2(Place(new Vector2(0, height)), Place(new Vector2(width, 0)));
        return MeshBuilder.Extrude([.. outline.Select(Place)], depth, whole);
    }
}
