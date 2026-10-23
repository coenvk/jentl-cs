using Jentl.Puzzle;
using Jentl.Rpg.Maps;
using Jentl.Rpg.World;
using Mood = Jentl.Rpg.World.SkittishBrain.Mood;

namespace Jentl.Rpg.Tests;

public class SkittishBrainTests
{
    [Fact]
    public void ItHunts_OnceItSpotsThePlayer()
    {
        var world = Fixtures.World("@....s");
        var (spider, brain) = Spider(world);

        brain.Current.Should().Be(Mood.Wandering);
        world.Wait();

        brain.Current.Should().Be(Mood.Hunting);
        brain.Machine.IsIn(Mood.Alert).Should().BeTrue();
        spider.Position.Should().Be(new Cell(4, 0));
    }

    [Fact]
    public void ItFlees_WhenBadlyHurt()
    {
        var world = Fixtures.World("@s........");
        var (spider, brain) = Spider(world);
        world.Wait();
        spider.Hp = 3;

        world.Wait();
        world.Wait();

        brain.Current.Should().Be(Mood.Fleeing);
        Pathfinder.Distance(spider.Position, world.Player.Position).Should().Be(3);
        world.Messages.Should().Contain("The Spider scuttles away!");
    }

    [Fact]
    public void AfterFleeingOutOfSight_ItRestsUntilHealed_ThenWandersAgain()
    {
        var world = Fixtures.World(
            "@s...........",
            "#############");
        var (spider, brain) = Spider(world);
        world.Wait();
        spider.Hp = 3;
        world.Wait();
        brain.Current.Should().Be(Mood.Fleeing);

        // Out of its sight (and the player stays put, so nothing finds it again).
        world.Player.Position = new Cell(0, 0);
        for (var i = 0; i < 8 && brain.Current == Mood.Fleeing; i++)
        {
            world.Wait();
        }

        brain.Current.Should().Be(Mood.Resting);
        var resting = spider.Hp;
        world.Wait();
        spider.Hp.Should().Be(resting + 1);

        for (var i = 0; i < 20 && brain.Current == Mood.Resting; i++)
        {
            world.Wait();
        }

        spider.Hp.Should().Be(spider.MaxHp);
        brain.Current.Should().Be(Mood.Wandering);
    }

    [Fact]
    public void ItsMachine_IsValid_AndDrawsAsADiagram()
    {
        var brain = new SkittishBrain();

        brain.Machine.Validate().Should().BeEmpty();
        brain.Machine.ToMermaid().Should().Contain("state Alert {\n        Hunting\n        Fleeing\n    }").And.Contain("Fleeing --> Resting : after 5s [out of sight]");
    }

    private static (Actor Spider, SkittishBrain Brain) Spider(RpgWorld world)
    {
        var spider = world.Area.Actors.Single(a => a.Kind == "spider");
        return (spider, (SkittishBrain)spider.Brain);
    }
}
