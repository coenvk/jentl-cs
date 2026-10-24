namespace Jentl.Tracks;

/// <summary>One piece of a <see cref="TrackPath"/>.</summary>
/// <param name="Length">How long the piece is, in world units.</param>
public abstract record PathSegment(double Length);

/// <summary>A straight piece.</summary>
/// <param name="Length">How long it is.</param>
public sealed record Straight(double Length) : PathSegment(Length);

/// <summary>A piece of circle: a corner.</summary>
/// <param name="Radius">The circle's radius.</param>
/// <param name="Degrees">How far it turns: positive turns left, negative right (as seen on a screen whose y points down).</param>
public sealed record Arc(double Radius, double Degrees) : PathSegment(Radius * Math.Abs(Degrees) * Math.PI / 180);

/// <summary>Where a path is, and which way it points, at some distance along it.</summary>
/// <param name="Position">The point.</param>
/// <param name="Heading">The direction of travel in radians: 0 along +X, π/2 along +Y (down the screen).</param>
public readonly record struct PathPose(System.Numerics.Vector2 Position, float Heading);
