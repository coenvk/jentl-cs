namespace Jentl.Traffic.Tests;

public class LaneChangeTests
{
    /// <summary>A tractor: slow (5 m/s), but otherwise a car.</summary>
    private static readonly VehicleKind Tractor = VehicleKind.Car with { Name = "tractor", MaxSpeed = 5 };

    /// <summary>A straight kilometre with two lanes each way, at 80.</summary>
    private static (TrafficSimulation Sim, Node West, Node East, Road Road) TwoLanes(bool laneChanges)
    {
        var b = new NetworkBuilder();
        var west = b.Node(0, 0);
        var east = b.Node(1000, 0);
        var road = b.Road(west, east, 2, 80);
        return (new TrafficSimulation(b.Build(), seed: 1) { LaneChanges = laneChanges }, west, east, road);
    }

    /// <summary>Puts a vehicle on a given lane of the road, its route along it.</summary>
    private static Vehicle Put(TrafficSimulation sim, Node from, Node to, VehicleKind kind, Lane lane)
    {
        var vehicle = sim.Spawn(from, to, kind)!;
        sim.PutOn(vehicle, lane);
        return vehicle;
    }

    private static float ArrivalOf(TrafficSimulation sim, Vehicle vehicle, float within)
    {
        float? at = null;
        sim.Arrived += v => at ??= v == vehicle ? sim.Time : null;
        for (var t = 0f; t < within && at is null; t += 1)
        {
            sim.Step(1);
        }

        return at ?? float.PositiveInfinity;
    }

    [Fact]
    public void ACarBehindATractor_Overtakes()
    {
        var (sim, west, east, road) = TwoLanes(laneChanges: true);
        var lane = road.ArrivingAt(east)[^1];
        var tractor = Put(sim, west, east, Tractor, lane);
        sim.Step(3);
        var car = Put(sim, west, east, VehicleKind.Car, lane);

        var arrived = ArrivalOf(sim, car, 300);

        car.LaneChanges.Should().BeGreaterThan(0, "it pulls out past the tractor");
        arrived.Should().BeLessThan(80, "at speed rather than at a tractor's pace (1 km at 5 m/s takes 200 s)");
        tractor.LaneChanges.Should().Be(0, "nothing holds the tractor up");
    }

    [Fact]
    public void WithoutLaneChanges_TheCarStaysBehind()
    {
        var (sim, west, east, road) = TwoLanes(laneChanges: false);
        var lane = road.ArrivingAt(east)[^1];
        Put(sim, west, east, Tractor, lane);
        sim.Step(3);
        var car = Put(sim, west, east, VehicleKind.Car, lane);

        var arrived = ArrivalOf(sim, car, 400);

        car.LaneChanges.Should().Be(0);
        arrived.Should().BeGreaterThan(150, "stuck behind the tractor all the way");
    }

    [Fact]
    public void ACarAlone_KeepsRight()
    {
        var (sim, west, east, road) = TwoLanes(laneChanges: true);
        var lanes = road.ArrivingAt(east);
        var car = Put(sim, west, east, VehicleKind.Car, lanes[0]);

        sim.Step(30);

        car.Lane.Should().Be(lanes[^1], "the right lane, the road being free");
    }

    [Fact]
    public void NoOneChangesIntoAVehicleAlongside()
    {
        var (sim, west, east, road) = TwoLanes(laneChanges: true);
        var lanes = road.ArrivingAt(east);

        // Two cars side by side: neither may move across into the other.
        var right = Put(sim, west, east, VehicleKind.Car, lanes[^1]);
        var left = Put(sim, west, east, VehicleKind.Car, lanes[0]);
        left.Position.Should().Be(right.Position);

        sim.Step(5);

        left.Lane.Should().NotBe(right.Lane);
        Math.Abs(left.Position - right.Position).Should().BeLessThan(left.Kind.Length + left.Kind.MinGap, "they are still alongside");
        left.Lane.Should().Be(lanes[0], "the right lane is taken");
    }

    [Fact]
    public void AChangingVehicle_IsDrawnBetweenTheLanes()
    {
        var (sim, west, east, road) = TwoLanes(laneChanges: true);
        var lanes = road.ArrivingAt(east);
        var car = Put(sim, west, east, VehicleKind.Car, lanes[0]);

        while (car.ChangingFrom is null && sim.Time < 30)
        {
            sim.Step(0.1f);
        }

        car.ChangingFrom.Should().Be(lanes[0]);
        sim.Step(0.5f);
        var (between, _) = car.Pose();
        var (onLeft, _) = lanes[0].Path.At(car.Position - (car.Kind.Length / 2));
        var (onRight, _) = lanes[^1].Path.At(car.Position - (car.Kind.Length / 2));
        var across = Vector(between - onLeft).Length() / Vector(onRight - onLeft).Length();
        across.Should().BeInRange(0.05f, 0.95f, "part way across");
    }

    /// <summary>A two-lane road from the west into a crossroads, one-lane roads north, south and east.</summary>
    private static (TrafficSimulation Sim, Node West, Node North, Node South, Road Road) Crossroads(bool laneChanges)
    {
        var b = new NetworkBuilder();
        var west = b.Node(0, 0);
        var middle = b.Node(400, 0);
        var north = b.Node(400, -400);
        var south = b.Node(400, 400);
        var east = b.Node(800, 0);
        var road = b.Road(west, middle, 2, 50);
        b.Road(middle, north, 1, 50);
        b.Road(middle, south, 1, 50);
        b.Road(middle, east, 1, 50);
        return (new TrafficSimulation(b.Build(), seed: 1) { LaneChanges = laneChanges }, west, north, south, road);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ACarInTheWrongLaneForItsTurn_ChangesOverAndTurns(bool goingNorth)
    {
        var (sim, west, north, south, road) = Crossroads(laneChanges: true);
        var to = goingNorth ? north : south;
        var lanes = road.LeavingFrom(west);
        var right = sim.Route(west, to)![0];
        var wrong = lanes.Single(l => l != right);
        var car = Put(sim, west, to, VehicleKind.Car, wrong);

        car.Route[1].Should().Be(right, "its route changes over to the lane the turn is made from");
        ArrivalOf(sim, car, within: 120).Should().BeLessThan(120, "it gets there");
        car.LaneChanges.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public void AnEdgeWithItsBestLaneFull_StartsTheNextCarInTheOther()
    {
        var (sim, west, north, _, _) = Crossroads(laneChanges: true);
        sim.Spawn(west, north, VehicleKind.Car).Should().NotBeNull();
        var second = sim.Spawn(west, north, VehicleKind.Car);
        second.Should().NotBeNull("the other lane has room");
        second!.Route[1].Should().Be(sim.Route(west, north)![0], "it changes over for its turn");

        var (without, w, n, _, _) = Crossroads(laneChanges: false);
        without.Spawn(w, n, VehicleKind.Car).Should().NotBeNull();
        without.Spawn(w, n, VehicleKind.Car).Should().BeNull("without lane changes it waits for room in its lane");
    }

    [Fact]
    public void ACarWaitingToChange_WaitsAtTheEndOfItsLane_AndDoesNotDriveOffIt()
    {
        var (sim, west, north, _, road) = Crossroads(laneChanges: true);
        var lanes = road.LeavingFrom(west);
        var right = sim.Route(west, north)![0];
        var wrong = lanes.Single(l => l != right);

        // A column standing in the turning lane, all the way along, so there is never a gap.
        var tractor = VehicleKind.Car with { Name = "parked", MaxSpeed = 0.001f };
        for (var i = 0; i < (int)(right.Length / 6) - 1; i++)
        {
            if (sim.Spawn(west, north, tractor) is { } parked)
            {
                sim.PutOn(parked, right);
                parked.Position = right.Length - 1 - (i * 6);
            }
        }

        var car = Put(sim, west, north, VehicleKind.Car, wrong);
        for (var t = 0; t < 900 && car.Lane == wrong; t++)
        {
            sim.Step(0.1f);
        }

        car.Lane.Should().Be(wrong, "there is never a gap beside");
        car.Position.Should().BeInRange(wrong.Length - 15, wrong.Length);
        car.Speed.Should().BeLessThan(0.5f);
    }

    private static System.Numerics.Vector2 Vector(System.Numerics.Vector2 v) => v;
}
