namespace Jentl.Core.Tests;

public class PoolTests
{
    private sealed class Bullet
    {
        public float X { get; set; }

        public bool Spent { get; set; }
    }

    [Fact]
    public void Get_ReusesWhatWasReturned_CleanedByTheReset()
    {
        var pool = new Pool<Bullet>(() => new Bullet(), b => b.X = 0);
        var first = pool.Get();
        first.X = 50;

        pool.Return(first);
        var again = pool.Get();

        again.Should().BeSameAs(first);
        again.X.Should().Be(0);
        pool.Created.Should().Be(1);
    }

    [Fact]
    public void ReturningTwice_Throws()
    {
        var pool = new Pool<Bullet>(() => new Bullet());
        var bullet = pool.Get();
        pool.Return(bullet);

        FluentActions.Invoking(() => pool.Return(bullet)).Should().Throw<InvalidOperationException>();
        pool.Get();
        FluentActions.Invoking(() => pool.Return(bullet)).Should().NotThrow("taken out again, it may come back again");
    }

    [Fact]
    public void Prewarm_MakesThemUpFront_AndMaxFreeCapsWhatIsKept()
    {
        var pool = new Pool<Bullet>(() => new Bullet(), maxFree: 3);

        pool.Prewarm(10);
        pool.Free.Should().Be(3);
        pool.Created.Should().Be(3);

        var taken = Enumerable.Range(0, 5).Select(_ => pool.Get()).ToList();
        taken.ForEach(pool.Return);
        pool.Free.Should().Be(3);
        pool.Created.Should().Be(5);
    }

    [Fact]
    public void ActivePool_SpawnsAndLetsGo_ReusingTheSpent()
    {
        var bullets = new ActivePool<Bullet>(() => new Bullet(), b => b.Spent = false);
        for (var i = 0; i < 5; i++)
        {
            bullets.Spawn().X = i;
        }

        foreach (var bullet in bullets)
        {
            bullet.Spent = bullet.X % 2 == 0;
        }

        bullets.DespawnWhere(b => b.Spent).Should().Be(3);
        bullets.Select(b => b.X).Should().BeEquivalentTo([1f, 3f]);

        bullets.Spawn().Spent.Should().BeFalse("it came back reset");
        bullets.Pool.Created.Should().Be(5);
        bullets.Despawn(bullets[0]).Should().BeTrue();
        bullets.Despawn(new Bullet()).Should().BeFalse();
        bullets.Clear();
        bullets.Count.Should().Be(0);
        bullets.Pool.Free.Should().Be(5);
    }
}
