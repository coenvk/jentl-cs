using System.Text.Json;
using Jentl.Core.Rpg;
using Jentl.Rpg;
using Jentl.Rpg.Items;
using Jentl.Rpg.Scripting;

namespace Jentl.Core.Tests;

public class RpgScriptsTests
{
    private static RpgState State()
    {
        var items = ItemCatalog.FromJson("""{ "items": [ { "id": "coin", "stack": 99 }, { "id": "key" } ] }""");
        var state = new RpgState(items, []);
        state.Scripts = new RpgScripts(state);
        return state;
    }

    private static Effect EffectOf(string json) => Effect.Parse(JsonDocument.Parse(json).RootElement, "test");

    private static Condition ConditionOf(string json) => Condition.Parse(JsonDocument.Parse(json).RootElement, "test");

    [Fact]
    public void ScriptEffects_ChangeTheState_AndScriptConditionsReadIt()
    {
        var state = State();
        var said = new List<string>();
        state.Said += said.Add;

        state.Run([EffectOf("""{ "script": "give(\"coin\", 5)\naddVar(\"visits\", 1)\nif has(\"coin\", 5) then setFlag(\"rich\") end\nsay(\"Coins: \" + str(var(\"visits\")))" }""")]);

        state.Inventory.Count("coin").Should().Be(5);
        state.Flags.Should().Contain("rich");
        said.Should().EndWith("Coins: 1");
        ConditionOf("""{ "script": "flag(\"rich\") and var(\"visits\") >= 1" }""").Test(state).Should().BeTrue();
        ConditionOf("""{ "script": "has(\"key\")" }""").Test(state).Should().BeFalse();
    }

    [Fact]
    public void WithoutScripts_AScriptSaysWhatIsMissing()
    {
        var state = new RpgState(ItemCatalog.FromJson("""{ "items": [] }"""), []);

        var act = () => ConditionOf("""{ "script": "true" }""").Test(state);

        act.Should().Throw<InvalidOperationException>().WithMessage("*RpgState.Scripts*");
    }
}
