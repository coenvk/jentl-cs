using System.Numerics;

namespace Jentl.Ai.Tests;

public class VehicleTests
{
    private const float Step = 1f / 60;

    [Fact]
    public void Seek_HeadsForTheTarget_AtNoMoreThanTopSpeed()
    {
        var v = new Vehicle(Vector2.Zero, maxSpeed: 100, maxForce: 400);

        Run(v, () => v.Seek(new Vector2(1000, 0)), 2);

        v.Velocity.X.Should().BeApproximately(100, 0.5f);
        v.Velocity.Length().Should().BeLessThanOrEqualTo(100.01f);
        v.Heading.Should().Be(Vector2.UnitX);
    }

    [Fact]
    public void Arrive_StopsOnTheTarget()
    {
        var v = new Vehicle(Vector2.Zero, maxSpeed: 100, maxForce: 400);
        var target = new Vector2(150, 50);

        Run(v, () => v.Arrive(target, slowingRadius: 60), 6);

        Vector2.Distance(v.Position, target).Should().BeLessThan(1);
        v.Velocity.Length().Should().BeLessThan(2);
    }

    [Fact]
    public void Flee_RunsOnlyWhenTheThreatIsClose()
    {
        var v = new Vehicle(Vector2.Zero, maxSpeed: 100, maxForce: 400);

        v.Flee(new Vector2(500, 0), panicDistance: 100).Should().Be(Vector2.Zero);
        v.Flee(new Vector2(50, 0), panicDistance: 100).X.Should().BeLessThan(0);
    }

    [Fact]
    public void Pursue_LeadsAMovingTarget()
    {
        var v = new Vehicle(Vector2.Zero, maxSpeed: 100, maxForce: 400);

        var lead = v.Pursue(new Vector2(100, 0), targetVelocity: new Vector2(0, 100));

        lead.Y.Should().BeGreaterThan(0, "it aims where the target will be");
    }

    [Fact]
    public void Wander_CurvesAboutWithoutStopping()
    {
        var v = new Vehicle(Vector2.Zero, maxSpeed: 50, maxForce: 200);
        var random = new Random(1);

        Run(v, () => v.Wander(random), 5);

        v.Velocity.Length().Should().BeGreaterThan(20);
        v.Position.Should().NotBe(Vector2.Zero);
    }

    [Fact]
    public void Separation_PushesCrowdedNeighboursApart()
    {
        var flock = Enumerable.Range(0, 6).Select(i => new Vehicle(new Vector2(i * 0.5f, i % 2), maxSpeed: 50, maxForce: 300)).ToList();

        for (var t = 0; t < 180; t++)
        {
            var forces = flock.Select(v => v.Separation(flock, radius: 20)).ToList();
            for (var i = 0; i < flock.Count; i++)
            {
                flock[i].Move(forces[i], Step);
            }
        }

        var closest = flock.SelectMany(a => flock.Where(b => b != a).Select(b => Vector2.Distance(a.Position, b.Position))).Min();
        closest.Should().BeGreaterThan(10);
    }

    [Fact]
    public void AFlock_EndsUpHeadingTheSameWay()
    {
        var random = new Random(4);
        var flock = Enumerable.Range(0, 12).Select(_ => new Vehicle(new Vector2(random.Next(100), random.Next(100)), maxSpeed: 60, maxForce: 120)
        {
            Velocity = new Vector2(random.Next(-60, 60), random.Next(-60, 60)),
        }).ToList();

        for (var t = 0; t < 1800; t++)
        {
            var forces = flock.Select(v => v.Flock(flock, radius: 200)).ToList();
            for (var i = 0; i < flock.Count; i++)
            {
                flock[i].Move(forces[i], Step);
            }
        }

        var average = flock.Aggregate(Vector2.Zero, (sum, v) => sum + v.Heading) / flock.Count;
        average.Length().Should().BeGreaterThan(0.9f, "their headings agree");
    }

    [Fact]
    public void Avoid_SwervesAroundAnObstacleAhead()
    {
        var v = new Vehicle(Vector2.Zero, maxSpeed: 100, maxForce: 300) { Velocity = new Vector2(100, 0) };
        (Vector2, float)[] rocks = [(new Vector2(60, 3), 10)];

        v.Avoid(rocks, lookAhead: 80, radius: 4).Y.Should().BeLessThan(0, "the rock is a little to the right, so it swerves left");
        v.Avoid([(new Vector2(-60, 0), 10f)], lookAhead: 80).Should().Be(Vector2.Zero, "behind it doesn't matter");

        var passed = true;
        for (var t = 0; t < 120; t++)
        {
            v.Move(v.Seek(new Vector2(200, 0)) + v.Avoid(rocks, lookAhead: 80, radius: 4) * 3, Step);
            passed &= Vector2.Distance(v.Position, new Vector2(60, 3)) > 10;
        }

        passed.Should().BeTrue("it never ran into the rock");
    }

    private static void Run(Vehicle vehicle, Func<Vector2> steer, float seconds)
    {
        for (var t = 0f; t < seconds; t += Step)
        {
            vehicle.Move(steer(), Step);
        }
    }
}
