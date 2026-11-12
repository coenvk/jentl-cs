using System.Numerics;

namespace Jentl.Script.Tests;

public class StructWriteBackTests
{
    internal sealed class Thing
    {
        public Vector2 Position { get; set; }

        public Vector2 Velocity = Vector2.Zero;

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
