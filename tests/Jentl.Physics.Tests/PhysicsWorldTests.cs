using System.Numerics;

namespace Jentl.Physics.Tests;

public class PhysicsWorldTests
{
    private static readonly TimeSpan Second = TimeSpan.FromSeconds(1);

    private readonly PhysicsWorld _world = new() { Gravity = new Vector2(0, 500) };

    [Fact]
    public void Update_RunsWholeSteps_AndKeepsTheRest()
    {
        _world.Update(TimeSpan.FromMilliseconds(20)).Should().Be(2);
        _world.Alpha.Should().BeApproximately(0.4f, 0.01f);
        _world.Update(TimeSpan.FromMilliseconds(5)).Should().Be(1, "the leftover 3⅓ ms and these 5 ms make a step");
    }

    [Fact]
    public void ALongStall_SkipsTimeInsteadOfFreezing()
    {
        _world.Update(TimeSpan.FromSeconds(5)).Should().Be(_world.MaxStepsPerUpdate);
        _world.Alpha.Should().BeLessThanOrEqualTo(1);
    }

    [Fact]
    public void Gravity_PullsDynamicBodies_Only()
    {
        var ball = _world.Add(new Body(new Circle(5), Vector2.Zero));
        var wall = _world.Add(new Body(new Box(10, 10), new Vector2(100, 0), BodyType.Static));
        var floater = _world.Add(new Body(new Circle(5), new Vector2(200, 0)) { GravityScale = 0 });

        Run(Second);

        ball.Position.Y.Should().BeApproximately(250, 5, "half of g t²");
        wall.Position.Should().Be(new Vector2(100, 0));
        floater.Position.Should().Be(new Vector2(200, 0));
    }

    [Fact]
    public void ABodyComesToRestOnTheFloor_WithoutSinking()
    {
        var floor = _world.Add(new Body(new Box(400, 20), new Vector2(0, 110), BodyType.Static));
        var crate = _world.Add(new Body(new Box(20, 20), Vector2.Zero));

        for (var i = 0; i < 4; i++)
        {
            Run(Second);
        }

        crate.Position.Y.Should().BeApproximately(90, 0.5f, "its bottom rests on the floor's top at 100");
        crate.Velocity.Length().Should().BeLessThan(1);
        floor.Position.Y.Should().Be(110);
    }

    [Fact]
    public void ACircleStacksOnABox()
    {
        _world.Add(new Body(new Box(400, 20), new Vector2(0, 110), BodyType.Static));
        _world.Add(new Body(new Box(40, 40), new Vector2(0, 70)));
        var ball = _world.Add(new Body(new Circle(10), new Vector2(0, 0)));

        for (var i = 0; i < 4; i++)
        {
            Run(Second);
        }

        ball.Position.Y.Should().BeApproximately(50, 1, "the box settles on the floor (top 100), so its top is at 60");
        ball.Position.X.Should().BeApproximately(0, 0.5f);
    }

    [Fact]
    public void Restitution_Bounces()
    {
        _world.Gravity = Vector2.Zero;
        _world.Add(new Body(new Box(20, 200), new Vector2(100, 0), BodyType.Static));
        var ball = _world.Add(new Body(new Circle(5), Vector2.Zero) { Velocity = new Vector2(300, 0), Restitution = 1 });

        Run(Second / 2);

        ball.Velocity.X.Should().BeApproximately(-300, 1, "a perfectly bouncy ball leaves as fast as it came");
        ball.Position.X.Should().BeLessThan(85);
    }

    [Fact]
    public void AnEqualHeadOnCollision_SwapsVelocities_AndKeepsMomentum()
    {
        _world.Gravity = Vector2.Zero;
        var a = _world.Add(new Body(new Circle(10), new Vector2(-50, 0)) { Velocity = new Vector2(100, 0), Restitution = 1 });
        var b = _world.Add(new Body(new Circle(10), new Vector2(50, 0)) { Restitution = 1 });

        Run(Second);

        a.Velocity.X.Should().BeApproximately(0, 1);
        b.Velocity.X.Should().BeApproximately(100, 1);
    }

    [Fact]
    public void AHeavyBody_BarelyNoticesALightOne()
    {
        _world.Gravity = Vector2.Zero;
        var heavy = _world.Add(new Body(new Circle(10), new Vector2(50, 0)) { Mass = 100 });
        _world.Add(new Body(new Circle(10), new Vector2(-50, 0)) { Velocity = new Vector2(200, 0), Mass = 1 });

        Run(Second);

        heavy.Velocity.X.Should().BeInRange(0.5f, 5);
    }

    [Fact]
    public void Friction_StopsASlidingBody()
    {
        _world.Add(new Body(new Box(2000, 20), new Vector2(0, 20), BodyType.Static) { Friction = 0.8f });
        var sled = _world.Add(new Body(new Box(20, 20), new Vector2(0, -0.1f)) { Velocity = new Vector2(200, 0), Friction = 0.8f });
        var puck = _world.Add(new Body(new Box(20, 20), new Vector2(-500, -0.1f)) { Velocity = new Vector2(200, 0), Friction = 0 });

        Run(Second * 2);

        sled.Velocity.X.Should().BeApproximately(0, 1);
        puck.Velocity.X.Should().BeGreaterThan(150, "it is frictionless on its side");
    }

    [Fact]
    public void AKinematicBody_PushesWithoutBeingPushed()
    {
        _world.Gravity = Vector2.Zero;
        var paddle = _world.Add(new Body(new Box(100, 10), new Vector2(0, 0), BodyType.Kinematic) { Velocity = new Vector2(0, -100) });
        var ball = _world.Add(new Body(new Circle(5), new Vector2(0, -30)));

        Run(Second / 2);

        paddle.Velocity.Should().Be(new Vector2(0, -100));
        paddle.Position.Y.Should().BeApproximately(-50, 0.5f);
        ball.Position.Y.Should().BeLessThan(-55);
    }

    [Fact]
    public void Sensors_ReportContacts_ButLetBodiesThrough()
    {
        var zone = _world.Add(new Body(new Box(200, 20), new Vector2(0, 100), BodyType.Static) { IsSensor = true });
        var ball = _world.Add(new Body(new Circle(5), Vector2.Zero));
        var began = new List<Contact>();
        _world.ContactBegan += began.Add;

        Run(Second);

        ball.Position.Y.Should().BeGreaterThan(200, "it fell straight through");
        began.Should().ContainSingle(c => c.Other(ball) == zone);
    }

    [Fact]
    public void Layers_DecideWhatCollides()
    {
        _world.Add(new Body(new Box(200, 20), new Vector2(0, 100), BodyType.Static) { Layer = 0b01 });
        var ghost = _world.Add(new Body(new Circle(5), Vector2.Zero) { Mask = 0b10 });
        var solid = _world.Add(new Body(new Circle(5), new Vector2(50, 0)) { Mask = 0b01 });

        Run(Second);

        ghost.Position.Y.Should().BeGreaterThan(200);
        solid.Position.Y.Should().BeLessThan(90);
    }

    [Fact]
    public void ContactEvents_BeginOnce_AndEnd()
    {
        _world.Gravity = Vector2.Zero;
        var wall = _world.Add(new Body(new Box(20, 200), new Vector2(100, 0), BodyType.Static));
        var ball = _world.Add(new Body(new Circle(5), Vector2.Zero) { Velocity = new Vector2(300, 0), Restitution = 1 });
        var began = 0;
        var ended = new List<(Body, Body)>();
        _world.ContactBegan += c =>
        {
            began++;
            c.NormalFrom(ball).X.Should().BePositive("the normal points from the ball to the wall");
        };
        _world.ContactEnded += (a, b) => ended.Add((a, b));

        Run(Second);

        began.Should().Be(1);
        ended.Should().ContainSingle().Which.Should().Be((wall, ball));
    }

    [Fact]
    public void AFastBall_DoesNotTunnelThroughAThinBrick()
    {
        _world.Gravity = Vector2.Zero;
        _world.Add(new Body(new Box(60, 16), new Vector2(0, -100), BodyType.Static));
        var ball = _world.Add(new Body(new Circle(6), Vector2.Zero) { Velocity = new Vector2(0, -900), Restitution = 1 });

        Run(Second / 2);

        ball.Velocity.Y.Should().BePositive("it bounced off");
    }

    [Fact]
    public void Raycast_FindsTheNearestBody()
    {
        var near = _world.Add(new Body(new Box(20, 20), new Vector2(50, 0), BodyType.Static));
        _world.Add(new Body(new Circle(10), new Vector2(150, 0), BodyType.Static));
        _world.Add(new Body(new Box(20, 20), new Vector2(20, 0), BodyType.Static) { IsSensor = true });

        var hit = _world.Raycast(Vector2.Zero, Vector2.UnitX, 500);

        hit.Should().NotBeNull();
        hit!.Value.Body.Should().Be(near);
        hit.Value.Distance.Should().BeApproximately(40, 0.001f);
        hit.Value.Normal.Should().Be(-Vector2.UnitX);
    }

    [Fact]
    public void Raycast_HitsCircles_AndRespectsItsReach()
    {
        var ball = _world.Add(new Body(new Circle(10), new Vector2(0, 100), BodyType.Static));

        var hit = _world.Raycast(Vector2.Zero, new Vector2(0, 2), 500);

        hit!.Value.Body.Should().Be(ball);
        hit.Value.Point.Y.Should().BeApproximately(90, 0.001f);
        hit.Value.Normal.Y.Should().BeApproximately(-1, 0.001f);
        _world.Raycast(Vector2.Zero, Vector2.UnitY, 50).Should().BeNull();
        _world.Raycast(Vector2.Zero, -Vector2.UnitY, 500).Should().BeNull();
    }

    [Fact]
    public void Queries_FindBodiesByAreaAndPoint()
    {
        var box = _world.Add(new Body(new Box(20, 20), Vector2.Zero, BodyType.Static));
        var ball = _world.Add(new Body(new Circle(10), new Vector2(100, 0), BodyType.Static));

        _world.QueryArea(new Aabb(new Vector2(-5, -5), new Vector2(95, 5))).Should().BeEquivalentTo([box, ball]);
        _world.QueryPoint(new Vector2(108, 8)).Should().BeEmpty("the point is in the ball's bounds but outside the circle");
        _world.QueryPoint(new Vector2(105, 0)).Should().Equal(ball);
    }

    [Fact]
    public void RemovedBodies_StopColliding()
    {
        var floor = _world.Add(new Body(new Box(400, 20), new Vector2(0, 110), BodyType.Static));
        var crate = _world.Add(new Body(new Box(20, 20), new Vector2(0, 80)));
        Run(Second / 4);

        _world.Remove(floor).Should().BeTrue();
        Run(Second);

        crate.Position.Y.Should().BeGreaterThan(200);
        _world.Remove(floor).Should().BeFalse();
    }

    [Fact]
    public void ForcesAndImpulses_DependOnMass()
    {
        _world.Gravity = Vector2.Zero;
        var light = _world.Add(new Body(new Circle(5), Vector2.Zero) { Mass = 1 });
        var heavy = _world.Add(new Body(new Circle(5), new Vector2(0, 100)) { Mass = 4 });

        light.ApplyImpulse(new Vector2(40, 0));
        heavy.ApplyImpulse(new Vector2(40, 0));

        light.Velocity.X.Should().Be(40);
        heavy.Velocity.X.Should().Be(10);
    }

    [Fact]
    public void BadShapesAndMasses_AreRejected()
    {
        FluentActions.Invoking(() => new Circle(0)).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => new Box(0, 5)).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => new Body(new Circle(1), Vector2.Zero) { Mass = 0 }).Should().Throw<ArgumentOutOfRangeException>();
    }

    /// <summary>Plays <paramref name="time"/> as 60 frames a second.</summary>
    private void Run(TimeSpan time)
    {
        var frame = TimeSpan.FromSeconds(1.0 / 60);
        for (var i = 0; i < Math.Round(time.TotalSeconds * 60); i++)
        {
            _world.Update(frame);
        }
    }

    [Fact]
    public void ABodyBelongsToOneWorld()
    {
        var body = _world.Add(new Body(new Circle(1), Vector2.Zero));

        FluentActions.Invoking(() => new PhysicsWorld().Add(body)).Should().Throw<InvalidOperationException>();
    }
}
