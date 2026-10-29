using System.Numerics;
using Jentl.Geometry;
using Jentl.Raster;

namespace Jentl.Scene3D;

/// <summary>Where a ray first met a scene: the node, which of its meshes, how far along the ray, and the point.</summary>
public readonly record struct PickHit(Node Node, MeshPart Part, float Distance, Vector3 Point);

/// <summary>Finding what is under the pointer.</summary>
public static class Picking
{
    /// <summary>
    /// The ray from <paramref name="camera"/> through <paramref name="point"/> on a picture <paramref name="size"/>
    /// big (pixels, y down), its direction unit length.
    /// </summary>
    public static Ray RayThrough(this Camera camera, Vector2 point, Vector2 size)
    {
        ArgumentNullException.ThrowIfNull(camera);
        var ndc = new Vector2((point.X / size.X * 2) - 1, 1 - (point.Y / size.Y * 2));
        Matrix4x4.Invert(camera.ViewProjection(size.X / size.Y), out var inverse);
        var near = Vector4.Transform(new Vector4(ndc, 0, 1), inverse);
        var far = Vector4.Transform(new Vector4(ndc, 1, 1), inverse);
        var from = new Vector3(near.X, near.Y, near.Z) / near.W;
        var to = new Vector3(far.X, far.Y, far.Z) / far.W;
        return new Ray(from, Vector3.Normalize(to - from));
    }

    /// <summary>
    /// The nearest visible mesh of <paramref name="root"/> and below it that <paramref name="ray"/> hits (by its
    /// triangles, either side), or null; <paramref name="where"/> skips nodes it says no to (and their children).
    /// </summary>
    public static PickHit? Pick(this Node root, Ray ray, Func<Node, bool>? where = null)
    {
        ArgumentNullException.ThrowIfNull(root);
        PickHit? best = null;
        Visit(root, root.Parent?.World ?? Matrix4x4.Identity);
        return best;

        void Visit(Node node, Matrix4x4 parent)
        {
            if (!node.Visible || (where is not null && !where(node)))
            {
                return;
            }

            var world = node.Local * parent;
            foreach (var part in node.Parts)
            {
                if (!part.Mesh.Bounds.Transform(world).Intersect(ray, out var enter) || (best is { } near && enter > near.Distance))
                {
                    continue;
                }

                // In the mesh's own space; an affine map keeps distances along the ray the same multiples.
                if (!Matrix4x4.Invert(world, out var inverse))
                {
                    continue;
                }

                var local = new Ray(Vector3.Transform(ray.Origin, inverse), Vector3.TransformNormal(ray.Direction, inverse));
                foreach (var t in part.Mesh.Triangles)
                {
                    if (part.Mesh.TriangleAt(t).Intersect(local, out var distance) && (best is not { } so || distance < so.Distance))
                    {
                        best = new PickHit(node, part, distance, ray.At(distance));
                    }
                }
            }

            foreach (var child in node.Children)
            {
                Visit(child, world);
            }
        }
    }
}
