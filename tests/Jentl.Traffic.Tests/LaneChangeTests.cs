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

    private static System.Numerics.Vector2 Vector(System.Numerics.Vector2 v) => v;
}
