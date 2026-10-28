namespace Jentl.Script.Tests;

public class LanguageTests
{
    private static ScriptValue Run(string source)
    {
        var engine = new ScriptEngine(seed: 1);
        return engine.Load(source, "test").Run();
    }

    private static string Printed(string source)
    {
        var engine = new ScriptEngine(seed: 1);
        var lines = new List<string>();
        engine.Print = lines.Add;
        engine.Load(source, "test").Run();
        return string.Join("\n", lines);
    }

    [Theory]
    [InlineData("return 1 + 2 * 3", 7)]
    [InlineData("return (1 + 2) * 3", 9)]
    [InlineData("return 7 % 3", 1)]
    [InlineData("return -7 % 3", 2)]
    [InlineData("return 10 / 4", 2.5)]
    [InlineData("return -2 * -3", 6)]
    [InlineData("return 1_000 + .5", 1000.5)]
    [InlineData("return 2e3", 2000)]
    public void Arithmetic_FollowsTheUsualOrder(string source, double expected) =>
        Run(source).AsNumber.Should().Be(expected);

    [Theory]
    [InlineData("return 1 < 2 and 2 <= 2", true)]
    [InlineData("return 3 > 4 or 4 >= 5", false)]
    [InlineData("return not nil", true)]
    [InlineData("return \"a\" < \"b\"", true)]
    [InlineData("return 1 == 1 and \"x\" != \"y\"", true)]
    [InlineData("return [1] == [1]", false)]
    public void Comparisons_AndLogic(string source, bool expected) =>
        Run(source).AsLogical.Should().Be(expected);

    [Fact]
    public void AndOr_GiveOneOfTheirOperands()
    {
        Run("return nil or \"fallback\"").AsText.Should().Be("fallback");
        Run("return 0 and \"zero is true\"").AsText.Should().Be("zero is true");
        Run("return false and error(\"never\")").AsLogical.Should().BeFalse();
    }

    [Fact]
    public void Text_JoinsWithPlus_AndFillsInBraces()
    {
        Run("let name = \"Mira\"\nlet gold = 12\nreturn \"{name} has {gold * 2} gold\"").AsText.Should().Be("Mira has 24 gold");
        Run("return \"hp: \" + 3.5").AsText.Should().Be("hp: 3.5");
        Run("return 'single {quotes} are plain'").AsText.Should().Be("single {quotes} are plain");
        Run("return \"a\\{b\\}\\n\"").AsText.Should().Be("a{b}\n");
        Run("let m = {a: 1}\nreturn \"{m.a + 1}\"").AsText.Should().Be("2");
    }

    [Fact]
    public void IfElifElse_TakesTheFirstBranchThatHolds()
    {
        const string Source = """
            fn grade(score)
              if score >= 90 then return "A"
              elif score >= 70 then return "B"
              else return "C"
              end
            end
            return grade(95) + grade(75) + grade(10)
            """;

        Run(Source).AsText.Should().Be("ABC");
    }

    [Fact]
    public void Loops_GoThroughListsMapsTextAndRanges()
    {
        const string Source = """
            let total = 0
            for i in range(1, 5) do total += i end
            for x in [10, 20] do total += x end
            for i, x in [100, 200] do total += i * x end
            let keys = ""
            for k, v in {a: 1, b: 2} do keys += k + v end
            for k in {c: 3} do keys += k end
            for c in "xy" do keys += c end
            return "{total} {keys}"
            """;

        Run(Source).AsText.Should().Be("240 a1b2cxy");
    }

    [Fact]
    public void BreakAndContinue_WorkInBothLoops()
    {
        const string Source = """
            let seen = []
            for i in range(10) do
              if i % 2 == 1 then continue end
              if i > 6 then break end
              push(seen, i)
            end
            let n = 0
            while true do
              n += 1
              if n < 3 then continue end
              break
            end
            push(seen, n)
            return seen
            """;

        Run(Source).ToString().Should().Be("[0, 2, 4, 6, 3]");
    }

    [Fact]
    public void Functions_CloseOverTheirVariables()
    {
        const string Source = """
            fn counter()
              let count = 0
              return fn()
                count += 1
                return count
              end
            end
            let a = counter()
            let b = counter()
            a()
            a()
            b()
            return [a(), b()]
            """;

        Run(Source).ToString().Should().Be("[3, 2]");
    }

    [Fact]
    public void Functions_CanCallThemselves()
    {
        Run("fn fib(n)\n if n < 2 then return n end\n return fib(n - 1) + fib(n - 2)\nend\nreturn fib(20)").AsNumber.Should().Be(6765);
        Run("fn outer()\n fn fact(n)\n  if n <= 1 then return 1 end\n  return n * fact(n - 1)\n end\n return fact(5)\nend\nreturn outer()").AsNumber.Should().Be(120);
    }

    [Fact]
    public void ListsAndMaps_ReadAndWrite()
    {
        const string Source = """
            let hero = {name: "Ash", "max hp": 20, stats: {str: 3}, bag: ["rope"]}
            hero.stats.str += 2
            hero["max hp"] -= 5
            hero.bag[0] = "torch"
            push(hero.bag, "map")
            hero.level = 2
            hero.name = nil
            return [hero.stats.str, hero["max hp"], hero.bag, hero.bag[-1], hero.level, hero.name, len(hero)]
            """;

        Run(Source).ToString().Should().Be("[5, 15, [\"torch\", \"map\"], \"map\", 2, nil, 4]");
    }

    [Fact]
    public void Values_PrintReadably()
    {
        Printed("print(1, 2.5, \"x\", true, nil, [1, \"a\"], {a: 1, \"b c\": [2]})")
            .Should().Be("1 2.5 x true nil [1, \"a\"] {a: 1, \"b c\": [2]}");
    }

    [Fact]
    public void ALineStartingWithAParenthesis_StartsANewStatement()
    {
        const string Source = """
            let log = []
            let f = fn(x) push(log, x) end
            f(1)
            (f)(2)
            return log
            """;

        Run(Source).ToString().Should().Be("[1, 2]");
    }

    [Fact]
    public void BlockLocals_StayInTheirBlock()
    {
        var act = () => Run("if true then let inner = 1 end\nreturn inner");

        act.Should().Throw<ScriptException>().WithMessage("test:2: unknown name 'inner'");
    }

    [Fact]
    public void TopLevelDeclarations_BecomeTheScriptsGlobals()
    {
        var engine = new ScriptEngine();
        var script = engine.Run("let gold = 5\nfn earn(n)\n gold += n\n return gold\nend", "shop");

        script.Call("earn", 3).AsNumber.Should().Be(8);
        script["gold"].AsNumber.Should().Be(8);
        script.Evaluate("gold * 2").AsNumber.Should().Be(16);
    }
}
