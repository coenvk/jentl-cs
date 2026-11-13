using Jentl.Core.Graphics;
using Jentl.Traffic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Numerics = System.Numerics;

namespace Jentl.Core.Traffic;

/// <summary>
/// Draws a <see cref="TrafficSimulation"/> from above: asphalt for every lane (junctions fill in from their turns),
/// dashed lines between the two directions of a road, a green island in each roundabout, a stop line in the colour
/// of its light at each signalled lane, and every vehicle as a box of its size with a windscreen at the front. The
/// network's metres (y north) map to the screen through <see cref="Centre"/> and <see cref="Scale"/>;
/// <see cref="Fit"/> sets both to show a whole network.
/// </summary>
public sealed class TrafficView
{
    private static readonly Color Asphalt = new(70, 72, 78);
    private static readonly Color Marking = new(236, 236, 228);
    private static readonly Color Island = new(92, 150, 82);

    private static readonly Color[] CarColours =
    [
        new(196, 52, 52), new(54, 98, 180), new(232, 232, 236), new(40, 42, 48), new(150, 152, 160), new(46, 132, 84), new(218, 160, 44),
    ];

    /// <summary>The point of the network at the middle of the area drawn into.</summary>
    public Numerics.Vector2 Centre { get; set; }

    /// <summary>Pixels per metre.</summary>
    public float Scale { get; set; } = 2;

    public float LaneWidth { get; set; } = 3.5f;

    /// <summary>Shows all of <paramref name="network"/> in <paramref name="area"/>, with a little room round it.</summary>
    public void Fit(RoadNetwork network, Rectangle area)
    {
        ArgumentNullException.ThrowIfNull(network);
        var (min, max) = network.Bounds();
        var size = max - min + new Numerics.Vector2(LaneWidth * 6);
        Centre = (min + max) / 2;
        Scale = MathF.Min(area.Width / size.X, area.Height / size.Y);
    }

    public Vector2 ToScreen(Rectangle area, Numerics.Vector2 world) =>
        new(area.Center.X + ((world.X - Centre.X) * Scale), area.Center.Y - ((world.Y - Centre.Y) * Scale));

    public Numerics.Vector2 ToWorld(Rectangle area, Vector2 screen) =>
        new(Centre.X + ((screen.X - area.Center.X) / Scale), Centre.Y - ((screen.Y - area.Center.Y) / Scale));

    public void Draw(SpriteBatch batch, TrafficSimulation simulation, Rectangle area)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(simulation);
        var network = simulation.Network;
        var width = LaneWidth * Scale;

        // Asphalt: every lane a little wider than itself so neighbours join up.
        foreach (var lane in network.Lanes)
        {
            Polyline(batch, area, lane.Path.Points, Asphalt, width * 1.15f);
        }

        foreach (var node in network.Nodes.Where(n => n.Control == NodeControl.Roundabout))
        {
            batch.FillCircle(ToScreen(area, node.Position), (node.RingRadius - (LaneWidth * 0.6f)) * Scale, Island);
        }

        // Dashes between the directions of a two-way road.
        foreach (var road in network.Roads.Where(r => r.Forward.Count > 0 && r.Backward.Count > 0))
        {
            var forward = road.Forward[0].Path;
            for (var s = 2f; s < forward.Length - 2; s += 9)
            {
                var (a, direction) = forward.At(s);
                var left = new Numerics.Vector2(-direction.Y, direction.X) * (LaneWidth / 2);
                batch.DrawLine(ToScreen(area, a + left), ToScreen(area, a + left + (direction * 4)), Marking, MathF.Max(1, Scale * 0.15f));
            }
        }

        // Finer dashes between lanes going the same way.
        foreach (var lanes in network.Roads.SelectMany(r => new[] { r.Forward, r.Backward }).Where(l => l.Count > 1))
        {
            for (var i = 1; i < lanes.Count; i++)
            {
                var path = lanes[i].Path;
                for (var s = 2f; s < path.Length - 2; s += 9)
                {
                    var (a, direction) = path.At(s);
                    var left = new Numerics.Vector2(-direction.Y, direction.X) * (LaneWidth / 2);
                    batch.DrawLine(ToScreen(area, a + left), ToScreen(area, a + left + (direction * 2.5f)), Marking * 0.8f, MathF.Max(1, Scale * 0.1f));
                }
            }
        }

        // Stop lines in the colour of their lights.
        foreach (var lane in network.Lanes.Where(l => l.Signal is not null))
        {
            var (end, direction) = lane.Path.At(lane.Length);
            var across = LanePath.RightOf(direction) * (LaneWidth / 2);
            var colour = lane.Signal!.State switch
            {
                SignalState.Green => new Color(80, 210, 100),
                SignalState.Amber => new Color(250, 190, 40),
                _ => new Color(230, 60, 50),
            };
            batch.DrawLine(ToScreen(area, end - across), ToScreen(area, end + across), colour, MathF.Max(2, Scale * 0.6f));
        }

        // Bus stops: a yellow bay marked on the kerb side of their lane, a post beside it.
        foreach (var stop in simulation.Lines.SelectMany(l => l.Stops).Distinct())
        {
            var (at, direction) = stop.Lane.Path.At(stop.At);
            var kerb = LanePath.RightOf(direction) * (LaneWidth * 0.62f);
            var back = direction * 6;
            batch.DrawLine(ToScreen(area, at - back + kerb), ToScreen(area, at + (back * 0.3f) + kerb), new Color(250, 210, 50), MathF.Max(2, Scale * 0.5f));
            batch.FillCircle(ToScreen(area, at + (kerb * 1.5f)), MathF.Max(2, Scale * 0.9f), new Color(250, 210, 50));
        }

        foreach (var vehicle in simulation.Vehicles)
        {
            DrawVehicle(batch, area, vehicle);
        }
    }

    /// <summary>A vehicle's colour: buses yellow, trucks white and grey, vans white, the rest by their number.</summary>
    public static Color ColourOf(Vehicle vehicle)
    {
        ArgumentNullException.ThrowIfNull(vehicle);
        return vehicle.Kind.Name switch
        {
            "bus" => new Color(236, 196, 40),
            "truck" => vehicle.Id % 2 == 0 ? new Color(220, 222, 226) : new Color(120, 124, 132),
            "van" => new Color(240, 240, 244),
            _ => CarColours[vehicle.Id % CarColours.Length],
        };
    }

    private void DrawVehicle(SpriteBatch batch, Rectangle area, Vehicle vehicle)
    {
        var (middle, direction) = vehicle.Pose();
        var at = ToScreen(area, middle);
        var rotation = MathF.Atan2(-direction.Y, direction.X);
        var size = new Vector2(vehicle.Kind.Length, vehicle.Kind.Width) * Scale;
        batch.FillRotated(at + new Vector2(1, 1.5f), size, rotation, new Color(0, 0, 0, 80));
        batch.FillRotated(at, size, rotation, ColourOf(vehicle));

        // A dark windscreen towards the front (a cab, on a truck).
        var screen = ToScreen(area, middle + (direction * (vehicle.Kind.Length * 0.28f)));
        batch.FillRotated(screen, new Vector2(MathF.Max(1, vehicle.Kind.Length * 0.14f * Scale), size.Y * 0.8f), rotation, new Color(40, 50, 64));
    }

    private void Polyline(SpriteBatch batch, Rectangle area, IReadOnlyList<Numerics.Vector2> points, Color colour, float thickness)
    {
        for (var i = 1; i < points.Count; i++)
        {
            var (a, b) = (ToScreen(area, points[i - 1]), ToScreen(area, points[i]));
            batch.DrawLine(a, b, colour, thickness);
            batch.FillCircle(b, thickness / 2, colour);
        }
    }
}
