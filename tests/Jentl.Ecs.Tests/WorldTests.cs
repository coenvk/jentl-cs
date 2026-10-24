namespace Jentl.Ecs.Tests;

public class WorldTests
{
    private readonly World _world = new();

    [Fact]
    public void Entities_AreCreatedAlive_AndDieOnce()
    {
        var a = _world.Create();
        var b = _world.Create();

        _world.Count.Should().Be(2);
        a.Should().NotBe(b);
        _world.Destroy(a);
        _world.Destroy(a);

        _world.IsAlive(a).Should().BeFalse();
        _world.IsAlive(b).Should().BeTrue();
        _world.Count.Should().Be(1);
    }

    [Fact]
    public void AReusedId_DoesNotBringTheOldHandleBackToLife()
    {
        var old = _world.Create();
        _world.Add(old, new Position(1, 1));
        _world.Destroy(old);

        var reused = _world.Create();

        reused.Id.Should().Be(old.Id);
        _world.IsAlive(old).Should().BeFalse();
        _world.Has<Position>(old).Should().BeFalse();
        _world.Has<Position>(reused).Should().BeFalse("destroying took the components with it");
    }

    [Fact]
    public void Components_AreAdded_Read_ChangedInPlace_AndRemoved()
    {
        var e = _world.Spawn().With(new Position(1, 2)).With(new Velocity(3, 0)).Entity;

        _world.Get<Velocity>(e).Should().Be(new Velocity(3, 0));
        _world.Get<Position>(e).X += 10;
        _world.Get<Position>(e).Should().Be(new Position(11, 2));

        _world.Remove<Velocity>(e).Should().BeTrue();
        _world.Has<Velocity>(e).Should().BeFalse();
        _world.TryGet<Velocity>(e, out _).Should().BeFalse();
        _world.Remove<Velocity>(e).Should().BeFalse();
    }

    [Fact]
    public void AddingAgain_Replaces()
    {
        var e = _world.Spawn().With(new Position(1, 1)).Entity;

        _world.Add(e, new Position(5, 5));

        _world.Get<Position>(e).Should().Be(new Position(5, 5));
        _world.CountOf<Position>().Should().Be(1);
    }

    [Fact]
    public void GettingAMissingComponent_Throws()
    {
        var e = _world.Create();

        var act = () => _world.Get<Position>(e);

        act.Should().Throw<KeyNotFoundException>();
    }

    [Fact]
    public void UsingADeadEntity_Throws()
    {
        var e = _world.Create();
        _world.Destroy(e);

        var act = () => _world.Add(e, new Position(0, 0));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Queries_FindEntitiesWithEveryType()
    {
        var moving = _world.Spawn().With(new Position(0, 0)).With(new Velocity(1, 0)).Entity;
        _world.Spawn().With(new Position(5, 5));
        _world.Spawn().With(new Velocity(2, 2));

        _world.Query<Position, Velocity>().Should().Equal(moving);
        _world.Query<Position>().Should().HaveCount(2);
        _world.Query<Position, Velocity, Tag>().Should().BeEmpty();
    }

    [Fact]
    public void ForEach_ChangesComponentsInPlace()
    {
        for (var i = 0; i < 3; i++)
        {
            _world.Spawn().With(new Position(i, 0)).With(new Velocity(10, 1));
        }

        _world.ForEach((Entity _, ref Position p, ref Velocity v) =>
        {
            p.X += v.X;
            p.Y += v.Y;
        });

        _world.Query<Position>().Select(e => _world.Get<Position>(e)).Should().BeEquivalentTo(
            [new Position(10, 1), new Position(11, 1), new Position(12, 1)]);
    }

    [Fact]
    public void DestroyingDuringAQuery_IsSafe()
    {
        var all = Enumerable.Range(0, 10).Select(i => _world.Spawn().With(new Position(i, 0)).Entity).ToList();
        var visited = 0;

        _world.ForEach((Entity e, ref Position p) =>
        {
            visited++;

            // Each visit destroys itself and the next entity, which must then be skipped.
            _world.Destroy(e);
            var next = all.FirstOrDefault(o => o.Id == e.Id + 1);
            _world.Destroy(next);
        });

        visited.Should().Be(5);
        _world.Count.Should().Be(0);
    }

    [Fact]
    public void RemovingSwapsTheLastComponentIn_WithoutMixingThemUp()
    {
        var entities = Enumerable.Range(0, 5).Select(i => _world.Spawn().With(new Position(i, i)).Entity).ToList();

        _world.Remove<Position>(entities[1]);
        _world.Remove<Position>(entities[3]);

        foreach (var i in new[] { 0, 2, 4 })
        {
            _world.Get<Position>(entities[i]).Should().Be(new Position(i, i));
        }
    }

    [Fact]
    public void FindOne_FindsTheOnlyOne()
    {
        _world.FindOne<Tag>().Should().Be(Entity.None);

        var player = _world.Spawn().With(new Tag()).Entity;

        _world.FindOne<Tag>().Should().Be(player);
    }

    [Fact]
    public void Resources_AreSharedByType()
    {
        _world.SetResource(new Score { Points = 3 });

        _world.Resource<Score>().Points.Should().Be(3);
        _world.TryResource<string>(out _).Should().BeFalse();
        var act = () => _world.Resource<string>();
        act.Should().Throw<KeyNotFoundException>();
    }

    [Fact]
    public void Events_QueueUntilDrained()
    {
        _world.Emit(new Hit(1));
        _world.Emit(new Hit(2));

        _world.Drain<Hit>().Should().Equal(new Hit(1), new Hit(2));
        _world.Drain<Hit>().Should().BeEmpty();
    }

    [Fact]
    public void ManyEntities_KeepTheirOwnComponents()
    {
        var entities = Enumerable.Range(0, 1000).Select(i => _world.Spawn().With(new Position(i, -i)).Entity).ToList();
        foreach (var e in entities.Where(e => e.Id % 3 == 0))
        {
            _world.Destroy(e);
        }

        _world.Count.Should().Be(666);
        foreach (var e in entities.Where(e => e.Id % 3 != 0))
        {
            _world.Get<Position>(e).Should().Be(new Position(e.Id, -e.Id));
        }
    }

    [Fact]
    public void Systems_RunInOrder_AndCanBeSwitchedOff()
    {
        var log = new List<string>();
        var systems = new SystemGroup()
            .Add("first", (_, _) => log.Add("first"))
            .Add("second", (_, _) => log.Add("second"));

        systems.Update(_world, TimeSpan.Zero);
        systems.Enable("first", false);
        systems.Update(_world, TimeSpan.Zero);

        log.Should().Equal("first", "second", "second");
        systems.Names.Should().Equal("first", "second");
    }

    private record struct Position(float X, float Y);

    private record struct Velocity(float X, float Y);

    private record struct Tag;

    private record struct Hit(int Damage);

    private sealed class Score
    {
        public int Points { get; set; }
    }
}
