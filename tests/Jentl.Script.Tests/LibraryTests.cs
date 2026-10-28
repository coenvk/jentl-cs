namespace Jentl.Script.Tests;

public class LibraryTests
{
    private readonly ScriptEngine _engine = new(seed: 7);

    private string Eval(string expression) => _engine.Evaluate(expression).ToString();

    [Theory]
    [InlineData("str(3) + num(\"4.5\")", "34.5")]
    [InlineData("num(\"x\")", "nil")]
    [InlineData("type([]) + type({}) + type(1) + type(\"\") + type(nil) + type(true) + type(print)", "listmapnumbertextnilboolfunction")]
    [InlineData("upper(\"ab\") + lower(\"CD\") + trim(\"  e \")", "ABcde")]
    [InlineData("split(\"a  b c\")", "[\"a\", \"b\", \"c\"]")]
    [InlineData("split(\"a,,b\", \",\")", "[\"a\", \"\", \"b\"]")]
    [InlineData("join([1, 2, 3], \"-\")", "1-2-3")]
    [InlineData("replace(\"aXa\", \"a\", \"b\")", "bXb")]
    [InlineData("[floor(1.7), ceil(1.2), round(2.5), round(1.234, 2), abs(-3), sqrt(9)]", "[1, 2, 3, 1.23, 3, 3]")]
    [InlineData("[min(3, 1, 2), max([4, 9]), clamp(12, 0, 10), lerp(10, 20, 0.25), sign(-4)]", "[1, 9, 10, 12.5, -1]")]
    [InlineData("[len([1, 2]), len({a: 1}), len(\"abc\"), len(range(2, 10, 3))]", "[2, 1, 3, 3]")]
    [InlineData("[contains([1, 2], 2), contains({a: 1}, \"b\"), contains(\"hello\", \"ell\")]", "[true, false, true]")]
    [InlineData("[indexOf([5, 6], 6), indexOf(\"abc\", \"c\"), indexOf([], 1)]", "[1, 2, -1]")]
    [InlineData("[keys({a: 1, b: 2}), values({a: 1, b: 2})]", "[[\"a\", \"b\"], [1, 2]]")]
    [InlineData("slice([1, 2, 3, 4], 1, -1)", "[2, 3]")]
    [InlineData("sort([3, \"b\", 1, \"a\"])", "[1, 3, \"a\", \"b\"]")]
    [InlineData("sort([\"ccc\", \"a\", \"bb\"], fn(s) return len(s) end)", "[\"a\", \"bb\", \"ccc\"]")]
    [InlineData("map(range(4), fn(x) return x * x end)", "[0, 1, 4, 9]")]
    [InlineData("filter([1, 2, 3, 4], fn(x) return x % 2 == 0 end)", "[2, 4]")]
    [InlineData("[startsWith(\"hello\", \"he\"), endsWith(\"hello\", \"lo\")]", "[true, true]")]
    public void TheStandardFunctions(string expression, string expected) =>
        Eval(expression).Should().Be(expected);

    [Fact]
    public void ListChanges()
    {
        var script = _engine.Run("""
            let list = [1, 2, 3]
            push(list, 4, 5)
            let last = pop(list)
            insert(list, 0, 0)
            insert(list, -1, 9)
            let second = remove(list, 1)
            let other = copy(list)
            push(other, 7)
            let map = {a: 1, b: 2}
            let a = remove(map, "a")
            """);

        script["list"].ToString().Should().Be("[0, 2, 3, 4, 9]");
        script["last"].AsNumber.Should().Be(5);
        script["second"].AsNumber.Should().Be(1);
        script["other"].AsList.Count.Should().Be(6);
        script["map"].ToString().Should().Be("{b: 2}");
        script["a"].AsNumber.Should().Be(1);
    }

    [Fact]
    public void Chance_RepeatsWithTheSameSeed()
    {
        var first = new ScriptEngine(seed: 3).Evaluate("[random(), random(6), random(1, 3), chance(0.5), pick([\"a\", \"b\", \"c\"]), shuffle([1, 2, 3, 4])]").ToString();
        var again = new ScriptEngine(seed: 3).Evaluate("[random(), random(6), random(1, 3), chance(0.5), pick([\"a\", \"b\", \"c\"]), shuffle([1, 2, 3, 4])]").ToString();

        first.Should().Be(again);
        var rolls = Enumerable.Range(0, 200).Select(_ => _engine.Evaluate("random(1, 3)").AsNumber).ToList();
        rolls.Should().OnlyContain(roll => roll >= 1 && roll <= 3).And.Contain([1, 2, 3]);
    }

    [Fact]
    public void Ranges_CountUpOrDown()
    {
        Eval("map(range(3), fn(x) return x end)").Should().Be("[0, 1, 2]");
        Eval("map(range(5, 0, -2), fn(x) return x end)").Should().Be("[5, 3, 1]");
        Eval("map(range(0, 1, 0.25), fn(x) return x end)").Should().Be("[0, 0.25, 0.5, 0.75]");
        Eval("range(2, 4)").Should().Be("range(2, 4)");
    }

    [Fact]
    public void Evaluate_IsCompiledOnce_AndSeesTheGamesGlobals()
    {
        _engine["gold"] = 12;

        _engine.Evaluate("gold >= 10").AsLogical.Should().BeTrue();
        _engine["gold"] = 3;
        _engine.Evaluate("gold >= 10").AsLogical.Should().BeFalse();
    }
}
