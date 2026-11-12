using System.Numerics;

namespace Jentl.Traffic.Tests;

public class AdaptiveTests
{
    private static (RoadNetwork Network, Node[] Edges) Lights(bool actuated)
    {
        var b = new NetworkBuilder();
        var centre = b.Node(0, 0);
        Node[] edges = [b.Node(0, 200), b.Node(200, 0), b.Node(0, -200), b.Node(-200, 0)];
        foreach (var edge in edges)
        {
            b.Road(centre, edge);
        }

        b.Signals(centre, green: 25, amber: 3, allRed: 2, actuated: actuated);
        return (b.Build(), edges);
    }

    [Fact]
    public void ActuatedLights_RestOnGreen_WhileNoOneElseWaits_AndChangeWhenSomeoneDoes()
    {
        var (network, edges) = Lights(actuated: true);
        var sim = new TrafficSimulation(network, seed: 1);
        var plan = network.Nodes.Single(n => n.Signals is not null).Signals!;

        sim.Step(60);
        plan.Phase.Should().Be(0, "no one is waiting anywhere, so the first phase keeps green");

        // A car from the west: the other axis. The lights turn for it within the least green, amber and all red.
        var car = sim.Spawn(edges[3], edges[1], VehicleKind.Car)!;
        var arrived = false;
        sim.Arrived += v => arrived |= v == car;
        sim.Step(60);

        arrived.Should().BeTrue();
        car.Stops.Should().BeLessThanOrEqualTo(1);
    }

    [Fact]
    public void WithTrafficFromOneSideOnly_ActuatedLights_DelayLessThanAFixedPlan()
    {
        float Delay(bool actuated)
        {
            var (network, edges) = Lights(actuated);
            var sim = new TrafficSimulation(network, seed: 3);
            sim.SetDemand(edges[0], 300);
            sim.SetDemand(edges[2], 300);
            sim.SetDemand(edges[1], 30);
            sim.Step(1200);
            return sim.Stats().AverageDelay;
        }

        Delay(actuated: true).Should().BeLessThan(Delay(actuated: false) * 0.8f);
    }

    [Fact]
    public void AvoidingJams_SendsTrafficRoundABlockedRoad()
    {
        // Two ways from west to east: straight through a short road, or round by a longer one.
        var b = new NetworkBuilder();
        var west = b.Node(-300, 0);
        var a = b.Node(-100, 0);
        var c = b.Node(100, 0);
        var east = b.Node(300, 0);
        b.Road(west, a);
        var shortcut = b.Road(a, c, 1, 50);
        b.Road(a, c, 1, 50, new Vector2(-50, 120), new Vector2(50, 120));
        b.Road(c, east);
        var network = b.Build();
        var sim = new TrafficSimulation(network, seed: 1) { AvoidJams = true };

        sim.Route(west, east)!.Should().Contain(l => l.Road == shortcut, "it is shorter while the roads are empty");

        // A broken-down truck on the shortcut: vehicles pile up behind it, and the way round becomes quicker.
        var truck = sim.Spawn(a, c, VehicleKind.Truck);
        for (var i = 0; i < 400 && sim.TravelTime(shortcut.Forward[0]) < 60; i++)
        {
            sim.Step(1);
            if (truck is not null && truck.Lane.Road == shortcut)
            {
                // Keep it standing in the middle of the road.
                truck.Speed = 0;
            }
        }

        sim.Route(west, east)!.Should().NotContain(l => l.Road == shortcut);
    }
}
