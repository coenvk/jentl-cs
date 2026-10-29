using System.Numerics;
using Jentl.Geometry;
using Jentl.Raster;

namespace Jentl.Scene3D;

/// <summary>A mesh in a material, one of the things a <see cref="Node"/> shows.</summary>
public readonly record struct MeshPart(Mesh Mesh, Material Material);

/// <summary>
/// A thing in a 3D scene: placed by its position, rotation and scale relative to its parent, showing any number of
/// meshes, and carrying children that move with it (a car and its wheels, an arm and its hand). The root of a tree is
/// the scene; draw it with <see cref="RenderList"/>. Models face +Z, as in glTF.
/// </summary>
public sealed class Node
{
    private readonly List<Node> _children = [];

    public Node(string name = "") => Name = name;

    public string Name { get; set; }

    public Vector3 Position { get; set; }

    public Quaternion Rotation { get; set; } = Quaternion.Identity;

    public Vector3 Scale { get; set; } = Vector3.One;

    /// <summary>Whether it and its children are drawn (and picked).</summary>
    public bool Visible { get; set; } = true;

    /// <summary>The meshes it shows, in its own space.</summary>
    public List<MeshPart> Parts { get; } = [];

    /// <summary>Anything the game wants to keep with it (the entity it shows, say).</summary>
    public object? Tag { get; set; }

    public Node? Parent { get; private set; }

    public IReadOnlyList<Node> Children => _children;

    /// <summary>Its placement relative to its parent: scale, then rotation, then position. Setting it splits it into those three.</summary>
    public Matrix4x4 Local
    {
        get => Matrix4x4.CreateScale(Scale) * Matrix4x4.CreateFromQuaternion(Rotation) * Matrix4x4.CreateTranslation(Position);
        set
        {
            if (!Matrix4x4.Decompose(value, out var scale, out var rotation, out var position))
            {
                throw new ArgumentException("The matrix can't be split into a scale, a rotation and a position (it shears or is flat).", nameof(value));
            }

            (Scale, Rotation, Position) = (scale, rotation, position);
        }
    }

    /// <summary>Its placement in the world: its own, then each parent's.</summary>
    public Matrix4x4 World => Parent is null ? Local : Local * Parent.World;

    public Vector3 WorldPosition => World.Translation;

    /// <summary>Its rotation in the world.</summary>
    public Quaternion WorldRotation => Parent is null ? Rotation : Quaternion.Concatenate(Rotation, Parent.WorldRotation);

    /// <summary>The way it faces in the world (its +Z), unit length.</summary>
    public Vector3 Forward => Vector3.Transform(Vector3.UnitZ, WorldRotation);

    /// <summary>Adds a child (taking it from any parent it had) and returns it.</summary>
    public Node Add(Node child)
    {
        ArgumentNullException.ThrowIfNull(child);
        for (var up = this; up is not null; up = up.Parent)
        {
            if (up == child)
            {
                throw new InvalidOperationException($"'{child.Name}' can't go inside itself.");
            }
        }

        child.Parent?._children.Remove(child);
        child.Parent = this;
        _children.Add(child);
        return child;
    }

    /// <summary>Adds a child showing <paramref name="mesh"/> in <paramref name="material"/>, and returns it.</summary>
    public Node Add(string name, Mesh mesh, Material material)
    {
        var child = new Node(name);
        child.Parts.Add(new MeshPart(mesh, material));
        return Add(child);
    }

    public bool Remove(Node child)
    {
        ArgumentNullException.ThrowIfNull(child);
        if (!_children.Remove(child))
        {
            return false;
        }

        child.Parent = null;
        return true;
    }

    /// <summary>Takes it out of its parent.</summary>
    public void Detach() => Parent?.Remove(this);

    /// <summary>It and everything below it, parents before children.</summary>
    public IEnumerable<Node> SelfAndDescendants()
    {
        var stack = new Stack<Node>();
        stack.Push(this);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            yield return node;
            for (var i = node._children.Count - 1; i >= 0; i--)
            {
                stack.Push(node._children[i]);
            }
        }
    }

    /// <summary>The first node by this name, it or below it, parents first; null if none.</summary>
    public Node? Find(string name) => SelfAndDescendants().FirstOrDefault(node => node.Name == name);

    /// <summary>Turns it about an axis of its own by <paramref name="angle"/> radians.</summary>
    public void Turn(Vector3 axis, float angle) =>
        Rotation = Quaternion.Normalize(Quaternion.Concatenate(Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), angle), Rotation));

    /// <summary>Turns it so it faces <paramref name="target"/> (a point in the world), its top towards <paramref name="up"/>.</summary>
    public void LookAt(Vector3 target, Vector3? up = null)
    {
        var direction = target - WorldPosition;
        if (direction.LengthSquared() < 1e-12f)
        {
            return;
        }

        // Straight up or down the given up is no use; any other will do.
        direction = Vector3.Normalize(direction);
        var top = up ?? Vector3.UnitY;
        if (Vector3.Cross(direction, top).LengthSquared() < 1e-8f)
        {
            top = MathF.Abs(direction.Z) < 0.9f ? Vector3.UnitZ : Vector3.UnitX;
        }

        var facing = Quaternion.CreateFromRotationMatrix(Matrix4x4.CreateWorld(Vector3.Zero, -direction, top));
        Rotation = Parent is null ? facing : Quaternion.Concatenate(facing, Quaternion.Inverse(Parent.WorldRotation));
    }

    /// <summary>The box around its own meshes, in its own space; null if it has none.</summary>
    public Box3? Bounds => Parts.Count == 0 ? null : Parts.Select(part => part.Mesh.Bounds).Aggregate((a, b) => a.Union(b));

    /// <summary>The box around every visible mesh in it and below it, in the world; null if there are none.</summary>
    public Box3? WorldBounds()
    {
        Box3? all = null;
        Visit(this, Parent?.World ?? Matrix4x4.Identity);
        return all;

        void Visit(Node node, Matrix4x4 parent)
        {
            if (!node.Visible)
            {
                return;
            }

            var world = node.Local * parent;
            if (node.Bounds is { } own)
            {
                var placed = own.Transform(world);
                all = all is { } so ? so.Union(placed) : placed;
            }

            foreach (var child in node._children)
            {
                Visit(child, world);
            }
        }
    }

    /// <summary>A copy of it and everything below it, sharing the meshes and materials; the copy has no parent.</summary>
    public Node Clone()
    {
        var copy = new Node(Name) { Position = Position, Rotation = Rotation, Scale = Scale, Visible = Visible, Tag = Tag };
        copy.Parts.AddRange(Parts);
        foreach (var child in _children)
        {
            copy.Add(child.Clone());
        }

        return copy;
    }

    public override string ToString() => Name.Length > 0 ? Name : "(node)";
}
