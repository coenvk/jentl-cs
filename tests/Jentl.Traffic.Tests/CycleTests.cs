using System.Numerics;

namespace Jentl.Traffic.Tests;

public class CycleTests
{
    /// <summary>A crossroads at the origin, roads 200 m out to north, east, south and west, every one with cycle paths.</summary>
    private static (RoadNetwork Network, Node Centre, Node[] Edges, Road[] Roads) Crossroads(NodeControl control = NodeControl.Uncontrolled)
    {
        var b = new NetworkBuilder();
        var centre = b.Node(0, 0);
        Node[] edges = [b.Node(0, 200), b.Node(200, 0), b.Node(0, -200), b.Node(-200, 0)];
        var roads = edges.Select(e => b.Road(e, centre)).ToArray();
        b.CyclePaths(roads);
        switch (control)
        {
            case NodeControl.Signals:
                b.Signals(centre, green: 15);
                break;
            case NodeControl.Roundabout:
                b.Roundabout(centre);
                break;
        }

        return (b.Build(), centre, edges, roads);
    }

    private static void Run(TrafficSimulation sim, float seconds, Action? each = null)
    {
        for (var t = 0f; t < seconds; t += 0.5f)
        {
            sim.Step(0.5f);
            each?.Invoke();
        }
    }

    [Fact]
    public void ARoadWithCyclePaths_HasOneEachSide_OutsideItsLanes_GoingWithTheTraffic()
    {
        var b = new NetworkBuilder();
        var road = b.Road(b.Node(0, 0), b.Node(300, 0), 1, 50);
        b.CyclePaths(road);
        b.Build();

        road.CycleForward!.ForBikes.Should().BeTrue();
        road.CycleBackward!.ForBikes.Should().BeTrue();
        road.Forward.Concat(road.Backward).Should().NotContain(l => l.ForBikes);

        // Driving on the right: the eastbound path south of the eastbound lane, the westbound one north of the other.
        road.CycleForward.Path.Start.Y.Should().BeLessThan(road.Forward[0].Path.Start.Y - 3);
        road.CycleBackward.Path.Start.Y.Should().BeGreaterThan(road.Backward[0].Path.Start.Y + 3);
        road.CycleForward.Path.StartDirection.X.Should().BeApproximately(1, 1e-3f);
        road.CycleBackward.Path.StartDirection.X.Should().BeApproximately(-1, 1e-3f);
    }

    [Fact]
    public void ACyclist_RidesOnlyCyclePaths_AndArrives()
    {
        var (network, _, edges, _) = Crossroads();
        var sim = new TrafficSimulation(network, seed: 1);
        var bike = sim.Spawn(edges[3], edges[1], VehicleKind.Bicycle)!;
        var car = sim.Spawn(edges[0], edges[2], VehicleKind.Car)!;

        bike.Route.Should().OnlyContain(l => l.ForBikes);
        car.Route.Should().NotContain(l => l.ForBikes);
        var arrived = new List<Vehicle>();
        sim.Arrived += arrived.Add;
        Run(sim, 120);

        arrived.Should().Contain([bike, car]);
        bike.Speed.Should().BeLessThanOrEqualTo(VehicleKind.Bicycle.MaxSpeed + 0.01f);
    }

    [Fact]
    public void WithoutCyclePaths_ThereIsNoWayForABicycle()
    {
        var b = new NetworkBuilder();
        var (west, east) = (b.Node(0, 0), b.Node(300, 0));
        b.Road(west, east);
        var sim = new TrafficSimulation(b.Build(), seed: 1);

        sim.Spawn(west, east, VehicleKind.Bicycle).Should().BeNull();
        sim.CycleRoute(west, east).Should().BeNull();
    }

    [Fact]
    public void ACarTurningRight_GivesWayToACyclistGoingStraightOn_BesideIt()
    {
        var (network, centre, _, roads) = Crossroads();
        var west = roads[3];
        var carRight = network.Lanes.Single(l => l.Node == centre && !l.ForBikes && l.Turn == TurnKind.Right && l.Previous[0].Road == west);
        var bikeStraight = network.Lanes.Single(l => l.Node == centre && l.ForBikes && l.Turn == TurnKind.Straight && l.Next[0].Road is not null && l.Previous[0].Road == west);

        carRight.Conflicts.Should().ContainSingle(c => c.Other == bikeStraight).Which.OtherFirst.Should().BeTrue();
        bikeStraight.Conflicts.Should().ContainSingle(c => c.Other == carRight).Which.OtherFirst.Should().BeFalse();
    }

    [Fact]
    public void ACyclistFromTheRight_GoesFirst_AtAnUncontrolledCrossroads()
    {
        var (network, centre, _, roads) = Crossroads();

        // A car from the west going straight on (east); a cyclist from the south (on its right) going straight on north.
        var car = network.Lanes.Single(l => l.Node == centre && !l.ForBikes && l.Turn == TurnKind.Straight && l.Next[0].Road is not null && l.Previous[0].Road == roads[3]);
        var bike = network.Lanes.Single(l => l.Node == centre && l.ForBikes && l.Turn == TurnKind.Straight && l.Next[0].Road is not null && l.Previous[0].Road == roads[2]);

        car.Conflicts.Should().ContainSingle(c => c.Other == bike).Which.OtherFirst.Should().BeTrue();
    }

    [Fact]
    public void ARoundabout_GetsACyclistsRingOutsideItsOwn_WhichCarsGiveWayTo()
    {
        var (network, centre, _, _) = Crossroads(NodeControl.Roundabout);
        var bikeRing = network.Lanes.Where(l => l.Node == centre && l.ForBikes && l.Kind == LaneKind.Ring).ToList();
        var carRing = network.Lanes.Where(l => l.Node == centre && !l.ForBikes && l.Kind == LaneKind.Ring).ToList();

        bikeRing.Should().HaveCount(8);
        Vector2.Distance(bikeRing[0].Path.Start, centre.Position).Should().BeGreaterThan(Vector2.Distance(carRing[0].Path.Start, centre.Position) + 4);

        // Every car going on or off the ring crosses the cyclists' ring, and gives way to it.
        var carLinks = network.Lanes.Where(l => l.Node == centre && !l.ForBikes && l.Kind == LaneKind.Connector).ToList();
        carLinks.Should().HaveCount(8);
        carLinks.Should().OnlyContain(l => l.Conflicts.Any(c => c.Other.ForBikes && c.Other.Kind == LaneKind.Ring && c.OtherFirst));
    }

    [Fact]
    public void ACyclistTurningLeft_GoesStraightAcross_WaitsAtTheFarCorner_ThenCrossesTheOtherRoad()
    {
        var (network, centre, edges, roads) = Crossroads();
        var sim = new TrafficSimulation(network, seed: 1);

        // From the west to the north: across the south arm, then up the east side across the east arm.
        var route = sim.CycleRoute(edges[3], edges[0])!;
        route.Should().HaveCount(5);
        var (first, wait, second) = (route[1], route[2], route[3]);
        first.Turn.Should().Be(TurnKind.Straight);
        second.Turn.Should().Be(TurnKind.Straight);
        wait.IsJunction.Should().BeFalse("they stop at its end");
        wait.Path.StartDirection.Y.Should().BeApproximately(1, 1e-3f, "they wait facing north");
        wait.Path.End.X.Should().BeGreaterThan(roads[0].PathLeaving(centre)!.Path.Start.X, "beside the path north, not on it");
        wait.Path.End.Y.Should().BeLessThan(0, "on the south side of the east arm");
        wait.Length.Should().BeGreaterThan(VehicleKind.Bicycle.Length + 2, "a bicycle waiting there is clear of the crossing behind it");
        route[^1].Should().BeSameAs(roads[0].PathLeaving(centre));

        var bike = sim.Spawn(edges[3], edges[0], VehicleKind.Bicycle)!;
        var arrived = false;
        sim.Arrived += v => arrived |= v == bike;
        Run(sim, 120);
        arrived.Should().BeTrue();
    }

    [Fact]
    public void AtLights_TheCornerWaitsForTheGreenOfTheRoadItCrosses()
    {
        var (network, centre, _, roads) = Crossroads(NodeControl.Signals);
        var corners = network.Lanes.Where(l => l.ForBikes && l.Road is null && !l.IsJunction).ToList();

        corners.Should().HaveCount(4);
        foreach (var wait in corners)
        {
            // Facing north or south, it goes with the north and south roads, whose lanes come in on the north–south axis.
            var northSouth = MathF.Abs(wait.Path.StartDirection.Y) > 0.7f;
            var road = northSouth ? roads[0] : roads[1];
            wait.Signal.Should().BeSameAs(road.ArrivingAt(centre)[0].Signal);
        }
    }

    [Fact]
    public void ACyclePath_StopsAtTheLights_OfItsRoad()
    {
        var (network, centre, _, roads) = Crossroads(NodeControl.Signals);

        foreach (var road in roads)
        {
            road.PathArriving(centre)!.Signal.Should().BeSameAs(road.ArrivingAt(centre)[0].Signal);
        }
    }

    [Theory]
    [InlineData(NodeControl.Uncontrolled)]
    [InlineData(NodeControl.Signals)]
    [InlineData(NodeControl.Roundabout)]
    public void CyclistsAndCars_ShareATown_EachOnTheirOwnLanes_AndAllGetThrough(NodeControl control)
    {
        var (network, _, _, _) = Crossroads(control);
        var sim = new TrafficSimulation(network, seed: 3);
        sim.SetDemand(100);
        sim.SetCycling(100);
        var (bikes, cars) = (0, 0);
        sim.Arrived += v => _ = v.Kind.Cyclist ? bikes++ : cars++;

        Run(sim, 600, () => sim.Vehicles.Where(v => v.Lane.ForBikes != v.Kind.Cyclist).Should().BeEmpty());

        // About 67 of each set out in ten minutes, the last minute or so of them still on their way.
        bikes.Should().BeGreaterThan(45);
        cars.Should().BeGreaterThan(40);
        sim.Stats().Queued.Should().BeLessThan(5, "nobody is stuck");
        sim.Stats().Standing.Should().BeLessThan(12);
    }
}
