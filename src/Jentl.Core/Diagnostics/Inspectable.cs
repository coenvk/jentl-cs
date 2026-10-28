using Jentl.Physics;
using Microsoft.Xna.Framework;

namespace Jentl.Core.Diagnostics;

/// <summary>
/// Something a scene offers the <see cref="Inspector"/>: <see cref="Target"/>'s properties and fields are shown and
/// edited under <see cref="Name"/>. With <see cref="Bounds"/> (in the scene's coordinates, as it draws) it is outlined
/// on screen and can be picked by clicking it; with <see cref="Move"/> it can be dragged, by the distance the pointer
/// moved.
/// </summary>
public sealed record Inspectable(string Name, object Target, Func<Rectangle>? Bounds = null, Action<Vector2>? Move = null)
{
    /// <summary>
    /// Every body of a physics world, outlined and draggable: <paramref name="toScreen"/> maps a world point to the
    /// scene's coordinates and <paramref name="scale"/> is screen pixels per world unit (as for <c>PhysicsDebug</c>).
    /// A body is named by its <see cref="Body.UserData"/> when it has some, else by its type and id.
    /// </summary>
    public static IEnumerable<Inspectable> Bodies(PhysicsWorld world, Func<System.Numerics.Vector2, Vector2> toScreen, float scale)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(toScreen);
        foreach (var body in world.Bodies)
        {
            yield return new Inspectable(
                body.UserData is { } data and not string ? $"{data.GetType().Name} {body.Id}" : body.UserData as string ?? $"{body.Type} {body.Id}",
                body,
                () =>
                {
                    var bounds = body.Bounds;
                    var min = toScreen(bounds.Min);
                    var max = toScreen(bounds.Max);
                    return new Rectangle((int)MathF.Floor(min.X), (int)MathF.Floor(min.Y), (int)MathF.Ceiling(max.X - min.X), (int)MathF.Ceiling(max.Y - min.Y));
                },
                delta => body.Position += new System.Numerics.Vector2(delta.X, delta.Y) / scale);
        }
    }
}
