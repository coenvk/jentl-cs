using Jentl.Core.Graphics;
using Jentl.Physics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Vector2 = Microsoft.Xna.Framework.Vector2;

namespace Jentl.Core.Diagnostics;

/// <summary>
/// Draws a <see cref="PhysicsWorld"/>'s bodies as outlines, for a scene's <c>DrawDebug</c>: static grey, dynamic
/// green, kinematic blue, sensors yellow; each contact as a red line along its normal.
/// </summary>
public static class PhysicsDebug
{
    private const int CircleSegments = 20;

    /// <param name="offset">Where the world's origin lands on screen.</param>
    /// <param name="scale">Screen pixels per world unit.</param>
    public static void Draw(SpriteBatch batch, PhysicsWorld world, Vector2 offset, float scale)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(world);
        Vector2 Screen(System.Numerics.Vector2 p) => offset + new Vector2(p.X, p.Y) * scale;

        foreach (var body in world.Bodies)
        {
            var color = body.IsSensor ? new Color(255, 220, 60)
                : body.Type switch
                {
                    BodyType.Static => new Color(170, 170, 185),
                    BodyType.Kinematic => new Color(90, 170, 255),
                    _ => new Color(110, 230, 120),
                };
            switch (body.Shape)
            {
                case Circle circle:
                    var centre = Screen(body.Position);
                    var radius = circle.Radius * scale;
                    for (var i = 0; i < CircleSegments; i++)
                    {
                        var a = MathF.Tau * i / CircleSegments;
                        var b = MathF.Tau * (i + 1) / CircleSegments;
                        batch.DrawLine(centre + new Vector2(MathF.Cos(a), MathF.Sin(a)) * radius, centre + new Vector2(MathF.Cos(b), MathF.Sin(b)) * radius, color, 1.5f);
                    }

                    if (body.Velocity != System.Numerics.Vector2.Zero)
                    {
                        // Where it is heading, a tenth of a second ahead.
                        batch.DrawLine(centre, Screen(body.Position + body.Velocity * 0.1f), color * 0.7f);
                    }

                    break;
                default:
                    var bounds = body.Bounds;
                    var min = Screen(bounds.Min);
                    var max = Screen(bounds.Max);
                    batch.StrokeRectangle(new Rectangle((int)min.X, (int)min.Y, (int)(max.X - min.X), (int)(max.Y - min.Y)), color, 2);
                    break;
            }
        }

        foreach (var contact in world.Contacts)
        {
            var point = Screen(contact.Point);
            batch.DrawLine(point, point + new Vector2(contact.Normal.X, contact.Normal.Y) * 14, new Color(255, 70, 70), 2);
        }
    }
}
