using Jentl.Core.Diagnostics;
using Jentl.Ecs;

namespace Jentl.Core.Tests;

public class EntityInspectionTests
{
    private struct Position
    {
        public float X;
        public float Y;
    }

    private sealed class Health
    {
        public int Points { get; set; } = 10;
    }

    [Fact]
    public void Entities_ListTheirComponents_AndEditsToAStructGoBackIntoTheWorld()
    {
        var world = new World();
        var hero = world.Spawn().With(new Position { X = 1, Y = 2 }).With(new Health()).Entity;
        world.Spawn().With(new Position());

        var things = Inspectable.Entities(world).ToList();

        things.Should().HaveCount(2);
        var components = InspectedMember.Of(things[0].Name, things[0].Target).Children();
        components.Select(c => c.Name).Should().BeEquivalentTo("Position", "Health");

        var x = components.Single(c => c.Name == "Position").Children().Single(m => m.Name == "X");
        x.Set(7f).Should().BeTrue();
        world.Get<Position>(hero).X.Should().Be(7);

        var points = components.Single(c => c.Name == "Health").Children().Single(m => m.Name == "Points");
        points.Set(3).Should().BeTrue();
        world.Get<Health>(hero).Points.Should().Be(3);
    }
}
