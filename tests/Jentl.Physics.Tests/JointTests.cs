using System.Numerics;

namespace Jentl.Physics.Tests;

public class JointTests
{
    private readonly PhysicsWorld _world = new() { Gravity = new Vector2(0, 500) };

    [Fact]
    public void APendulum_SwingsOnItsRod_KeepingItsLength()
    {
        var bob = _world.Add(new Body(new Circle(4), new Vector2(100, 0)));
        _world.Add(new DistanceJoint(bob, Vector2.Zero));

        var lowest = 0f;
        for (var i = 0; i < 240; i++)
        {
            _world.Update(TimeSpan.FromSeconds(1.0 / 120));
            Vector2.Distance(bob.Position, Vector2.Zero).Should().BeApproximately(100, 1.5f);
            lowest = MathF.Max(lowest, bob.Position.Y);
        }

        lowest.Should().BeGreaterThan(95, "it swings down through the bottom");
    }

    [Fact]
    public void ARope_IsSlackUntilPulledStraight()
    {
        var weight = _world.Add(new Body(new Circle(4), new Vector2(0, 20)));
        var rope = (RopeJoint)_world.Add(new RopeJoint(weight, Vector2.Zero, maxLength: 60));

        Run(TimeSpan.FromSeconds(0.1));
        weight.Position.Y.Should().BeGreaterThan(20, "it falls freely while the rope is slack");
        rope.IsTaut.Should().BeFalse();

        Run(TimeSpan.FromSeconds(2));
        weight.Position.Y.Should().BeApproximately(60, 1.5f);
        rope.IsTaut.Should().BeTrue();

        // Thrown up, it rises freely: a rope never pushes.
        weight.Velocity = new Vector2(0, -300);
        _world.Step(1f / 120);
        weight.Velocity.Y.Should().BeLessThan(-290);
    }

    [Fact]
    public void ASpring_SettlesWhereItsPullMeetsGravity()
    {
        var weight = _world.Add(new Body(new Circle(4), new Vector2(0, 50)) { Mass = 2 });
        _world.Add(new SpringJoint(weight, Vector2.Zero, stiffness: 100, damping: 20, restLength: 50));

        Run(TimeSpan.FromSeconds(6));

        // Stretched until k x = m g: 100 x = 2 * 500.
        weight.Position.Y.Should().BeApproximately(60, 1);
        weight.Velocity.Length().Should().BeLessThan(1);
    }

    [Fact]
    public void TwoBodiesOnARod_MoveTogether()
    {
        _world.Gravity = Vector2.Zero;
        var a = _world.Add(new Body(new Circle(4), Vector2.Zero));
        var b = _world.Add(new Body(new Circle(4), new Vector2(30, 0)));
        _world.Add(new DistanceJoint(a, b));

        a.Velocity = new Vector2(-100, 0);
        Run(TimeSpan.FromSeconds(1));

        Vector2.Distance(a.Position, b.Position).Should().BeApproximately(30, 0.5f);
        b.Position.X.Should().BeLessThan(0, "a pulled b along");
    }

    [Fact]
    public void RemovingABody_TakesItsJoints()
    {
        var bob = _world.Add(new Body(new Circle(4), new Vector2(100, 0)));
        _world.Add(new DistanceJoint(bob, Vector2.Zero));

        _world.Remove(bob);

        _world.Joints.Should().BeEmpty();
    }

    [Fact]
    public void AJoint_NeedsItsBodiesInTheWorld() =>
        FluentActions.Invoking(() => _world.Add(new DistanceJoint(new Body(new Circle(1), Vector2.Zero), Vector2.One)))
            .Should().Throw<InvalidOperationException>();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AFastBody_GoesThroughAThinWall_UnlessItIsABullet(bool bullet)
    {
        _world.Gravity = Vector2.Zero;
        _world.Add(new Body(new Box(2, 200), new Vector2(100, 0), BodyType.Static));
        var shot = _world.Add(new Body(new Circle(2), Vector2.Zero) { IsBullet = bullet, Restitution = 0 });
        shot.Velocity = new Vector2(30_000, 0);

        Run(TimeSpan.FromSeconds(0.05));

        if (bullet)
        {
            shot.Position.X.Should().BeLessThan(100, "it stopped at the wall");
            shot.Velocity.X.Should().BeLessThanOrEqualTo(0);
        }
        else
        {
            shot.Position.X.Should().BeGreaterThan(100, "250 units a step jumps a wall 2 thick");
        }
    }

    [Fact]
    public void ABullet_BouncesOffWhatItHits()
    {
        _world.Gravity = Vector2.Zero;
        _world.Add(new Body(new Box(200, 2), new Vector2(0, 100), BodyType.Static) { Restitution = 1 });
        var ball = _world.Add(new Body(new Box(4, 4), Vector2.Zero) { IsBullet = true, Restitution = 1 });
        ball.Velocity = new Vector2(0, 20_000);

        _world.Step(1f / 120);

        ball.Position.Y.Should().BeLessThan(100);
        ball.Velocity.Y.Should().BeApproximately(-20_000, 1);
    }

    private void Run(TimeSpan time)
    {
        for (var t = TimeSpan.Zero; t < time; t += TimeSpan.FromSeconds(1.0 / 120))
        {
            _world.Update(TimeSpan.FromSeconds(1.0 / 120));
        }
    }
}
