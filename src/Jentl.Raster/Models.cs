using System.Numerics;
using Jentl.Geometry;

namespace Jentl.Raster;

/// <summary>
/// A thing made of several materials (a car's paint, glass, tyres and lamps): one mesh per material, translucent ones
/// last so they show what is behind them. Make one with <see cref="MeshModelBuilder"/>; draw it with
/// <see cref="Renderer.DrawModel"/> (or <c>DrawModel</c> on a mesh view).
/// </summary>
public sealed class MeshModel
{
    internal MeshModel(IReadOnlyList<(Mesh Mesh, Material Material)> parts)
    {
        Parts = parts;
        Bounds = parts.Count == 0 ? default : parts.Select(p => p.Mesh.Bounds).Aggregate((a, b) => a.Union(b));
    }

    public IReadOnlyList<(Mesh Mesh, Material Material)> Parts { get; }

    public Box3 Bounds { get; }
}

/// <summary>Puts a <see cref="MeshModel"/> together from placed meshes; pieces of the same material are joined into one mesh.</summary>
public sealed class MeshModelBuilder
{
    private readonly List<(Material Material, List<(Mesh Mesh, Matrix4x4 Place)> Pieces)> _groups = [];

    /// <summary>Adds <paramref name="mesh"/>, placed by <paramref name="place"/>, in <paramref name="material"/>.</summary>
    public MeshModelBuilder Add(Mesh mesh, Matrix4x4 place, Material material)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(material);
        var group = _groups.FindIndex(g => g.Material == material);
        if (group < 0)
        {
            _groups.Add((material, []));
            group = _groups.Count - 1;
        }

        _groups[group].Pieces.Add((mesh, place));
        return this;
    }

    public MeshModel Build() => new([.. _groups
        .OrderBy(g => g.Material.Translucent)
        .Select(g => (MeshBuilder.Combine(g.Pieces), g.Material))]);
}

/// <summary>Meshes made from pictures.</summary>
public static class Models
{
    /// <summary>
    /// A side view made solid: the outline of <paramref name="picture"/>'s opaque texels (see
    /// <see cref="Polygon2.Outline"/>) extruded <paramref name="depth"/> deep along Z, each texel
    /// <paramref name="pixelSize"/> across. Drawn with the picture as its texture, both sides show the picture and the
    /// edges its colours, as pixel art made into blocks. The model is centred on X and Z and stands on its lowest
    /// opaque row at y = 0, so a car sprite's wheels touch the ground. The edges read their colours
    /// <paramref name="sideInset"/> texels in from the outline (past a dark border, say). Empty if no texel is opaque.
    /// </summary>
    public static Mesh FromPicture(Texture picture, float pixelSize, float depth, float sideInset = 0)
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
        return MeshBuilder.Extrude([.. outline.Select(Place)], depth, whole, sideInset * pixelSize);
    }
}
