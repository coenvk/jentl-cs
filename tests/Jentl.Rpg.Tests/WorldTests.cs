using Jentl.Puzzle;
using Jentl.Rpg.World;

namespace Jentl.Rpg.Tests;

public class WorldTests
{
    [Fact]
    public void Stepping_MovesThePlayer_AndWallsBlockWithoutTakingATurn()
    {
        var world = Fixtures.World(
            "#####",
            "#@..#",
            "#####");

        var step = world.Step(GridDirection.Right);
        step.TookTurn.Should().BeTrue();
        step.Events.OfType<ActorMoved>().Should().ContainSingle().Which.To.Should().Be(new Cell(2, 1));

        world.Step(GridDirection.Up).Should().Be(TurnResult.Nothing);
        world.Turn.Should().Be(1);
    }

    [Fact]
    public void WalkingOverItems_PicksThemUp()
    {
        var world = Fixtures.World("@$.");

        world.Step(GridDirection.Right).Events.Should().ContainSingle(e => e is ItemPickedUp);

        world.State.Inventory.Count("coins").Should().Be(3);
        world.Area.Items.Should().BeEmpty();
        world.Messages.Should().Contain("You pick up 3 x Coins.");
    }

    [Fact]
    public void BumpingAMonster_Attacks_AndDefeatingItDropsLootAndGivesXp()
    {
        var world = Fixtures.World("@r");
        var rat = world.Area.Actors.Single(a => a.Kind == "rat");
        world.State.Equipment.Equip(world.Content.Items["sword"]);

        world.Step(GridDirection.Right).Events.Should().Contain(e => e is ActorAttacked);

        rat.IsAlive.Should().BeFalse("2 + 3 attack, minus at most 1, beats 3 health");
        world.Area.Actors.Should().NotContain(rat);
        world.Area.ItemsAt(new Cell(1, 0)).Single().Item.Id.Should().Be("coins");
        world.Player.Level.Should().Be(2, "10 xp is a level");
        world.Player.MaxHp.Should().Be(25);
    }

    [Fact]
    public void Hunters_ChaseAndAttackThePlayer()
    {
        var world = Fixtures.World(
            "@....r");
        var rat = world.Area.Actors.Single(a => a.Kind == "rat");

        world.Wait();
        rat.Position.Should().Be(new Cell(4, 0));
        world.Wait();
        world.Wait();
        world.Wait();
        rat.Position.Should().Be(new Cell(1, 0));
        var hit = world.Wait();

        hit.Events.OfType<ActorAttacked>().Should().ContainSingle(a => a.Target == world.Player);
        world.Player.Hp.Should().BeLessThan(world.Player.MaxHp);
    }

    [Fact]
    public void Hunters_DoNotSeeThroughWalls()
    {
        var world = Fixtures.World(
            "@.#...r",
            "..#....",
            "..#....");
        var rat = world.Area.Actors.Single(a => a.Kind == "rat");

        for (var i = 0; i < 10; i++)
        {
            world.Wait();
        }

        Pathfinder(rat.Position, world.Player.Position).Should().BeGreaterThan(2);
    }

    [Fact]
    public void TheGame_EndsWhenThePlayerFalls()
    {
        var world = Fixtures.World("@r");
        world.Player.Hp = 1;

        while (!world.IsOver)
        {
            world.Wait();
        }

        world.Messages.Should().Contain("You have fallen...");
        world.Wait().Should().Be(TurnResult.Nothing);
    }

    [Fact]
    public void BumpingSomeoneWithADialogue_OffersToTalk_AndTalkingCountsForQuests()
    {
        var world = Fixtures.World("@e");
        var elder = world.Area.Actors.Single(a => a.Kind == "elder");

        var bump = world.Step(GridDirection.Right);

        bump.TalkTo.Should().Be(elder);
        bump.TookTurn.Should().BeFalse();
        var talk = world.Talk(elder);
        talk.Current!.Text.Should().Be("Hello.");
    }

    [Fact]
    public void UsingAndEquippingItems_TakeTurns()
    {
        var world = Fixtures.World("@.");
        world.State.Give("potion", 2);
        world.State.Give("sword");
        world.Player.Hp = 10;

        world.Use("potion").TookTurn.Should().BeTrue();
        world.Player.Hp.Should().Be(15);
        world.State.Inventory.Count("potion").Should().Be(1);

        world.Equip("sword").TookTurn.Should().BeTrue();
        world.AttackOf(world.Player).Should().Be(5);
        world.State.Inventory.Has("sword").Should().BeFalse();

        world.Unequip("weapon");
        world.AttackOf(world.Player).Should().Be(2);
        world.State.Inventory.Has("sword").Should().BeTrue();

        world.Use("coins").Should().Be(TurnResult.Nothing, "coins cannot be used");
    }

    [Fact]
    public void DarkAreas_ShowOnlyWhatThePlayerSees_AndRememberIt()
    {
        var world = new RpgWorld(Fixtures.Content(), (w, id) =>
        {
            var area = new Area(id, Fixtures.Map(
                "..........#..........",
                "..........#.........."))
            { IsLit = false };
            area.Spawns["start"] = new Cell(8, 0);
            return area;
        });
        world.Enter("cave");

        world.Area.IsVisible(new Cell(8, 1)).Should().BeTrue();
        world.Area.IsVisible(new Cell(12, 0)).Should().BeFalse("behind the wall");
        world.Area.IsVisible(new Cell(0, 0)).Should().BeFalse("further than the player sees");

        world.Step(GridDirection.Left);
        world.Step(GridDirection.Left);
        world.Area.IsExplored(new Cell(9, 0)).Should().BeTrue();
        world.Area.IsVisible(new Cell(0, 0)).Should().BeTrue();
    }

    [Fact]
    public void Portals_TravelBetweenAreas_WhichKeepTheirState()
    {
        var created = new List<string>();
        var world = new RpgWorld(Fixtures.Content(), (w, id) =>
        {
            created.Add(id);
            var area = new Area(id, Fixtures.Map("...."));
            area.Spawns["start"] = new Cell(0, 0);
            area.Spawns["back"] = new Cell(1, 0);
            area.Portals.Add(new Portal(new Cell(2, 0), id == "a" ? "b" : "a", "back"));
            return area;
        });
        world.Enter("a");

        world.Step(GridDirection.Right);
        var travel = world.Step(GridDirection.Right);

        travel.Events.OfType<AreaEntered>().Single().Area.Id.Should().Be("b");
        world.Player.Position.Should().Be(new Cell(1, 0));
        world.Step(GridDirection.Right);
        world.Area.Id.Should().Be("a");
        created.Should().Equal("a", "b");
    }

    [Fact]
    public void Actors_NeverStepOntoPortals()
    {
        var world = Fixtures.World("@.r");
        world.Area.Portals.Add(new Portal(new Cell(1, 0), "elsewhere", "start"));
        var rat = world.Area.Actors.Single(a => a.Kind == "rat");

        world.Wait();
        world.Wait();

        rat.Position.Should().Be(new Cell(2, 0), "the only way to the player is through the doorway");
    }

    [Fact]
    public void LoadArea_ReadsMapObjects()
    {
        const string tsx = """
            <tileset name="t" tilewidth="16" tileheight="16" tilecount="1" columns="1"><image source="t.png" width="16" height="16"/></tileset>
            """;
        const string tmx = """
            <map orientation="orthogonal" width="4" height="1" tilewidth="16" tileheight="16">
             <tileset firstgid="1" source="t.tsx"/>
             <layer name="ground" width="4" height="1"><data encoding="csv">1,1,1,1</data></layer>
             <objectgroup name="o">
              <object name="start" type="spawn" x="0" y="0"/>
              <object name="wise_one" type="actor" x="16" y="0"><properties>
               <property name="kind" value="elder"/><property name="name" value="Old Tam"/></properties></object>
              <object type="item" x="32" y="0"><properties><property name="item" value="herb"/><property name="count" type="int" value="2"/></properties></object>
              <object type="trigger" x="32" y="0"><properties><property name="do" value="[{&quot;setFlag&quot;: &quot;stepped&quot;}]"/></properties></object>
              <object type="portal" x="48" y="0"><properties><property name="area" value="cave"/></properties></object>
             </objectgroup>
            </map>
            """;
        var map = Jentl.Rpg.Maps.TmxReader.ReadMap(tmx, _ => tsx);
        var content = Fixtures.Content();
        var world = new RpgWorld(content, (w, id) => w.LoadArea(id, map));

        ContentValidator.Validate(content, map, "village").Should().BeEmpty();
        world.Enter("village");
        var area = world.Area;

        area.Spawns["start"].Should().Be(new Cell(0, 0));
        area.Actors.Single(a => a.Kind == "elder").Should().Match<Actor>(a => a.Tag == "wise_one" && a.Name == "Old Tam" && a.Dialogue == "elder");
        area.ItemsAt(new Cell(2, 0)).Single().Count.Should().Be(2);
        area.Portals.Single().Should().Be(new Portal(new Cell(3, 0), "cave", "start"));
        area.Triggers.Should().ContainSingle();
    }

    private static int Pathfinder(Cell a, Cell b) => Jentl.Rpg.Maps.Pathfinder.Distance(a, b);
}
