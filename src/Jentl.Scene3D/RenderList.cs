using System.Numerics;
using Jentl.Geometry;
using Jentl.Raster;

namespace Jentl.Scene3D;

/// <summary>One mesh to draw: where, in what, how far from the eye, and for which node.</summary>
public readonly record struct RenderItem(Mesh Mesh, Matrix4x4 World, Material Material, float Distance, Node Node);

/// <summary>
/// What a camera sees of a scene, in the order to draw it: nodes out of view are left out (by their boxes against
/// the view frustum), solid meshes come first grouped by material and nearest first (so hidden pixels are skipped),
/// then see-through ones furthest first (so each blends over what is behind it). Built once a frame.
/// </summary>
public sealed class RenderList
{
    private readonly List<RenderItem> _items = [];

    public IReadOnlyList<RenderItem> Items => _items;

    /// <summary>Meshes in view at the last <see cref="Build"/>.</summary>
    public int Drawn => _items.Count;

    /// <summary>Meshes left out at the last <see cref="Build"/> for being out of view.</summary>
    public int Culled { get; private set; }

    /// <summary>
    /// Gathers the meshes of <paramref name="root"/> and below it that <paramref name="viewProjection"/> sees, from
    /// the eye at <paramref name="eye"/>.
    /// </summary>
    public RenderList Build(Node root, Matrix4x4 viewProjection, Vector3 eye)
    {
        ArgumentNullException.ThrowIfNull(root);
        _items.Clear();
        Culled = 0;
        var frustum = Frustum.From(viewProjection);
        Visit(root, root.Parent?.World ?? Matrix4x4.Identity);

        // Materials have no order of their own; the first time each appears groups them stably.
        var groups = new Dictionary<Material, int>(ReferenceEqualityComparer.Instance);
        foreach (var item in _items)
        {
            groups.TryAdd(item.Material, groups.Count);
        }

        _items.Sort((a, b) => (a.Material.Translucent, b.Material.Translucent) switch
        {
            (false, true) => -1,
            (true, false) => 1,
            (true, true) => b.Distance.CompareTo(a.Distance),
            _ => groups[a.Material] != groups[b.Material] ? groups[a.Material].CompareTo(groups[b.Material]) : a.Distance.CompareTo(b.Distance),
        });
        return this;

        void Visit(Node node, Matrix4x4 parent)
        {
            if (!node.Visible)
            {
                return;
            }

            var world = node.Local * parent;
            foreach (var part in node.Parts)
            {
                var box = part.Mesh.Bounds.Transform(world);
                if (part.Mesh.Positions.Count == 0 || frustum.Test(box) == Containment.Outside)
                {
                    Culled++;
                    continue;
                }

                _items.Add(new RenderItem(part.Mesh, world, part.Material, Vector3.Distance(eye, box.Center), node));
            }

            foreach (var child in node.Children)
            {
                Visit(child, world);
            }
        }
    }
}
