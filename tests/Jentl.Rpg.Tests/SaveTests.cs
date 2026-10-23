using Jentl.Puzzle;
using Jentl.Rpg.Quests;
using Jentl.Rpg.World;

namespace Jentl.Rpg.Tests;

public class SaveTests
{
    private static readonly string[] Cave =
    [
        "@$..#......",
        "....#..r..d",
        "e.........#",
    ];

    [Fact]
    public void SavingAndLoading_RestoresTheGame()
    {
        var world = Played();
        var save = world.Save();

        var loaded = RpgWorld.Load(Fixtures.Content(), Factory, save);

        loaded.Save().Should().Be(save, "a loaded game saves exactly as it was saved");
        loaded.Turn.Should().Be(world.Turn);
        loaded.Seed.Should().Be(7);
        loaded.Area.Id.Should().Be("cave");
        loaded.Player.Position.Should().Be(world.Player.Position);
        loaded.Player.Hp.Should().Be(world.Player.Hp);
        loaded.State.Inventory.Count("coins").Should().Be(3);
        loaded.State.Equipment["weapon"]!.Id.Should().Be("sword");
        loaded.State.Flags.Should().Contain("brave");
        loaded.State.Variable("gold").Should().Be(4);
        loaded.State.Quests["herbs"].Status.Should().Be(QuestStatus.Active);
        loaded.State.Quests["herbs"].Progress(0).Should().Be(1);
        loaded.Messages.Should().EndWith(world.Messages.TakeLast(3));
    }

    [Fact]
    public void Loading_KeepsWhatChangedInEachArea()
    {
        var world = Played();
        var rat = world.Area.Actors.Single(a => a.Kind == "rat");
        rat.Hp = 1;

        var loaded = RpgWorld.Load(Fixtures.Content(), Factory, world.Save());
        var area = loaded.Area;

        area.Items.Should().BeEmpty("the coins were picked up");
        area.Actors.Should().HaveCount(4, "the player, the elder, the rat and the dummy");
        var loadedRat = area.Actors.Single(a => a.Kind == "rat");
        loadedRat.Hp.Should().Be(1);
        loadedRat.Position.Should().Be(rat.Position);
        loadedRat.BrainName.Should().Be("hunter");
        area.Actors.Single(a => a.Kind == "elder").Tag.Should().Be("elder");
        area.Explored.Should().BeEquivalentTo(world.Area.Explored);
        area.IsVisible(loaded.Player.Position).Should().BeTrue("sight is worked out again on load");
        area.Triggers.Single().HasFired.Should().BeTrue();
    }

    [Fact]
    public void LoadedGames_CarryOn()
    {
        var loaded = RpgWorld.Load(Fixtures.Content(), Factory, Played().Save());

        var turn = loaded.Wait();

        turn.TookTurn.Should().BeTrue();
        loaded.Area.Actors.Should().Contain(a => a.Kind == "rat");
        loaded.Step(GridDirection.Down).TookTurn.Should().BeTrue();
    }

    [Fact]
    public void DefeatedMonsters_StayDefeated()
    {
        var world = new RpgWorld(Fixtures.Content(), (w, id) =>
        {
            var area = new Area(id, Fixtures.Map("@r."));
            area.Spawns["start"] = new Cell(0, 0);
            w.Spawn(area, "rat", new Cell(1, 0));
            return area;
        });
        world.Enter("den");
        world.State.Equipment.Equip(world.Content.Items["sword"]);
        world.Step(GridDirection.Right);

        var loaded = RpgWorld.Load(world.Content, (w, id) =>
        {
            var area = new Area(id, Fixtures.Map("@r."));
            area.Spawns["start"] = new Cell(0, 0);
            w.Spawn(area, "rat", new Cell(1, 0));
            return area;
        }, world.Save());

        loaded.Area.Actors.Should().ContainSingle(a => a == loaded.Player, "the factory's rat is replaced by the saved, empty, area");
        loaded.Area.ItemsAt(new Cell(1, 0)).Single().Item.Id.Should().Be("coins", "its loot is still there");
    }

    [Fact]
    public void UnknownVersions_AndBrokenSaves_AreRefused()
    {
        var content = Fixtures.Content();

        var future = () => RpgWorld.Load(content, Factory, """{ "version": 99 }""");
        var garbage = () => RpgWorld.Load(content, Factory, "not json");
        var missing = () => RpgWorld.Load(content, Factory, Played().Save().Replace("\"sword\"", "\"axe\"", StringComparison.Ordinal));

        future.Should().Throw<ContentException>().WithMessage("*version 99*");
        garbage.Should().Throw<ContentException>();
        missing.Should().Throw<ContentException>().WithMessage("*axe*");
    }

    /// <summary>A dark cave with a coin pile, a trigger, a rat, a dummy and the elder, played for a few turns.</summary>
    private static RpgWorld Played()
    {
        var world = new RpgWorld(Fixtures.Content(), Factory, seed: 7);
        world.Enter("cave");
        world.State.Give("sword");
        world.Equip("sword");
        world.State.Flags.Add("brave");
        world.State.AddVariable("gold", 4);
        world.State.StartQuest("herbs");
        world.State.Give("herb");
        world.Step(GridDirection.Right);
        world.Step(GridDirection.Down);
        world.Wait();
        return world;
    }

    private static Area Factory(RpgWorld world, string id)
    {
        var area = new Area(id, Fixtures.Map(Cave)) { IsLit = false };
        area.Spawns["start"] = new Cell(0, 0);
        world.Place(area, "coins", 3, new Cell(1, 0));
        world.Spawn(area, "rat", new Cell(7, 1));
        world.Spawn(area, "dummy", new Cell(10, 1));
        world.Spawn(area, "elder", new Cell(0, 2), "elder");
        area.Triggers.Add(new Trigger(new Cell(1, 0), null, [], repeat: false));
        return area;
    }
}
