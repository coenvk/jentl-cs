namespace Jentl.Traffic.Tests;

public class BusLineTests
{
    /// <summary>A kilometre of road, one lane each way (or two), west to east.</summary>
    private static (TrafficSimulation Sim, Node West, Node East, Road Road) Street(int lanes = 1)
    {
        var b = new NetworkBuilder();
        var west = b.Node(0, 0);
        var east = b.Node(1000, 0);
        var road = b.Road(west, east, lanes, 50);
        return (new TrafficSimulation(b.Build(), seed: 1) { LaneChanges = lanes > 1 }, west, east, road);
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
    public void ABus_StopsAtItsStop_ForItsDwell_ThenGoesOn()
    {
        var (sim, west, east, road) = Street();
        var stop = BusStop.On(road.LeavingFrom(west)[0], 0.5f, dwell: 15);
        var line = new BusLine("1", west, east, [stop], Headway: 600);
        sim.AddLine(line);

        var standing = 0f;
        Run(sim, 150, () =>
        {
            if (sim.Vehicles.FirstOrDefault(v => v.Line == line) is { Speed: < 0.3f } bus && bus.StopsLeft.Count > 0)
            {
                standing += 0.5f;
                Math.Abs(bus.Position - stop.At).Should().BeLessThan(3, "it stands at its stop");
            }
        });

        standing.Should().BeInRange(14, 20);
        line.Completed.Should().Be(1);
        line.AverageRunTime.Should().BeGreaterThan(1000 / (50 / 3.6f) + 15);
    }

    [Fact]
    public void ABusAtItsStop_HoldsUpTheCarBehind_OnOneLane()
    {
        var (sim, west, east, road) = Street();
        var lane = road.LeavingFrom(west)[0];
        sim.AddLine(new BusLine("1", west, east, [BusStop.On(lane, 0.3f, dwell: 30)], Headway: 600));
        Run(sim, 10);
        var car = sim.Spawn(west, east, VehicleKind.Car)!;

        var passed = false;
        Run(sim, 60, () => passed |= car.Lane == lane && sim.Vehicles.FirstOrDefault(v => v.Line is not null) is { } bus && car.Position > bus.Position);
        passed.Should().BeFalse("there is no way past on one lane");
    }

    [Fact]
    public void OnTwoLanes_TheCarGoesRoundTheBus()
    {
        var (sim, west, east, road) = Street(lanes: 2);
        var right = road.LeavingFrom(west)[^1];
        sim.AddLine(new BusLine("1", west, east, [BusStop.On(right, 0.3f, dwell: 40)], Headway: 600));
        Run(sim, 2);
        var bus = sim.Vehicles.Single(v => v.Line is not null);
        bus.Route[0].Should().Be(right, "it keeps to its stop's lane");
        var car = sim.Spawn(west, east, VehicleKind.Car)!;

        var ahead = false;
        Run(sim, 60, () => ahead |= sim.Vehicles.Contains(bus) && bus.StopsLeft.Count > 0 && (!sim.Vehicles.Contains(car) || car.Position > bus.Position + 5));
        ahead.Should().BeTrue("the car overtakes the bus standing at its stop");
        bus.LaneChanges.Should().Be(0, "a bus with stops to make keeps to its lane");
    }

    [Fact]
    public void Buses_ComeEveryHeadway()
    {
        var (sim, west, east, road) = Street();
        var line = new BusLine("1", west, east, [BusStop.On(road.LeavingFrom(west)[0])], Headway: 120);
        sim.AddLine(line);
        Run(sim, 600);
        line.Completed.Should().BeInRange(3, 5);
    }

    [Fact]
    public void ALineWithoutAWay_IsRefused()
    {
        var (sim, west, east, road) = Street();
        var wrongWay = road.ArrivingAt(west)[0];
        var act = () => sim.AddLine(new BusLine("1", west, east, [BusStop.On(wrongWay)], 300));
        act.Should().Throw<ArgumentException>();
    }
}
