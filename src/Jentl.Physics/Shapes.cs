using System.Numerics;

namespace Jentl.Physics;

/// <summary>An axis-aligned box from <see cref="Min"/> to <see cref="Max"/>.</summary>
public readonly record struct Aabb(Vector2 Min, Vector2 Max)
{
    public Vector2 Center => (Min + Max) / 2;

    public Vector2 Size => Max - Min;

    public static Aabb FromCenter(Vector2 center, Vector2 halfSize) => new(center - halfSize, center + halfSize);

    public bool Overlaps(Aabb other) =>
        Min.X < other.Max.X && other.Min.X < Max.X && Min.Y < other.Max.Y && other.Min.Y < Max.Y;

    public bool Contains(Vector2 point) =>
        point.X >= Min.X && point.X <= Max.X && point.Y >= Min.Y && point.Y <= Max.Y;
}

/// <summary>A body's outline, centred on its position. Shapes don't rotate.</summary>
public abstract class Shape
{
    public abstract Aabb Bounds(Vector2 position);
}

public sealed class Circle(float radius) : Shape
{
    public float Radius { get; } = radius > 0 ? radius : throw new ArgumentOutOfRangeException(nameof(radius), radius, "A circle needs a radius.");

    public override Aabb Bounds(Vector2 position) => Aabb.FromCenter(position, new Vector2(Radius));
}

/// <summary>An axis-aligned rectangle, <see cref="HalfSize"/> from its centre to each side.</summary>
public sealed class Box(Vector2 halfSize) : Shape
{
    public Box(float width, float height)
        : this(new Vector2(width / 2, height / 2))
    {
    }

    public Vector2 HalfSize { get; } = halfSize.X > 0 && halfSize.Y > 0 ? halfSize : throw new ArgumentOutOfRangeException(nameof(halfSize), halfSize, "A box needs a size.");

    public override Aabb Bounds(Vector2 position) => Aabb.FromCenter(position, HalfSize);
}
