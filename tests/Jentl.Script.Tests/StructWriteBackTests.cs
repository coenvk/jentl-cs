using System.Numerics;

namespace Jentl.Script.Tests;

public class StructWriteBackTests
{
    internal sealed class Thing
    {
        public Vector2 Position { get; set; }

        public Vector2 Velocity = Vector2.Zero;

        public Body Body { get; set; }

        public List<Vector2> Points { get; } = [Vector2.Zero, Vector2.Zero];

        public List<Body> Bodies { get; } = [new Body()];
    }

    internal struct Body
    {
        public Vector2 Centre { get; set; }

        public float Mass { get; set; }
    }

    [Fact]
    public void SettingAPartOfAStruct_PutsItBack_ForPropertiesAndFields()
    {
        var thing = new Thing { Position = new Vector2(1, 2) };
        var engine = new ScriptEngine();
        engine.Globals["thing"] = ScriptValue.From(thing);

        engine.Run("""
            thing.position.x = 5
            thing.position.y += 1
            thing.velocity.x = -3
            """);

        thing.Position.Should().Be(new Vector2(5, 3));
        thing.Velocity.Should().Be(new Vector2(-3, 0));
    }

    [Fact]
    public void SettingAPartDeeperDown_PutsEveryStructBack()
    {
        var thing = new Thing();
        var engine = new ScriptEngine();
        engine.Globals["thing"] = ScriptValue.From(thing);

        engine.Run("""
            thing.body.centre.x = 4
            thing.body.centre.y += 2
            thing.body.mass = 3
            thing.points[1].y = 7
            thing.bodies[0].centre.x += 2
            thing.bodies[0].mass = 5
            """);

        thing.Body.Centre.Should().Be(new Vector2(4, 2));
        thing.Body.Mass.Should().Be(3);
        thing.Points.Should().Equal(Vector2.Zero, new Vector2(0, 7));
        thing.Bodies[0].Centre.Should().Be(new Vector2(2, 0));
        thing.Bodies[0].Mass.Should().Be(5);
    }

    [Fact]
    public void ScriptListsAndMaps_SetDeepAsBefore()
    {
        var engine = new ScriptEngine();
        engine.Globals["result"] = ScriptValue.From(0);
        engine.Run("""
            let grid = [[1, 2], [3, 4]]
            grid[1][0] = 9
            grid[0][1] += 10
            let data = {inner: {list: [1, 2, 3]}}
            data.inner.list[2] = 30
            result = grid[1][0] + grid[0][1] + data.inner.list[2]
            """);

        engine.Globals["result"].AsNumber.Should().Be(9 + 12 + 30);
    }

    [Fact]
    public void ACopyKeptInAVariable_StaysTheScriptsOwn()
    {
        var thing = new Thing { Position = new Vector2(1, 2) };
        var engine = new ScriptEngine();
        engine.Globals["thing"] = ScriptValue.From(thing);

        engine.Run("""
            let p = thing.position
            p.x = 9
            """);

        thing.Position.Should().Be(new Vector2(1, 2));
    }
}
