using System.Numerics;

namespace Jentl.Traffic.Tests;

public class TrafficTests
{
    private const float Close = 1e-3f;

    /// <summary>A crossroads at the origin with roads 200 m out to the four edges: north, east, south, west.</summary>
    private static (RoadNetwork Network, Node Centre, Node[] Edges) Crossroads(NodeControl control, int lanes = 1)
    {
        var builder = new NetworkBuilder();
        var centre = builder.Node(0, 0);
        Node[] edges = [builder.Node(0, 200), builder.Node(200, 0), builder.Node(0, -200), builder.Node(-200, 0)];
        var roads = edges.Select(e => builder.Road(centre, e, lanes)).ToArray();
        switch (control)
        {
            case NodeControl.Signals:
                builder.Signals(centre, green: 15, amber: 3, allRed: 2);
                break;
            case NodeControl.Roundabout:
                builder.Roundabout(centre);
                break;
            case NodeControl.Priority:
                builder.Priority(centre, roads[1], roads[3]);
                break;
        }

        return (builder.Build(), centre, edges);
    }

    /// <summary>A small town: lights in the middle, a roundabout east, a crossroads with priority from the right west.</summary>
    internal static RoadNetwork Town()
    {
        var b = new NetworkBuilder();
        var lights = b.Node(0, 0);
        var round = b.Node(220, 0);
        var plain = b.Node(-220, 0);
        Node Edge(float x, float y) => b.Node(x, y);
        b.Road(lights, round, 1, 50);
        b.Road(lights, plain, 1, 50);
        b.Road(lights, Edge(0, 220), 1, 50);
        b.Road(lights, Edge(0, -220), 1, 50);
        b.Road(round, Edge(420, 0), 1, 70);
        b.Road(round, Edge(220, 200), 1, 50);
        b.Road(round, Edge(220, -200), 1, 50);
        b.Road(plain, Edge(-420, 0), 1, 70);
        b.Road(plain, Edge(-220, 200), 1, 50, new Vector2(-260, 100));
        b.Road(plain, Edge(-220, -200), 1, 50);
        b.Signals(lights);
        b.Roundabout(round);
        return b.Build();
    }

    /// <summary>Vehicles on one lane never overlap: each one's front stays behind the rear of the one ahead.</summary>
    private static void NoOverlaps(TrafficSimulation sim)
    {
        foreach (var lane in sim.Network.Lanes)
        {
            var on = sim.On(lane);
            for (var i = 1; i < on.Count; i++)
            {
                var gap = on[i - 1].Position - on[i - 1].Kind.Length - on[i].Position;
                gap.Should().BeGreaterThan(-0.5f, $"{on[i]} ran into {on[i - 1]} on {lane} at {sim.Time:0.0} s");
            }
        }
    }

    /// <summary>Where two paths cross, two vehicles are never on the crossing point together.</summary>
    private static int Collisions(TrafficSimulation sim)
    {
        var count = 0;
        foreach (var lane in sim.Network.Lanes.Where(l => l.Conflicts.Count > 0))
        {
            foreach (var conflict in lane.Conflicts.Where(c => c.At < lane.Length - 0.5f && c.OtherAt < c.Other.Length - 0.5f))
            {
                foreach (var a in sim.On(lane).Where(v => v.Position >= conflict.At && v.Position - v.Kind.Length <= conflict.At))
                {
                    count += sim.On(conflict.Other).Count(b => b.Position >= conflict.OtherAt && b.Position - b.Kind.Length <= conflict.OtherAt);
                    _ = a;
                }
            }
        }

        return count;
    }

    [Fact]
    public void LanePaths_MeasureOffsetAndCross()
    {
        var line = LanePath.Line(Vector2.Zero, new Vector2(10, 0));
        line.Length.Should().Be(10);
        line.At(4).Should().Be((new Vector2(4, 0), Vector2.UnitX));
        line.Offset(2).Start.Should().Be(new Vector2(0, -2), "right of heading east is south");

        var arc = LanePath.Arc(Vector2.Zero, 10, 0, MathF.PI / 2);
        arc.Length.Should().BeApproximately(MathF.PI * 5, 0.05f);
        arc.MinRadius().Should().BeApproximately(10, 0.2f);

        LanePath.Cross(line, LanePath.Line(new Vector2(5, -5), new Vector2(5, 5)), out var a, out var b).Should().BeTrue();
        (a, b).Should().Be((5f, 5f));
    }

    [Fact]
    public void ACrossroads_GetsATurnForEveryWay_AndPriorityFromTheRight()
    {
        var (network, centre, edges) = Crossroads(NodeControl.Uncontrolled);

        var turns = network.Lanes.Where(l => l.Node == centre).ToList();
        turns.Should().HaveCount(12, "from each of four roads: left, straight on and right");
        turns.Count(t => t.Turn == TurnKind.Left).Should().Be(4);

        // Northbound straight on (from the south) and westbound straight on (from the east) cross; the one from the right goes first.
        var north = turns.Single(t => t.Turn == TurnKind.Straight && t.Path.StartDirection.Y > 0.9f);
        var west = turns.Single(t => t.Turn == TurnKind.Straight && t.Path.StartDirection.X < -0.9f);
        north.Conflicts.Single(c => c.Other == west).OtherFirst.Should().BeTrue();
        west.Conflicts.Single(c => c.Other == north).OtherFirst.Should().BeFalse();

        // A left turn gives way to oncoming traffic going straight on.
        var leftFromSouth = turns.Single(t => t.Turn == TurnKind.Left && t.Path.StartDirection.Y > 0.9f);
        var southbound = turns.Single(t => t.Turn == TurnKind.Straight && t.Path.StartDirection.Y < -0.9f);
        leftFromSouth.Conflicts.Single(c => c.Other == southbound).OtherFirst.Should().BeTrue();
        edges.Should().OnlyContain(e => e.IsEdge);
    }

    [Fact]
    public void Lights_SplitIntoPhasesByAxis()
    {
        var (network, centre, _) = Crossroads(NodeControl.Signals);

        centre.Signals!.Phases.Should().Be(2);
        var arriving = centre.Roads.Select(r => r.ArrivingAt(centre)[0]).ToList();
        arriving[0].Signal.Should().BeSameAs(arriving[2].Signal, "north and south go together");
        arriving[1].Signal.Should().BeSameAs(arriving[3].Signal);
        arriving[0].Signal.Should().NotBeSameAs(arriving[1].Signal);
        network.Signals.Should().ContainSingle();
    }

    [Fact]
    public void ARoundabout_IsARing_ThatEntriesGiveWayTo()
    {
        var (network, centre, _) = Crossroads(NodeControl.Roundabout);

        var ring = network.Lanes.Where(l => l.Kind == LaneKind.Ring).ToList();
        ring.Should().HaveCount(8, "an entry and an exit point for each of four roads");
        ring.Should().OnlyContain(l => l.Next.Any(n => n.Kind == LaneKind.Ring));
        var entries = network.Lanes.Where(l => l.Node == centre && l.Kind == LaneKind.Connector && l.Next[0].Kind == LaneKind.Ring).ToList();
        entries.Should().HaveCount(4);
        entries.Should().OnlyContain(e => e.Conflicts.Any(c => c.Other.Kind == LaneKind.Ring && c.OtherFirst));
    }

    [Fact]
    public void OnAnEmptyRoad_ACar_ReachesTheLimit_AndArrives()
    {
        var b = new NetworkBuilder();
        var (start, end) = (b.Node(0, 0), b.Node(1000, 0));
        b.Road(start, end, 1, 50);
        var sim = new TrafficSimulation(b.Build(), seed: 1);
        var car = sim.Spawn(start, end, VehicleKind.Car)!;
        var top = 0f;
        sim.Arrived += v => v.Should().Be(car);

        for (var t = 0; t < 1200 && sim.Completed == 0; t++)
        {
            sim.Step(0.1f);
            top = MathF.Max(top, car.Speed);
        }

        sim.Completed.Should().Be(1);
        top.Should().BeInRange(50 / 3.6f * 0.88f, 50 / 3.6f * 1.11f);
        sim.Stats().AverageDelay.Should().BeLessThan(8, "a little time to get up to speed");
    }

    [Fact]
    public void AQueue_KeepsItsDistance()
    {
        var b = new NetworkBuilder();
        var (start, end) = (b.Node(0, 0), b.Node(800, 0));
        b.Road(start, end, 1, 70);
        var sim = new TrafficSimulation(b.Build(), seed: 2);
        sim.SetDemand(start, 1500);

        for (var t = 0; t < 3000; t++)
        {
            sim.Step(0.1f);
            NoOverlaps(sim);
        }

        sim.Completed.Should().BeGreaterThan(30);
    }

    [Fact]
    public void ARedLight_StopsTheCar_AndGreenLetsItGo()
    {
        var (network, centre, edges) = Crossroads(NodeControl.Signals);
        var sim = new TrafficSimulation(network, seed: 3);

        // Wait for north-south to turn red, then send a car north from the south edge.
        var fromSouth = centre.Roads.Single(r => r.Other(centre) == edges[2]).ArrivingAt(centre)[0];
        while (fromSouth.Signal!.State != SignalState.Red)
        {
            sim.Step(0.1f);
        }

        var car = sim.Spawn(edges[2], edges[0], VehicleKind.Car)!;
        var stoppedAtLine = false;
        for (var t = 0; t < 1200 && sim.Completed == 0; t++)
        {
            sim.Step(0.1f);
            if (car.Lane == fromSouth && fromSouth.Signal.State == SignalState.Red)
            {
                car.Position.Should().BeLessThanOrEqualTo(fromSouth.Length + 0.05f, "never over the line at red");
                stoppedAtLine |= car.Speed < 0.1f && fromSouth.Length - car.Position < 3;
            }
        }

        stoppedAtLine.Should().BeTrue();
        sim.Completed.Should().Be(1, "it goes on green");
    }

    [Fact]
    public void AtAPlainCrossroads_TheCarFromTheRight_GoesFirst()
    {
        var (network, centre, edges) = Crossroads(NodeControl.Uncontrolled);
        var sim = new TrafficSimulation(network, seed: 4);

        // Both 150 m out, at the same speed: one north from the south edge, one west from the east edge.
        var northbound = sim.Spawn(edges[2], edges[0], VehicleKind.Car)!;
        var westbound = sim.Spawn(edges[1], edges[3], VehicleKind.Car)!;
        int? northIn = null, westIn = null;
        for (var t = 0; t < 1200 && sim.Completed < 2; t++)
        {
            sim.Step(0.1f);
            northIn ??= northbound.Lane.Node == centre ? t : null;
            westIn ??= westbound.Lane.Node == centre ? t : null;
            Collisions(sim).Should().Be(0);
        }

        sim.Completed.Should().Be(2);
        westIn.Should().BeLessThan(northIn!.Value, "traffic from the right has priority");
    }

    [Theory]
    [InlineData(NodeControl.Uncontrolled)]
    [InlineData(NodeControl.Priority)]
    [InlineData(NodeControl.Signals)]
    [InlineData(NodeControl.Roundabout)]
    public void EveryKindOfJunction_KeepsTrafficMoving_WithoutCrashes(NodeControl control)
    {
        var (network, _, _) = Crossroads(control);
        var sim = new TrafficSimulation(network, seed: 5);
        sim.SetDemand(250);
        var crashes = 0;

        for (var t = 0; t < 600 * 10; t++)
        {
            sim.Step(0.1f);
            NoOverlaps(sim);
            crashes += Collisions(sim);
        }

        crashes.Should().Be(0);
        var stats = sim.Stats();
        stats.Completed.Should().BeGreaterThan(250 * 4 * 600 / 3600 / 2, $"{control}: most of the demand gets through");
        stats.Active.Should().BeLessThan(60, $"{control}: no gridlock");
    }

    [Fact]
    public void ATown_RunsForHalfAnHour_AndTheSameSeedRunsTheSame()
    {
        static (TrafficStats Stats, string Positions) Run(int seed)
        {
            var sim = new TrafficSimulation(Town(), seed);
            sim.SetDemand(120);
            for (var t = 0; t < 1800 * 10; t++)
            {
                sim.Step(0.1f);
                if (t % 50 == 0)
                {
                    NoOverlaps(sim);
                    Collisions(sim).Should().Be(0);
                }
            }

            return (sim.Stats(), string.Join(";", sim.Vehicles.Select(v => $"{v.Id}:{v.Lane.Id}:{v.Position:0.00}")));
        }

        var (stats, positions) = Run(6);

        stats.Completed.Should().BeGreaterThan(380);
        stats.Active.Should().BeLessThan(90, "the town doesn't lock up");
        stats.Throughput.Should().BeGreaterThan(750);
        stats.AverageStops.Should().BeGreaterThan(0);
        Run(6).Positions.Should().Be(positions);
    }

    [Fact]
    public void EveryEdge_CanReachEveryOther()
    {
        var network = Town();
        var sim = new TrafficSimulation(network, seed: 1);

        foreach (var from in network.Entries)
        {
            foreach (var to in network.Entries.Where(e => e != from))
            {
                sim.Route(from, to).Should().NotBeNull($"{from} to {to}");
            }
        }
    }
}
